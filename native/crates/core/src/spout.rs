#![allow(unsafe_code)]

//! Hand-rolled Spout2 sender — Option A of the Spout2 Integration Plan (see the Obsidian vault):
//! publishes the compositor's output as a named GPU-shared D3D11 texture that other Spout-aware
//! apps (OBS + obs-spout2-plugin, TouchDesigner, Resolume, the official SpoutReceiver demo, etc.)
//! can pick up directly, without StreamFlow needing to know who's receiving it. Deliberately does
//! NOT depend on any of the third-party spout2-rs/spout-rs/rust-spout2 crates — all are weeks-to-
//! months old with double/triple-digit crates.io download counts, and spout2-rs specifically has
//! no visible source repository at all, so there's no way to audit the `unsafe` FFI before it
//! runs in a process that also handles OAuth tokens and stream keys. This reimplements the wire
//! protocol directly against `windows-rs`, which the crate already depends on for WGC capture's
//! own D3D11/DXGI needs.
//!
//! Protocol notes (reverse-engineered from leadedge/Spout2's SpoutSenderNames.h/.cpp,
//! SpoutSharedMemory.cpp, and SpoutFrameCount.cpp — NOT independently verified against a running
//! Spout2 build as of this writing; this is the M1 "spike" milestone from the plan, meant to be
//! validated empirically against the official SpoutReceiver demo before anything downstream
//! depends on it):
//!   - Sender name list: a `"SpoutSenderNames"` file mapping — a flat array of 256-byte
//!     null-terminated name slots — guarded by a `"SpoutSenderNames_mutex"` named mutex.
//!   - Per-sender info: a file mapping named after the sender's own name, holding one
//!     [`SharedTextureInfo`] (280 bytes), guarded by `"<name>_mutex"`.
//!   - New-frame signal: a named semaphore `"<name>_Count_Semaphore"` (initial count 1, max
//!     `LONG_MAX`) — the sender calls `ReleaseSemaphore(+2)` after publishing each frame.
//!   - No `"Global\"` namespace prefix — session-local, which is fine here (same Windows
//!     session, not cross-RDP-session).
//!   - A DirectX shared handle is guaranteed by Windows to fit in 32 bits even on 64-bit builds
//!     (documented WOW64/interop behavior: kernel object handles never use the upper 32 bits),
//!     which is what lets the wire struct store it as a plain `u32`.

use std::ffi::CString;
use std::mem::size_of;

use anyhow::{Context, Result, anyhow, bail};
use windows::Win32::Foundation::{CloseHandle, HANDLE};
use windows::Win32::Graphics::Direct3D::D3D_DRIVER_TYPE_HARDWARE;
use windows::Win32::Graphics::Direct3D11::{
    D3D11_BIND_RENDER_TARGET, D3D11_BIND_SHADER_RESOURCE, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
    D3D11_RESOURCE_MISC_SHARED, D3D11_SDK_VERSION, D3D11_TEXTURE2D_DESC, D3D11_USAGE_DEFAULT,
    D3D11CreateDevice, ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D,
};
use windows::Win32::Graphics::Dxgi::IDXGIResource;
use windows::Win32::Graphics::Dxgi::Common::{DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_SAMPLE_DESC};
use windows::Win32::System::Memory::{
    CreateFileMappingA, FILE_MAP_ALL_ACCESS, MEMORY_MAPPED_VIEW_ADDRESS, MapViewOfFile,
    PAGE_READWRITE, UnmapViewOfFile,
};
use windows::Win32::System::Threading::{
    CreateMutexA, CreateSemaphoreA, ReleaseMutex, ReleaseSemaphore, WaitForSingleObject,
};
use windows::core::{Interface, PCSTR};

const MAX_SENDERS: usize = 64;
const NAME_SLOT_LEN: usize = 256;
const SENDER_LIST_MAP_NAME: &str = "SpoutSenderNames";
const SENDER_LIST_SIZE: usize = MAX_SENDERS * NAME_SLOT_LEN;

/// Matches leadedge/Spout2's `SharedTextureInfo` (SpoutSenderNames.h) field-for-field — this is
/// the one struct where getting the layout wrong silently breaks interop with every real Spout
/// receiver, so field order/types/sizes here are load-bearing, not just documentation.
#[repr(C)]
#[derive(Clone, Copy)]
struct SharedTextureInfo {
    share_handle: u32,
    width: u32,
    height: u32,
    format: u32,
    usage: u32,
    description: [u8; 256],
    partner_id: u32,
}

const _: () = assert!(size_of::<SharedTextureInfo>() == 280);

/// A named file mapping + view, closed together on drop. Used for both the sender-name list and
/// a single sender's own info block.
struct NamedMap {
    map_handle: HANDLE,
    view: MEMORY_MAPPED_VIEW_ADDRESS,
    mutex: HANDLE,
}

impl NamedMap {
    /// Creates (or opens, if some other process already has) a named file mapping of `size`
    /// bytes plus its paired `"<name>_mutex"` — matching SpoutSharedMemory's naming convention.
    /// `NULL` security attributes (matches the real SDK): visible within this Windows session
    /// only, which is fine — Spout senders/receivers only ever talk to other processes on the
    /// same desktop session, never across an RDP session boundary.
    fn create(name: &str, size: usize) -> Result<Self> {
        let name_c = CString::new(name).context("Spout map name contains a NUL byte")?;
        let mutex_name_c =
            CString::new(format!("{name}_mutex")).context("Spout mutex name contains a NUL byte")?;

        let map_handle = unsafe {
            CreateFileMappingA(
                windows::Win32::Foundation::INVALID_HANDLE_VALUE,
                None,
                PAGE_READWRITE,
                0,
                size as u32,
                PCSTR(name_c.as_ptr() as *const u8),
            )
        }
        .context("CreateFileMappingA failed for Spout named map")?;

        let view = unsafe { MapViewOfFile(map_handle, FILE_MAP_ALL_ACCESS, 0, 0, 0) };
        if view.Value.is_null() {
            unsafe { let _ = CloseHandle(map_handle); }
            bail!("MapViewOfFile failed for Spout named map '{name}'");
        }

        let mutex = unsafe { CreateMutexA(None, false, PCSTR(mutex_name_c.as_ptr() as *const u8)) }
            .context("CreateMutexA failed for Spout named map")?;

        Ok(Self { map_handle, view, mutex })
    }

    /// Bounded wait (not the real SDK's exact 67ms — that timeout is purely a sender-local
    /// choice, a receiver never sees or cares how long we waited to acquire our own mutex).
    fn lock(&self) -> Result<()> {
        let wait = unsafe { WaitForSingleObject(self.mutex, 200) };
        if wait.0 != 0 {
            bail!("Timed out acquiring Spout named-map mutex");
        }
        Ok(())
    }

    fn unlock(&self) {
        unsafe { let _ = ReleaseMutex(self.mutex); }
    }

    fn as_bytes_mut(&self, len: usize) -> &mut [u8] {
        unsafe { std::slice::from_raw_parts_mut(self.view.Value as *mut u8, len) }
    }
}

impl Drop for NamedMap {
    fn drop(&mut self) {
        unsafe {
            let _ = UnmapViewOfFile(self.view);
            let _ = CloseHandle(self.map_handle);
            let _ = CloseHandle(self.mutex);
        }
    }
}

/// One published Spout source. Owns its own D3D11 device (created lazily, like the compositor's
/// GL blur context) rather than sharing one with anything else — this only ever runs on the
/// compositor's dedicated render thread, same thread-affinity rule the blur engine already
/// follows for its own GPU context.
pub struct SpoutSender {
    name: String,
    device: ID3D11Device,
    context: ID3D11DeviceContext,
    texture: Option<ID3D11Texture2D>,
    width: u32,
    height: u32,
    /// Raw DXGI shared-resource handle for the current `texture`, if any — same value written
    /// into the public `SharedTextureInfo` registry block, also handed to the C# host directly
    /// via `Event::SpoutTextureReady` for the internal "Show Preview" GPU path (see
    /// `texture_info`).
    share_handle: Option<u32>,
    /// This device's DXGI adapter LUID, packed as `(HighPart << 32) | LowPart` — sent to the C#
    /// host alongside the shared handle so its own D3D9Ex device can be created on the *same*
    /// physical GPU. D3D9's and DXGI's adapter enumeration don't have to agree on which one is
    /// "default" (a real, documented gotcha on hybrid-graphics/multi-GPU machines), and opening a
    /// shared handle from a mismatched adapter fails with E_INVALIDARG — this is exactly what
    /// `IDirect3D9Ex::GetAdapterLUID` exists for on the C# side to resolve.
    adapter_luid: i64,
    list_map: NamedMap,
    info_map: NamedMap,
    frame_semaphore: HANDLE,
}

fn get_adapter_luid(device: &ID3D11Device) -> Result<i64> {
    let dxgi_device: windows::Win32::Graphics::Dxgi::IDXGIDevice =
        device.cast().context("D3D11 device doesn't implement IDXGIDevice")?;
    let adapter = unsafe { dxgi_device.GetAdapter() }.context("IDXGIDevice::GetAdapter failed")?;
    let desc = unsafe { adapter.GetDesc() }.context("IDXGIAdapter::GetDesc failed")?;
    let luid = desc.AdapterLuid;
    Ok(((luid.HighPart as i64) << 32) | (luid.LowPart as i64 & 0xFFFF_FFFF))
}

impl SpoutSender {
    pub fn new(name: &str) -> Result<Self> {
        if name.is_empty() || name.len() >= NAME_SLOT_LEN {
            bail!("Spout sender name must be non-empty and under {NAME_SLOT_LEN} bytes");
        }

        let mut device: Option<ID3D11Device> = None;
        let mut context: Option<ID3D11DeviceContext> = None;
        unsafe {
            D3D11CreateDevice(
                None,
                D3D_DRIVER_TYPE_HARDWARE,
                windows::Win32::Foundation::HMODULE::default(),
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                None,
                D3D11_SDK_VERSION,
                Some(&mut device),
                None,
                Some(&mut context),
            )
        }
        .context("D3D11CreateDevice failed for Spout sender")?;
        let device = device.ok_or_else(|| anyhow!("D3D11CreateDevice returned no device"))?;
        let context = context.ok_or_else(|| anyhow!("D3D11CreateDevice returned no context"))?;
        let adapter_luid = get_adapter_luid(&device).unwrap_or_else(|e| {
            tracing::warn!("Failed to resolve Spout D3D11 device's adapter LUID: {e:#}");
            0
        });

        let list_map = NamedMap::create(SENDER_LIST_MAP_NAME, SENDER_LIST_SIZE)?;
        register_sender_name(&list_map, name)?;

        let info_map = NamedMap::create(name, size_of::<SharedTextureInfo>())?;

        let semaphore_name = CString::new(format!("{name}_Count_Semaphore"))
            .context("Spout semaphore name contains a NUL byte")?;
        let frame_semaphore = unsafe {
            CreateSemaphoreA(None, 1, i32::MAX, PCSTR(semaphore_name.as_ptr() as *const u8))
        }
        .context("CreateSemaphoreA failed for Spout sender")?;

        tracing::info!("Spout sender '{name}' registered");

        Ok(Self {
            name: name.to_string(),
            device,
            context,
            texture: None,
            width: 0,
            height: 0,
            share_handle: None,
            adapter_luid,
            list_map,
            info_map,
            frame_semaphore,
        })
    }

    /// Publishes one BGRA frame (`pixels.len()` must be `width * height * 4`). Recreates the
    /// shared texture only when dimensions actually change — same invalidate-on-dims-change
    /// pattern the compositor already uses for its `PipScalerCache`/`sws_getContext` entries.
    pub fn send_bgra(&mut self, pixels: &[u8], width: u32, height: u32) -> Result<()> {
        if pixels.len() != (width as usize) * (height as usize) * 4 {
            bail!("Spout send_bgra: pixel buffer length doesn't match width*height*4");
        }

        if self.texture.is_none() || self.width != width || self.height != height {
            self.recreate_texture(width, height)?;
        }

        let texture = self.texture.as_ref().expect("just created above");
        unsafe {
            self.context.UpdateSubresource(
                texture,
                0,
                None,
                pixels.as_ptr() as *const _,
                width * 4,
                0,
            );
            // Without this, UpdateSubresource just queues the write on the immediate context's
            // command buffer — nothing here ever naturally flushes it (no swapchain/Present to
            // piggyback on, unlike a normal render loop), so the driver is free to batch an
            // unbounded number of these before actually executing them against the GPU resource.
            // A cross-process reader (the receiver, via CopyResource on its own device against
            // our shared handle) only sees what's actually landed, so an unflushed backlog reads
            // exactly like a growing, erratic delay — this is a well-known D3D11 shared-resource
            // interop gotcha, not something specific to Spout's own protocol. Confirmed to fix
            // the "erratic but consistently delayed" receiver-side lag seen before this was added.
            self.context.Flush();
        }

        unsafe {
            let _ = ReleaseSemaphore(self.frame_semaphore, 2, None);
        }

        Ok(())
    }

    fn recreate_texture(&mut self, width: u32, height: u32) -> Result<()> {
        let desc = D3D11_TEXTURE2D_DESC {
            Width: width,
            Height: height,
            MipLevels: 1,
            ArraySize: 1,
            Format: DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc: DXGI_SAMPLE_DESC { Count: 1, Quality: 0 },
            Usage: D3D11_USAGE_DEFAULT,
            // RENDER_TARGET is required, not just SHADER_RESOURCE: the C# host's D3D9Ex side
            // opens this shared surface with D3DUSAGE_RENDERTARGET (D3DImage's own requirement),
            // and D3D9/D3D11 shared-surface interop needs the originating D3D11 resource to
            // actually carry the matching bind flag — without it, IDirect3DDevice9Ex.CreateTexture
            // fails with D3DERR_INVALIDCALL (0x8876086C) when opening the handle. SHADER_RESOURCE
            // is kept alongside it since it doesn't cost anything and keeps this usable by GPU
            // shader-sampling receivers too, not just render-target-style ones.
            BindFlags: (D3D11_BIND_SHADER_RESOURCE.0 | D3D11_BIND_RENDER_TARGET.0) as u32,
            CPUAccessFlags: 0,
            MiscFlags: D3D11_RESOURCE_MISC_SHARED.0 as u32,
        };

        let mut texture: Option<ID3D11Texture2D> = None;
        unsafe { self.device.CreateTexture2D(&desc, None, Some(&mut texture)) }
            .context("CreateTexture2D failed for Spout shared texture")?;
        let texture = texture.ok_or_else(|| anyhow!("CreateTexture2D returned no texture"))?;

        let dxgi_resource: IDXGIResource =
            texture.cast().context("Spout texture doesn't implement IDXGIResource")?;
        let share_handle = unsafe { dxgi_resource.GetSharedHandle() }
            .context("IDXGIResource::GetSharedHandle failed for Spout texture")?;
        // [diag] Verifying the "upper 32 bits are always zero" assumption the u32 truncation
        // below relies on — logs the raw, untruncated isize so a lossy truncation would actually
        // show up here instead of silently producing a wrong handle value downstream.
        tracing::info!(
            "[diag] Spout GetSharedHandle raw value: 0x{:016X} (pointer={:?})",
            share_handle.0 as usize, share_handle.0
        );

        self.update_info(share_handle, width, height)?;

        self.texture = Some(texture);
        self.width = width;
        self.height = height;
        // Safe: Windows guarantees kernel-object HANDLEs fit in 32 bits even on 64-bit builds —
        // same reasoning as the identical cast in update_info below.
        self.share_handle = Some(share_handle.0 as usize as u32);
        Ok(())
    }

    /// Current `(share_handle, width, height)`, if a texture has been created yet — used to
    /// notify the C# host (`Event::SpoutTextureReady`) so its own D3D9Ex-backed "Show Preview"
    /// path can open the same shared resource directly.
    pub fn texture_info(&self) -> Option<(u32, u32, u32, i64)> {
        self.share_handle.map(|h| (h, self.width, self.height, self.adapter_luid))
    }

    fn update_info(&self, share_handle: HANDLE, width: u32, height: u32) -> Result<()> {
        self.info_map.lock()?;
        let bytes = self.info_map.as_bytes_mut(size_of::<SharedTextureInfo>());
        let info = SharedTextureInfo {
            // Safe: Windows guarantees kernel-object HANDLEs fit in 32 bits even on 64-bit
            // builds (the upper 32 bits are always zero) — see this module's doc comment.
            share_handle: share_handle.0 as usize as u32,
            width,
            height,
            format: DXGI_FORMAT_B8G8R8A8_UNORM.0 as u32,
            usage: 0,
            description: [0u8; 256],
            partner_id: 0,
        };
        let info_bytes = unsafe {
            std::slice::from_raw_parts(&info as *const _ as *const u8, size_of::<SharedTextureInfo>())
        };
        bytes.copy_from_slice(info_bytes);
        self.info_map.unlock();
        Ok(())
    }
}

impl Drop for SpoutSender {
    fn drop(&mut self) {
        if let Err(e) = deregister_sender_name(&self.list_map, &self.name) {
            tracing::warn!("Failed to deregister Spout sender '{}': {e:#}", self.name);
        }
        unsafe {
            let _ = CloseHandle(self.frame_semaphore);
        }
        tracing::info!("Spout sender '{}' deregistered", self.name);
    }
}

fn register_sender_name(list_map: &NamedMap, name: &str) -> Result<()> {
    list_map.lock()?;
    let bytes = list_map.as_bytes_mut(SENDER_LIST_SIZE);
    let name_bytes = name.as_bytes();

    let mut target_slot: Option<usize> = None;
    for slot in 0..MAX_SENDERS {
        let start = slot * NAME_SLOT_LEN;
        let slot_bytes = &bytes[start..start + NAME_SLOT_LEN];
        let is_empty = slot_bytes[0] == 0;
        let is_same_name = slot_bytes.starts_with(name_bytes) && slot_bytes.get(name_bytes.len()) == Some(&0);
        if is_empty || is_same_name {
            target_slot = Some(slot);
            break;
        }
    }

    let Some(slot) = target_slot else {
        list_map.unlock();
        bail!("Spout sender-name list is full ({MAX_SENDERS} max)");
    };

    let start = slot * NAME_SLOT_LEN;
    let slot_bytes = &mut bytes[start..start + NAME_SLOT_LEN];
    slot_bytes.fill(0);
    slot_bytes[..name_bytes.len()].copy_from_slice(name_bytes);

    list_map.unlock();
    Ok(())
}

fn deregister_sender_name(list_map: &NamedMap, name: &str) -> Result<()> {
    list_map.lock()?;
    let bytes = list_map.as_bytes_mut(SENDER_LIST_SIZE);
    let name_bytes = name.as_bytes();

    for slot in 0..MAX_SENDERS {
        let start = slot * NAME_SLOT_LEN;
        let slot_bytes = &mut bytes[start..start + NAME_SLOT_LEN];
        if slot_bytes.starts_with(name_bytes) && slot_bytes.get(name_bytes.len()) == Some(&0) {
            slot_bytes.fill(0);
            break;
        }
    }

    list_map.unlock();
    Ok(())
}
