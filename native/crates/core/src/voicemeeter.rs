#![allow(unsafe_code)]
//! Live level metering for Voicemeeter's virtual devices, via `VoicemeeterRemote.dll`.
//!
//! WASAPI loopback doesn't work on Voicemeeter's virtual render endpoints — confirmed
//! empirically (see the investigation that led here): Voicemeeter's driver keeps a synthetic
//! buffer stream continuously "alive" for WASAPI's benefit (so loopback clients never see the
//! `AUDCLNT_BUFFERFLAGS_SILENT` flag and always get a full, correctly-paced stream of buffers),
//! but never actually writes real submitted audio into that buffer — the real mixing happens
//! through Voicemeeter's own internal engine, a path the standard shared-mode audio engine
//! (and thus loopback) never sees. `VBVMR_GetLevel` reads Voicemeeter's own internal meter
//! values directly instead, sidestepping the limitation entirely.
//!
//! This module is only ever exercised for devices whose friendly name indicates they're a
//! Voicemeeter endpoint — everything else keeps using the existing WASAPI/CPAL paths in
//! `audio.rs` unchanged.

use std::path::{Path, PathBuf};
use std::sync::{Mutex, OnceLock};
use windows::core::{PCSTR, PCWSTR};
use windows::Win32::Foundation::{CloseHandle, FreeLibrary, HMODULE, MAX_PATH};
use windows::Win32::System::LibraryLoader::{GetProcAddress, LoadLibraryW};
use windows::Win32::System::Registry::{
    RegCloseKey, RegEnumKeyExW, RegOpenKeyExW, RegQueryValueExW, HKEY, HKEY_LOCAL_MACHINE,
    KEY_READ, REG_SZ,
};

// ── Process detection ───────────────────────────────────────────────────────

/// True if any running process's name starts with "voicemeeter" (case-insensitive) — covers
/// `voicemeeter.exe` (Basic), `voicemeeterpro.exe` (Banana), `voicemeeter8.exe` (Potato), and
/// any future edition, without needing to enumerate exact names.
pub fn is_voicemeeter_running() -> bool {
    use windows::Win32::System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W,
        TH32CS_SNAPPROCESS,
    };

    unsafe {
        let Ok(snapshot) = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) else {
            return false;
        };

        let mut entry = PROCESSENTRY32W {
            dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
            ..Default::default()
        };

        let mut found = false;
        if Process32FirstW(snapshot, &mut entry).is_ok() {
            loop {
                let name_len = entry.szExeFile.iter().position(|&c| c == 0).unwrap_or(entry.szExeFile.len());
                let name = String::from_utf16_lossy(&entry.szExeFile[..name_len]);
                if name.to_ascii_lowercase().starts_with("voicemeeter") {
                    found = true;
                    break;
                }
                if Process32NextW(snapshot, &mut entry).is_err() {
                    break;
                }
            }
        }

        let _ = CloseHandle(snapshot);
        found
    }
}

// ── DLL discovery ────────────────────────────────────────────────────────────

/// Locates `VoicemeeterRemote64.dll` (preferred, matches our x64 build) or
/// `VoicemeeterRemote.dll`, first via the registered install directory (read from the
/// Uninstall entry's `UninstallString`, since VB-Audio doesn't register a dedicated
/// "install path" value anywhere more direct), falling back to the well-known default
/// install location if that lookup fails for any reason (portable install, registry quirk).
pub fn find_remote_dll() -> Option<PathBuf> {
    if let Some(dir) = find_install_dir_from_registry() {
        if let Some(p) = pick_dll_in_dir(&dir) {
            return Some(p);
        }
    }

    pick_dll_in_dir(Path::new(r"C:\Program Files (x86)\VB\Voicemeeter"))
}

fn pick_dll_in_dir(dir: &Path) -> Option<PathBuf> {
    for name in ["VoicemeeterRemote64.dll", "VoicemeeterRemote.dll"] {
        let p = dir.join(name);
        if p.exists() {
            return Some(p);
        }
    }
    None
}

fn find_install_dir_from_registry() -> Option<PathBuf> {
    const UNINSTALL_KEYS: &[&str] = &[
        r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    for &uninstall_key in UNINSTALL_KEYS {
        for subkey_name in reg_enum_subkeys(HKEY_LOCAL_MACHINE, uninstall_key) {
            let full_subkey = format!("{uninstall_key}\\{subkey_name}");
            let Some(display_name) = reg_read_string(HKEY_LOCAL_MACHINE, &full_subkey, "DisplayName") else {
                continue;
            };
            if !display_name.to_ascii_lowercase().contains("voicemeeter") {
                continue;
            }
            if let Some(uninstall_string) = reg_read_string(HKEY_LOCAL_MACHINE, &full_subkey, "UninstallString") {
                if let Some(dir) = Path::new(&uninstall_string).parent() {
                    return Some(dir.to_path_buf());
                }
            }
        }
    }
    None
}

fn to_wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Enumerates the immediate subkey names of `hive\subkey`. Returns an empty Vec on any error
/// (key doesn't exist, access denied, etc.) — registry probing is inherently best-effort here.
fn reg_enum_subkeys(hive: HKEY, subkey: &str) -> Vec<String> {
    unsafe {
        let mut hkey = HKEY::default();
        let subkey_w = to_wide(subkey);
        if RegOpenKeyExW(hive, PCWSTR(subkey_w.as_ptr()), Some(0), KEY_READ, &mut hkey).is_err() {
            return Vec::new();
        }

        let mut names = Vec::new();
        let mut index = 0u32;
        loop {
            let mut name_buf = [0u16; 256];
            let mut name_len = name_buf.len() as u32;
            let result = RegEnumKeyExW(
                hkey, index, Some(windows::core::PWSTR(name_buf.as_mut_ptr())), &mut name_len,
                None, Some(windows::core::PWSTR::null()), None, None,
            );
            if result.is_err() {
                break;
            }
            names.push(String::from_utf16_lossy(&name_buf[..name_len as usize]));
            index += 1;
        }

        let _ = RegCloseKey(hkey);
        names
    }
}

/// Reads a single REG_SZ value from `hive\subkey\value_name`. `None` on any error or type
/// mismatch — same best-effort contract as `reg_enum_subkeys`.
fn reg_read_string(hive: HKEY, subkey: &str, value_name: &str) -> Option<String> {
    unsafe {
        let mut hkey = HKEY::default();
        let subkey_w = to_wide(subkey);
        if RegOpenKeyExW(hive, PCWSTR(subkey_w.as_ptr()), Some(0), KEY_READ, &mut hkey).is_err() {
            return None;
        }

        let value_w = to_wide(value_name);
        let mut value_type = REG_SZ;
        let mut data = vec![0u8; (MAX_PATH as usize) * 2];
        let mut data_len = data.len() as u32;
        let result = RegQueryValueExW(
            hkey, PCWSTR(value_w.as_ptr()), None, Some(&mut value_type),
            Some(data.as_mut_ptr()), Some(&mut data_len),
        );
        let _ = RegCloseKey(hkey);

        if result.is_err() || value_type != REG_SZ {
            return None;
        }

        let u16_data: Vec<u16> = data[..data_len as usize]
            .chunks_exact(2)
            .map(|b| u16::from_le_bytes([b[0], b[1]]))
            .collect();
        let end = u16_data.iter().position(|&c| c == 0).unwrap_or(u16_data.len());
        Some(String::from_utf16_lossy(&u16_data[..end]))
    }
}

// ── FFI surface ──────────────────────────────────────────────────────────────
//
// Signatures transcribed from VB-Audio's official VoicemeeterRemote.h (Voicemeeter-SDK on
// GitHub, vburel2018/Voicemeeter-SDK). `long` is 32-bit on Windows (LLP64) -> i32.

type FnLogin = unsafe extern "system" fn() -> i32;
type FnLogout = unsafe extern "system" fn() -> i32;
type FnGetVoicemeeterType = unsafe extern "system" fn(*mut i32) -> i32;
type FnGetLevel = unsafe extern "system" fn(i32, i32, *mut f32) -> i32;

/// GetLevel's `nType` — see VoicemeeterRemote.h's "Get levels" section.
const VBVMR_LEVEL_TYPE_PRE_FADER_INPUT: i32 = 0;

struct Bindings {
    module: HMODULE,
    logout: FnLogout,
    get_voicemeeter_type: FnGetVoicemeeterType,
    get_level: FnGetLevel,
}

// SAFETY: these are plain function pointers into a DLL loaded once and never mutated; all
// actual calls are serialized through SESSION's Mutex (VBVMR's own docs require several of
// these to be called from one thread only).
unsafe impl Send for Bindings {}

impl Drop for Bindings {
    fn drop(&mut self) {
        unsafe {
            (self.logout)();
            let _ = FreeLibrary(self.module);
        }
    }
}

/// One process-wide login session, lazily established on first use and kept for the process's
/// lifetime. `None` means either Voicemeeter/its DLL isn't available, or login failed — callers
/// should treat that as "fall back to the existing WASAPI/CPAL path", not an error to surface.
static SESSION: OnceLock<Mutex<Option<Bindings>>> = OnceLock::new();

fn session() -> &'static Mutex<Option<Bindings>> {
    SESSION.get_or_init(|| Mutex::new(try_init()))
}

/// Explicitly logs out of VoicemeeterRemote, if a session was ever established. Voicemeeter
/// counts logged-in API clients internally, so skipping this on exit leaves it thinking a
/// client is still attached — this must run before process exit, since `SESSION` being a
/// `static` means its `Drop` impl never runs on normal termination, and the core's own exit
/// path uses `std::process::exit`, which skips destructors entirely regardless. Safe to call
/// even if no session was ever established (no-op) or if called more than once (Option::take
/// makes the second call a no-op too).
pub fn shutdown() {
    if let Some(lock) = SESSION.get() {
        if let Ok(mut guard) = lock.lock() {
            drop(guard.take());
        }
    }
}

fn try_init() -> Option<Bindings> {
    let dll_path = find_remote_dll()?;
    tracing::info!("VoicemeeterRemote.dll found at {}", dll_path.display());
    let wide_path = to_wide(dll_path.to_str()?);

    unsafe {
        let module = LoadLibraryW(PCWSTR(wide_path.as_ptr())).ok()?;

        macro_rules! load_fn {
            ($name:literal, $ty:ty) => {{
                let addr = GetProcAddress(module, PCSTR($name.as_ptr()))?;
                std::mem::transmute::<_, $ty>(addr)
            }};
        }

        let login: FnLogin = load_fn!(b"VBVMR_Login\0", FnLogin);
        let logout: FnLogout = load_fn!(b"VBVMR_Logout\0", FnLogout);
        let get_voicemeeter_type: FnGetVoicemeeterType = load_fn!(b"VBVMR_GetVoicemeeterType\0", FnGetVoicemeeterType);
        let get_level: FnGetLevel = load_fn!(b"VBVMR_GetLevel\0", FnGetLevel);

        // 0 = OK. 1 = OK but Voicemeeter application itself isn't running yet — still a valid
        // login (per VB-Audio's docs), levels will just read as silence until it's launched.
        let login_result = login();
        if login_result != 0 && login_result != 1 {
            tracing::warn!("VBVMR_Login failed (rc={login_result})");
            let _ = FreeLibrary(module);
            return None;
        }
        tracing::info!("VoicemeeterRemote login OK (rc={login_result})");

        Some(Bindings {
            module,
            logout,
            get_voicemeeter_type,
            get_level,
        })
    }
}

// ── Strip name lookup + per-product channel offset tables ─────────────────
//
// Two different numbering schemes are in play here, both from VoicemeeterRemote.h:
//   - "Strip index" (0, 1, 2, ...): used in parameter names like "Strip[0].name" and in the
//     GetVoicemeeterType doc's STRIP/BUS INDEX ASSIGNMENT table.
//   - "Flat channel index" (nuChannel for GetLevel): a separate, wider numbering where each
//     strip occupies a contiguous *range* of channels (2 for physical hardware strips, 8 for
//     virtual input busses), taken from the GetLevel doc's own CHANNEL ASSIGNMENT tables.
// The tables below give (strip_index -> (base_channel, channel_count)) per Voicemeeter product,
// transcribed directly from those tables. Voicemeeter type: 1=Basic, 2=Banana, 3/6=Potato.

/// (windows_name_substring, base_channel, channel_count) for each product's *virtual* input
/// strips — the only ones that ever correspond to a Windows "output:" (render) device, since
/// physical strips (Strip 1-5) don't have their own WASAPI render endpoint. Voicemeeter names
/// these virtual devices identically for every install of a given product (they're not
/// user-configurable — confirmed via `VBVMR_GetParameterStringA("Strip[i].name", ...)`
/// returning empty for all strips: that parameter is the *assigned hardware device* for a
/// strip, not a queryable label, so matching against it doesn't work — the Windows device name
/// itself is the only reliable signal). Checked in order, first (most specific) match wins,
/// since e.g. "aux input" also contains the substring "input".
fn virtual_strip_patterns(voicemeeter_type: i32) -> &'static [(&'static str, i32, i32)] {
    match voicemeeter_type {
        1 => &[("input", 4, 8)], // Basic: Virtual Input only
        2 => &[("aux input", 14, 8), ("input", 6, 8)], // Banana: + Virtual Input AUX
        3 | 6 => &[("aux input", 18, 8), ("vaio3 input", 26, 8), ("input", 10, 8)], // Potato: + VAIO3
        _ => &[],
    }
}

/// Finds the virtual strip whose Voicemeeter-fixed device name matches `friendly_name` (the
/// Windows device friendly name, e.g. "Voicemeeter AUX Input (VB-Audio Voicemeeter VAIO)") and
/// reads its current pre-fader input peak level (linear amplitude, same convention as the
/// WASAPI/CPAL peak_tx values elsewhere in audio.rs — 0.0 = silence). Returns `None` if
/// Voicemeeter/its DLL isn't available, login failed, or no pattern matches this device —
/// callers should treat that as "fall back to the existing capture-based metering", not an error.
pub fn try_get_level(friendly_name: &str) -> Option<f32> {
    let guard = session().lock().ok()?;
    let Some(bindings) = guard.as_ref() else {
        return None;
    };

    let mut voicemeeter_type = 0i32;
    unsafe {
        if (bindings.get_voicemeeter_type)(&mut voicemeeter_type) != 0 {
            return None;
        }
    }

    let friendly_lower = friendly_name.to_ascii_lowercase();

    for &(pattern, base_channel, channel_count) in virtual_strip_patterns(voicemeeter_type) {
        if !friendly_lower.contains(pattern) {
            continue;
        }

        let mut peak = 0.0f32;
        for channel in base_channel..base_channel + channel_count {
            let mut value = 0.0f32;
            unsafe {
                if (bindings.get_level)(VBVMR_LEVEL_TYPE_PRE_FADER_INPUT, channel, &mut value) == 0
                    && value > peak
                {
                    peak = value;
                }
            }
        }
        return Some(peak);
    }

    None
}
