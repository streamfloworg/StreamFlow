#![allow(unsafe_code)]

use streamflow_ipc::{CaptureSource, CaptureSourceKind};
use windows::{
    core::BOOL,
    Graphics::Capture::GraphicsCaptureItem,
    Win32::{
        Foundation::{HWND, LPARAM, RECT},
        Graphics::Gdi::{
            EnumDisplayMonitors, GetMonitorInfoW, HDC, HMONITOR, MONITORINFOEXW,
        },
        System::WinRT::Graphics::Capture::IGraphicsCaptureItemInterop,
        UI::WindowsAndMessaging::{
            EnumWindows, GetWindowLongW, GetWindowTextLengthW, GetWindowTextW, IsWindowVisible,
            GWL_EXSTYLE, GWL_STYLE, WS_EX_TOOLWINDOW, WS_MINIMIZE,
        },
    },
};

/// Return every capturable monitor and visible application window.
pub fn enumerate() -> Vec<CaptureSource> {
    let mut sources = Vec::new();
    sources.extend(enumerate_monitors());
    sources.extend(enumerate_windows());
    sources.extend(enumerate_webcams());
    sources
}

// ── Monitors ──────────────────────────────────────────────────────────────────

fn enumerate_monitors() -> Vec<CaptureSource> {
    let mut monitors: Vec<CaptureSource> = Vec::new();

    unsafe {
        let _ = EnumDisplayMonitors(
            None::<HDC>,
            None,
            Some(monitor_enum_proc),
            LPARAM(&raw mut monitors as isize),
        );
    }

    monitors
}

unsafe extern "system" fn monitor_enum_proc(
    hmonitor: HMONITOR,
    _hdc: HDC,
    _rect: *mut RECT,
    lparam: LPARAM,
) -> BOOL {
    let monitors = &mut *(lparam.0 as *mut Vec<CaptureSource>);

    // Verify WGC can capture this monitor before advertising it, and reuse the resulting
    // item to read its native capture resolution (its Size() call is otherwise free here).
    let Ok(item) = capture_item_for_monitor(hmonitor) else {
        return BOOL(1); // continue enumeration
    };
    let (width, height) = item
        .Size()
        .map(|s| (s.Width.max(0) as u32, s.Height.max(0) as u32))
        .unwrap_or((0, 0));

    let mut info = MONITORINFOEXW::default();
    info.monitorInfo.cbSize = std::mem::size_of::<MONITORINFOEXW>() as u32;
    if GetMonitorInfoW(hmonitor, &raw mut info.monitorInfo).as_bool() {
        let name = String::from_utf16_lossy(
            &info.szDevice[..info.szDevice.iter().position(|&c| c == 0).unwrap_or(32)],
        );
        let index = monitors.len();
        monitors.push(CaptureSource {
            id: format!("monitor:{index}"),
            name: if name.is_empty() {
                format!("Display {}", index + 1)
            } else {
                name
            },
            kind: CaptureSourceKind::Monitor,
            width,
            height,
        });
    }

    BOOL(1) // continue
}

// ── Windows ───────────────────────────────────────────────────────────────────

fn enumerate_windows() -> Vec<CaptureSource> {
    let mut windows: Vec<CaptureSource> = Vec::new();

    unsafe {
        let _ = EnumWindows(
            Some(window_enum_proc),
            LPARAM(&raw mut windows as isize),
        );
    }

    windows
}

unsafe extern "system" fn window_enum_proc(hwnd: HWND, lparam: LPARAM) -> BOOL {
    let windows = &mut *(lparam.0 as *mut Vec<CaptureSource>);

    // Skip invisible and minimized windows
    if !IsWindowVisible(hwnd).as_bool() {
        return BOOL(1);
    }
    let style = GetWindowLongW(hwnd, GWL_STYLE) as u32;
    if style & WS_MINIMIZE.0 != 0 {
        return BOOL(1);
    }

    // Skip DWM cloaked windows (e.g. suspended UWP apps or apps on other virtual desktops)
    use windows::Win32::Graphics::Dwm::{DwmGetWindowAttribute, DWMWA_CLOAKED};
    let mut cloaked: u32 = 0;
    let _ = DwmGetWindowAttribute(
        hwnd,
        DWMWA_CLOAKED,
        &mut cloaked as *mut _ as _,
        std::mem::size_of::<u32>() as u32,
    );
    if cloaked != 0 {
        return BOOL(1);
    }

    // Get the title
    let title_len = GetWindowTextLengthW(hwnd);
    let mut title = String::new();
    if title_len > 0 {
        let mut buf = vec![0u16; title_len as usize + 1];
        GetWindowTextW(hwnd, &mut buf);
        title = String::from_utf16_lossy(&buf[..buf.iter().position(|&c| c == 0).unwrap_or(0)]);
    }

    // Get process name for fallback
    let mut pid = 0;
    let mut process_name = String::new();
    use windows::Win32::System::Threading::{OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION};
    use windows::Win32::System::ProcessStatus::GetModuleBaseNameW;
    use windows::Win32::UI::WindowsAndMessaging::GetWindowThreadProcessId;
    use windows::Win32::Foundation::CloseHandle;

    GetWindowThreadProcessId(hwnd, Some(&mut pid));
    if pid != 0 {
        if let Ok(process) = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) {
            let mut name_buf = [0u16; 256];
            let len = GetModuleBaseNameW(process, None, &mut name_buf);
            if len > 0 {
                process_name = String::from_utf16_lossy(&name_buf[..len as usize]);
            }
            let _ = CloseHandle(process);
        }
    }

    let ex_style = GetWindowLongW(hwnd, GWL_EXSTYLE) as u32;
    let is_tool = ex_style & WS_EX_TOOLWINDOW.0 != 0;

    // Filter out completely generic, invisible tool windows to avoid 100+ junk entries.
    // If it has a title, or it is a normal window (not tool window), we keep it.
    if title.is_empty() && is_tool {
        return BOOL(1);
    }

    // If still empty but valid, label it with the process name
    if title.trim().is_empty() {
        if process_name.is_empty() {
            return BOOL(1);
        }
        title = format!("[Untitled] ({})", process_name);
    }

    // Verify WGC supports capturing this window, and reuse the item for its client-area size.
    let Ok(item) = capture_item_for_hwnd(hwnd) else {
        return BOOL(1);
    };
    let (width, height) = item
        .Size()
        .map(|s| (s.Width.max(0) as u32, s.Height.max(0) as u32))
        .unwrap_or((0, 0));

    windows.push(CaptureSource {
        id: format!("window:{:#010x}", hwnd.0 as usize),
        name: title,
        kind: CaptureSourceKind::Window,
        width,
        height,
    });

    BOOL(1) // continue
}

// ── Webcams (Media Foundation) ────────────────────────────────────────────────

fn enumerate_webcams() -> Vec<CaptureSource> {
    let mut webcams = Vec::new();

    unsafe {
        // Ensure MF is initialized before enumerating. Safe to call multiple times.
        let _ = windows::Win32::Media::MediaFoundation::MFStartup(
            windows::Win32::Media::MediaFoundation::MF_VERSION,
            windows::Win32::Media::MediaFoundation::MFSTARTUP_NOSOCKET,
        );

        let mut attrs: Option<windows::Win32::Media::MediaFoundation::IMFAttributes> = None;
        if windows::Win32::Media::MediaFoundation::MFCreateAttributes(&mut attrs, 1).is_err() {
            return webcams;
        }
        let attrs = attrs.unwrap();
        if attrs
            .SetGUID(
                &windows::Win32::Media::MediaFoundation::MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                &windows::Win32::Media::MediaFoundation::MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID,
            )
            .is_err()
        {
            return webcams;
        }

        let mut devices_ptr: *mut Option<windows::Win32::Media::MediaFoundation::IMFActivate> =
            std::ptr::null_mut();
        let mut count = 0;
        if windows::Win32::Media::MediaFoundation::MFEnumDeviceSources(
            &attrs,
            &mut devices_ptr,
            &mut count,
        )
        .is_err()
        {
            return webcams;
        }

        if devices_ptr.is_null() || count == 0 {
            return webcams;
        }

        let devices = std::slice::from_raw_parts(devices_ptr, count as usize);
        for dev in devices {
            if let Some(activate) = dev {
                // Get Friendly Name
                let mut name_ptr = windows::core::PWSTR::null();
                let mut name_len = 0;
                if activate
                    .GetAllocatedString(
                        &windows::Win32::Media::MediaFoundation::MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME,
                        &mut name_ptr,
                        &mut name_len,
                    )
                    .is_ok()
                    && !name_ptr.is_null()
                {
                    let name = unsafe { name_ptr.to_string().unwrap_or_default() };
                    windows::Win32::System::Com::CoTaskMemFree(Some(name_ptr.as_ptr() as _));

                    // Get Symbolic Link (used for opening it later)
                    let mut sym_ptr = windows::core::PWSTR::null();
                    let mut sym_len = 0;
                    if activate
                        .GetAllocatedString(
                            &windows::Win32::Media::MediaFoundation::MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
                            &mut sym_ptr,
                            &mut sym_len,
                        )
                        .is_ok()
                        && !sym_ptr.is_null()
                    {
                        let sym_link = unsafe { sym_ptr.to_string().unwrap_or_default() };
                        windows::Win32::System::Com::CoTaskMemFree(Some(sym_ptr.as_ptr() as _));

                        webcams.push(CaptureSource {
                            id: format!("webcam:{}", sym_link),
                            name,
                            kind: CaptureSourceKind::Webcam,
                            // Resolution isn't known without opening the device and querying
                            // its supported media types — deferred along with webcam support.
                            width: 0,
                            height: 0,
                        });
                    }
                }
            }
        }

        windows::Win32::System::Com::CoTaskMemFree(Some(devices_ptr as _));
    }

    webcams
}

// ── Capture item construction ─────────────────────────────────────────────────

/// Build a [`GraphicsCaptureItem`] from a source id string.
/// Format: `"monitor:<index>"` or `"window:<hwnd_hex>"`.
pub fn capture_item_for_id(id: &str) -> windows::core::Result<GraphicsCaptureItem> {
    if let Some(rest) = id.strip_prefix("monitor:") {
        // Re-enumerate monitors in order and pick by index.
        let index: usize = rest.parse().unwrap_or(0);
        let hmonitor = nth_monitor(index)?;
        capture_item_for_monitor(hmonitor)
    } else if let Some(rest) = id.strip_prefix("window:") {
        let hwnd_val = usize::from_str_radix(rest.trim_start_matches("0x"), 16)
            .map_err(|_| windows::core::Error::from_win32())?;
        let hwnd = HWND(hwnd_val as *mut core::ffi::c_void);
        capture_item_for_hwnd(hwnd)
    } else {
        Err(windows::core::Error::from_win32())
    }
}

fn capture_item_for_monitor(hmonitor: HMONITOR) -> windows::core::Result<GraphicsCaptureItem> {
    let interop: IGraphicsCaptureItemInterop =
        windows::core::factory::<GraphicsCaptureItem, IGraphicsCaptureItemInterop>()?;
    unsafe { interop.CreateForMonitor(hmonitor) }
}

fn capture_item_for_hwnd(hwnd: HWND) -> windows::core::Result<GraphicsCaptureItem> {
    let interop: IGraphicsCaptureItemInterop =
        windows::core::factory::<GraphicsCaptureItem, IGraphicsCaptureItemInterop>()?;
    unsafe { interop.CreateForWindow(hwnd) }
}

fn nth_monitor(index: usize) -> windows::core::Result<HMONITOR> {
    struct State {
        target: usize,
        current: usize,
        result: Option<HMONITOR>,
    }

    unsafe extern "system" fn proc(
        hmonitor: HMONITOR,
        _: HDC,
        _: *mut RECT,
        lparam: LPARAM,
    ) -> BOOL {
        let state = &mut *(lparam.0 as *mut State);
        if state.current == state.target {
            state.result = Some(hmonitor);
            return BOOL(0); // stop
        }
        state.current += 1;
        BOOL(1)
    }

    let mut state = State { target: index, current: 0, result: None };
    unsafe {
        let _ = EnumDisplayMonitors(
            None::<HDC>,
            None,
            Some(proc),
            LPARAM(&raw mut state as isize),
        );
    }
    state
        .result
        .ok_or_else(|| windows::core::Error::from_win32())
}

// ── Tests ─────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;

    /// Smoke test — requires a real display; skipped in headless CI.
    #[test]
    #[ignore]
    fn enumerate_returns_at_least_one_monitor() {
        let sources = enumerate();
        let monitors: Vec<_> = sources
            .iter()
            .filter(|s| s.kind == CaptureSourceKind::Monitor)
            .collect();
        assert!(!monitors.is_empty(), "expected at least one monitor source");
        for m in &monitors {
            assert!(m.id.starts_with("monitor:"), "bad monitor id: {}", m.id);
            assert!(!m.name.is_empty(), "monitor name is empty");
        }
    }

    #[test]
    #[ignore]
    fn capture_item_for_primary_monitor() {
        let item = capture_item_for_id("monitor:0");
        assert!(item.is_ok(), "failed to create capture item: {item:?}");
    }
}
