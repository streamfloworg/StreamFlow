#![allow(unsafe_code)]
//! Audio capture for streaming.
//!
//! - `input:` prefix  → CPAL (microphone / physical input device)
//! - `output:` prefix → WASAPI loopback (Voicemeeter, speakers, virtual cables)
//!
//! Both paths produce f32 PCM into the same `ringbuf::HeapRb<f32>` consumed
//! by the AAC encoder. The caller does not need to know which path was taken.

use anyhow::{anyhow, Result};
use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};
use ringbuf::{traits::{Producer, Consumer, Split}, HeapRb};
use streamflow_ipc::AudioDeviceDef;

/// How often the WASAPI loopback and CPAL input paths flush their accumulated peak to
/// `peak_tx` for the UI level meter. Shorter = a snappier-feeling meter (more, smaller updates)
/// at essentially no extra cost — this doesn't add any audio processing or extra WASAPI/CPAL
/// calls, just flushes a max-over-samples that's already being computed on every audio callback
/// regardless, more frequently. ~30ms (~33 Hz) matches typical UI meter refresh rates without
/// meaningfully increasing IPC/UI-thread traffic versus the previous 100ms.
const METER_EMIT_INTERVAL: std::time::Duration = std::time::Duration::from_millis(30);

// ── Device enumeration ─────────────────────────────────────────────────────

/// Enumerate all active audio endpoints via MMDEVAPI, classifying each as
/// `Output` (render/WASAPI-loopback), `Microphone` (physical capture), or
/// `Capture` (software/virtual capture — VoiceMeeter Out, VB-CABLE, Stereo Mix).
pub fn get_audio_devices() -> Result<Vec<AudioDeviceDef>> {
    // SAFETY: COM property-store reads; no shared mutable state.
    unsafe { enumerate_audio_devices_mmdevapi() }
}

unsafe fn enumerate_audio_devices_mmdevapi() -> Result<Vec<AudioDeviceDef>> {
    use streamflow_ipc::AudioDeviceKind;
    use windows::Win32::Foundation::PROPERTYKEY;
    use windows::Win32::Media::Audio::{
        eCapture, eConsole, eRender,
        IMMDeviceEnumerator, MMDeviceEnumerator,
        DEVICE_STATE_ACTIVE,
    };
    use windows::Win32::System::Com::{
        CoCreateInstance, CoInitializeEx, CLSCTX_ALL, COINIT_MULTITHREADED, STGM_READ,
    };
    use windows::Win32::System::Com::StructuredStorage::PropVariantClear;
    use windows::Win32::System::Variant::{VT_LPWSTR, VT_UI4};
    use windows::core::GUID;

    let _ = CoInitializeEx(None, COINIT_MULTITHREADED);

    let enumerator: IMMDeviceEnumerator =
        CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;

    // {a45c254e-df1c-4efd-8020-67d146a850e0} pid=14 — PKEY_Device_FriendlyName
    let pkey_name = PROPERTYKEY {
        fmtid: GUID::from_u128(0xa45c254e_df1c_4efd_8020_67d146a850e0),
        pid: 14,
    };
    // {1da5d803-d492-4edd-8c23-e0c0ffee7f0e} pid=0 — PKEY_AudioEndpoint_FormFactor
    // Values: 1=Speakers 2=LineLevel 3=Headphones 4=Microphone 5=Headset 8=SPDIF ...
    let pkey_form_factor = PROPERTYKEY {
        fmtid: GUID::from_u128(0x1da5d803_d492_4edd_8c23_e0c0ffee7f0e),
        pid: 0,
    };

    // Helper: read VT_LPWSTR property as String.
    let read_name = |dev: &windows::Win32::Media::Audio::IMMDevice| -> Option<String> {
        let ps = dev.OpenPropertyStore(STGM_READ).ok()?;
        let mut pv = ps.GetValue(&pkey_name).ok()?;
        let name = if pv.Anonymous.Anonymous.vt == VT_LPWSTR {
            let ptr = pv.Anonymous.Anonymous.Anonymous.pwszVal;
            if !ptr.is_null() { ptr.to_string().ok() } else { None }
        } else {
            None
        };
        let _ = PropVariantClear(&mut pv);
        name
    };

    // Helper: read VT_UI4 form-factor property.
    let read_form_factor = |dev: &windows::Win32::Media::Audio::IMMDevice| -> Option<u32> {
        let ps = dev.OpenPropertyStore(STGM_READ).ok()?;
        let mut pv = ps.GetValue(&pkey_form_factor).ok()?;
        let ff = if pv.Anonymous.Anonymous.vt == VT_UI4 {
            Some(pv.Anonymous.Anonymous.Anonymous.ulVal)
        } else {
            None
        };
        let _ = PropVariantClear(&mut pv);
        ff
    };

    // Default endpoint names for is_default detection.
    let default_render  = enumerator.GetDefaultAudioEndpoint(eRender,  eConsole).ok().and_then(|d| read_name(&d));
    let default_capture = enumerator.GetDefaultAudioEndpoint(eCapture, eConsole).ok().and_then(|d| read_name(&d));

    let mut devices = Vec::new();

    // ── Render endpoints: Output (WASAPI loopback) ──────────────────────────
    let render_col = enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)?;
    for i in 0..render_col.GetCount().unwrap_or(0) {
        if let Ok(dev) = render_col.Item(i) {
            if let Some(name) = read_name(&dev) {
                let is_default = default_render.as_deref() == Some(name.as_str());
                devices.push(AudioDeviceDef {
                    id: format!("output:{name}"),
                    name,
                    kind: AudioDeviceKind::Output,
                    is_default,
                });
            }
        }
    }

    // ── Capture endpoints: Microphone or software Capture ───────────────────
    let capture_col = enumerator.EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE)?;
    for i in 0..capture_col.GetCount().unwrap_or(0) {
        if let Ok(dev) = capture_col.Item(i) {
            if let Some(name) = read_name(&dev) {
                // EndpointFormFactor 4=Microphone 5=Headset → physical mic.
                // Anything else (LineLevel=2, virtual/unknown) → software capture device.
                let kind = match read_form_factor(&dev) {
                    Some(4) | Some(5) => AudioDeviceKind::Microphone,
                    _                 => AudioDeviceKind::Capture,
                };
                let is_default = default_capture.as_deref() == Some(name.as_str());
                devices.push(AudioDeviceDef {
                    id: format!("input:{name}"),
                    name,
                    kind,
                    is_default,
                });
            }
        }
    }

    Ok(devices)
}

// ── OS-level device volume/mute (IAudioEndpointVolume) ─────────────────────
//
// Distinct from the per-source `gain` in AudioSourceConfig: that only scales a device's
// contribution to this app's own stream mix. This is the actual Windows device volume —
// the same value Windows' own Volume Mixer shows/controls for this device, shared with every
// other app using it.

/// Resolves `device_id` ("output:Name" / "input:Name") to its `IAudioEndpointVolume`, matching
/// the same by-friendly-name-with-default-fallback heuristic used for WASAPI loopback capture.
/// Self-contained (re-enumerates + re-activates on every call) rather than caching the COM
/// interface across calls — this is only ever called at a slow poll cadence (device volume
/// rarely changes) or once per user action, so the enumeration cost doesn't matter, and it
/// avoids any cross-thread COM lifetime/apartment concerns entirely.
unsafe fn resolve_endpoint_volume(device_id: &str) -> Result<windows::Win32::Media::Audio::Endpoints::IAudioEndpointVolume> {
    use windows::Win32::Foundation::PROPERTYKEY;
    use windows::Win32::Media::Audio::{
        eCapture, eConsole, eRender,
        IMMDeviceEnumerator, MMDeviceEnumerator,
        DEVICE_STATE_ACTIVE,
    };
    use windows::Win32::Media::Audio::Endpoints::IAudioEndpointVolume;
    use windows::Win32::System::Com::{CoCreateInstance, CoInitializeEx, CLSCTX_ALL, COINIT_MULTITHREADED, STGM_READ};
    use windows::Win32::System::Com::StructuredStorage::PropVariantClear;
    use windows::Win32::System::Variant::VT_LPWSTR;
    use windows::core::GUID;

    let _ = CoInitializeEx(None, COINIT_MULTITHREADED);

    let (flow, name) = if let Some(name) = device_id.strip_prefix("output:") {
        (eRender, name)
    } else if let Some(name) = device_id.strip_prefix("input:") {
        (eCapture, name)
    } else {
        (eRender, device_id)
    };

    let enumerator: IMMDeviceEnumerator = CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;

    let device = if name.is_empty() {
        enumerator.GetDefaultAudioEndpoint(flow, eConsole)?
    } else {
        // PKEY_Device_FriendlyName = {a45c254e-df1c-4efd-8020-67d146a850e0}, pid=14
        let pkey = PROPERTYKEY {
            fmtid: GUID::from_u128(0xa45c254e_df1c_4efd_8020_67d146a850e0),
            pid: 14,
        };
        let collection = enumerator.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE)?;
        let count = collection.GetCount().unwrap_or(0);
        let mut found = None;
        for i in 0..count {
            if let Ok(dev) = collection.Item(i) {
                if let Ok(ps) = dev.OpenPropertyStore(STGM_READ) {
                    if let Ok(mut pv) = ps.GetValue(&pkey) {
                        if pv.Anonymous.Anonymous.vt == VT_LPWSTR {
                            let ptr = pv.Anonymous.Anonymous.Anonymous.pwszVal;
                            if !ptr.is_null() {
                                if let Ok(friendly) = ptr.to_string() {
                                    if friendly.contains(name) || name.contains(&friendly) {
                                        let _ = PropVariantClear(&mut pv);
                                        found = Some(dev);
                                        break;
                                    }
                                }
                            }
                        }
                        let _ = PropVariantClear(&mut pv);
                    }
                }
            }
        }
        match found {
            Some(d) => d,
            None => enumerator.GetDefaultAudioEndpoint(flow, eConsole)?,
        }
    };

    Ok(device.Activate::<IAudioEndpointVolume>(CLSCTX_ALL, None)?)
}

/// Sets the device's actual OS-level master volume (0.0-1.0 linear scalar).
pub fn set_device_volume(device_id: &str, volume: f32) -> Result<()> {
    unsafe {
        let endpoint = resolve_endpoint_volume(device_id)?;
        endpoint.SetMasterVolumeLevelScalar(volume.clamp(0.0, 1.0), std::ptr::null())?;
    }
    Ok(())
}

/// Sets the device's actual OS-level mute state.
pub fn set_device_mute(device_id: &str, muted: bool) -> Result<()> {
    unsafe {
        let endpoint = resolve_endpoint_volume(device_id)?;
        endpoint.SetMute(muted, std::ptr::null())?;
    }
    Ok(())
}

/// Reads the device's actual OS-level volume + mute state.
pub fn get_device_volume(device_id: &str) -> Result<(f32, bool)> {
    unsafe {
        let endpoint = resolve_endpoint_volume(device_id)?;
        let volume = endpoint.GetMasterVolumeLevelScalar()?;
        let muted = endpoint.GetMute()?.as_bool();
        Ok((volume, muted))
    }
}

// ── Voicemeeter metering (see voicemeeter.rs for why this exists) ──────────

/// True for any "output:" device whose friendly name indicates it's a Voicemeeter endpoint —
/// these need `voicemeeter::try_get_level` instead of WASAPI loopback for metering, since
/// loopback never sees real content on Voicemeeter's virtual render endpoints.
pub fn is_voicemeeter_output_device(device_id: &str) -> bool {
    let name_matches = device_id
        .strip_prefix("output:")
        .is_some_and(|name| name.to_ascii_lowercase().contains("voicemeeter"));
    name_matches && crate::voicemeeter::is_voicemeeter_running()
}

/// Starts a polling thread that periodically reads this device's level via
/// `voicemeeter::try_get_level` and forwards it through `peak_tx`, at the same cadence and
/// linear-amplitude convention as the WASAPI/CPAL peak_tx paths (see METER_EMIT_INTERVAL) —
/// existing callers (the audio-monitor-bridge in main.rs) don't need to know the difference.
pub fn start_voicemeeter_monitor(device_id: &str, peak_tx: std::sync::mpsc::SyncSender<f32>) -> ActiveStream {
    let friendly_name = device_id.strip_prefix("output:").unwrap_or(device_id).to_string();
    let (stop_tx, stop_rx) = std::sync::mpsc::sync_channel::<()>(1);

    std::thread::Builder::new()
        .name("voicemeeter-monitor".into())
        .spawn(move || loop {
            if stop_rx.try_recv().is_ok() {
                break;
            }
            let level = crate::voicemeeter::try_get_level(&friendly_name).unwrap_or(0.0);
            if peak_tx.try_send(level).is_err() {
                break;
            }
            std::thread::sleep(METER_EMIT_INTERVAL);
        })
        .ok();

    ActiveStream::Voicemeeter(stop_tx)
}

// ── Unified capture entry point ────────────────────────────────────────────

pub fn start_audio_capture(
    device_id: &str,
    peak_tx: Option<std::sync::mpsc::SyncSender<f32>>,
) -> Result<(ActiveStream, ringbuf::HeapCons<f32>, cpal::StreamConfig)> {
    if let Some(name) = device_id.strip_prefix("output:") {
        start_wasapi_loopback(name, peak_tx)
    } else if let Some(name) = device_id.strip_prefix("input:") {
        start_cpal_input(name, peak_tx)
    } else {
        // Legacy bare id: try loopback first, then input.
        match start_wasapi_loopback(device_id, peak_tx.clone()) {
            Ok(result) => Ok(result),
            Err(_) => start_cpal_input(device_id, peak_tx),
        }
    }
}

// ── Stream handle ──────────────────────────────────────────────────────────

pub enum ActiveStream {
    Cpal(cpal::Stream),
    /// Drop this sender to signal the WASAPI loopback thread to stop.
    Wasapi(std::sync::mpsc::SyncSender<()>),
    /// Drop this sender to signal the Voicemeeter (VBVMR_GetLevel) polling thread to stop —
    /// see `start_voicemeeter_monitor`. Metering-only: no PCM samples, so this variant is never
    /// used for actual stream capture, only `Command::StartAudioMonitor` sessions.
    Voicemeeter(std::sync::mpsc::SyncSender<()>),
}

// Safety: cpal::Stream is not Send; we hold it only for its Drop lifetime.
#[allow(clippy::non_send_fields_in_send_ty)]
unsafe impl Send for ActiveStream {}

impl Drop for ActiveStream {
    fn drop(&mut self) {
        match self {
            ActiveStream::Wasapi(tx) | ActiveStream::Voicemeeter(tx) => {
                let _ = tx.try_send(());
            }
            ActiveStream::Cpal(_) => {}
        }
    }
}

// ── CPAL input path (microphones / physical inputs) ───────────────────────

fn start_cpal_input(
    name: &str,
    peak_tx: Option<std::sync::mpsc::SyncSender<f32>>,
) -> Result<(ActiveStream, ringbuf::HeapCons<f32>, cpal::StreamConfig)> {
    let host = cpal::default_host();

    let device = if name.is_empty() {
        host.default_input_device()
            .ok_or_else(|| anyhow!("No default input device"))?
    } else {
        host.input_devices()?
            .find(|x| x.name().unwrap_or_default() == name)
            .ok_or_else(|| anyhow!("Input device '{name}' not found"))?
    };

    let config = device.default_input_config()?;
    let sample_format = config.sample_format();
    let config: cpal::StreamConfig = config.into();

    let rb = HeapRb::<f32>::new(config.sample_rate.0 as usize * 4);
    let (mut prod, cons) = rb.split();
    let err_fn = |err| tracing::error!("CPAL input error: {}", err);

    // Peak accumulation happens on CPAL's own audio-callback thread; a separate lightweight
    // thread drains it every ~100ms and forwards to peak_tx — same cadence/shape as the WASAPI
    // loopback path's UI-meter emission, just bridged via a shared accumulator instead of being
    // computed inline (CPAL's callback doesn't own a long-lived loop to time it in directly).
    let peak_accum = std::sync::Arc::new(std::sync::Mutex::new(0.0f32));
    let peak_accum_cb = peak_accum.clone();

    let stream = match sample_format {
        cpal::SampleFormat::F32 => device.build_input_stream(
            &config,
            move |data: &[f32], _: &_| {
                prod.push_slice(data);
                if let Ok(mut m) = peak_accum_cb.try_lock() {
                    for &s in data {
                        let a = s.abs();
                        if a > *m { *m = a; }
                    }
                }
            },
            err_fn, None,
        )?,
        cpal::SampleFormat::I16 => device.build_input_stream(
            &config,
            move |data: &[i16], _: &_| {
                if let Ok(mut m) = peak_accum_cb.try_lock() {
                    for &s in data {
                        let f = s as f32 / i16::MAX as f32;
                        let a = f.abs();
                        if a > *m { *m = a; }
                        let _ = prod.try_push(f);
                    }
                } else {
                    for &s in data { let _ = prod.try_push(s as f32 / i16::MAX as f32); }
                }
            },
            err_fn, None,
        )?,
        cpal::SampleFormat::U16 => device.build_input_stream(
            &config,
            move |data: &[u16], _: &_| {
                if let Ok(mut m) = peak_accum_cb.try_lock() {
                    for &s in data {
                        let f = (s as f32 - u16::MAX as f32 / 2.0) / (u16::MAX as f32 / 2.0);
                        let a = f.abs();
                        if a > *m { *m = a; }
                        let _ = prod.try_push(f);
                    }
                } else {
                    for &s in data {
                        let _ = prod.try_push(
                            (s as f32 - u16::MAX as f32 / 2.0) / (u16::MAX as f32 / 2.0)
                        );
                    }
                }
            },
            err_fn, None,
        )?,
        _ => return Err(anyhow!("Unsupported sample format")),
    };

    stream.play()?;

    if let Some(tx) = peak_tx {
        std::thread::Builder::new()
            .name("cpal-input-meter".into())
            .spawn(move || loop {
                std::thread::sleep(METER_EMIT_INTERVAL);
                let level = {
                    let mut m = peak_accum.lock().unwrap();
                    std::mem::replace(&mut *m, 0.0)
                };
                // Err means the receiver (this monitor/stream session) has ended — stop
                // reporting. The capture itself keeps running via its own ActiveStream lifetime.
                if tx.try_send(level).is_err() {
                    break;
                }
            })
            .ok();
    }

    Ok((ActiveStream::Cpal(stream), cons, config))
}

// ── WASAPI loopback path (Voicemeeter / speakers / virtual cables) ─────────

fn start_wasapi_loopback(
    name: &str,
    peak_tx: Option<std::sync::mpsc::SyncSender<f32>>,
) -> Result<(ActiveStream, ringbuf::HeapCons<f32>, cpal::StreamConfig)> {
    // Use CPAL to resolve sample_rate + channels for the device.
    let host = cpal::default_host();
    let device = if name.is_empty() {
        host.default_output_device()
            .ok_or_else(|| anyhow!("No default output device"))?
    } else {
        host.output_devices()?
            .find(|x| x.name().unwrap_or_default() == name)
            .ok_or_else(|| anyhow!("Output device '{name}' not found — is Voicemeeter running?"))?
    };

    let out_cfg     = device.default_output_config()?;
    let sample_rate = out_cfg.sample_rate().0;
    let channels    = out_cfg.channels() as u32;

    let stream_config = cpal::StreamConfig {
        channels:    channels as u16,
        sample_rate: cpal::SampleRate(sample_rate),
        buffer_size: cpal::BufferSize::Default,
    };

    let rb = HeapRb::<f32>::new(sample_rate as usize * channels as usize * 4);
    let (mut prod, cons) = rb.split();

    let (stop_tx, stop_rx) = std::sync::mpsc::sync_channel::<()>(1);
    let dev_name = name.to_owned();

    std::thread::Builder::new()
        .name("wasapi-loopback".into())
        .spawn(move || {
            // Safety: WASAPI COM calls are all wrapped in this dedicated thread.
            let result = unsafe {
                wasapi_loopback_thread(&dev_name, sample_rate, channels, &mut prod, stop_rx, peak_tx)
            };
            if let Err(e) = result {
                tracing::error!("WASAPI loopback thread error: {e}");
            }
        })
        .map_err(|e| anyhow!("Failed to spawn WASAPI loopback thread: {e}"))?;

    Ok((ActiveStream::Wasapi(stop_tx), cons, stream_config))
}

// ── WASAPI loopback thread implementation ─────────────────────────────────

unsafe fn wasapi_loopback_thread(
    dev_name: &str,
    sample_rate: u32,
    channels: u32,
    prod: &mut ringbuf::HeapProd<f32>,
    stop_rx: std::sync::mpsc::Receiver<()>,
    peak_tx: Option<std::sync::mpsc::SyncSender<f32>>,
) -> Result<()> {
    use windows::Win32::Foundation::{CloseHandle, HANDLE, PROPERTYKEY};
    use windows::Win32::Media::Audio::{
        eConsole, eRender,
        IAudioCaptureClient, IAudioClient,
        IMMDeviceEnumerator, MMDeviceEnumerator,
        AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, AUDCLNT_STREAMFLAGS_LOOPBACK,
        DEVICE_STATE_ACTIVE, WAVEFORMATEX, WAVEFORMATEXTENSIBLE,
    };
    use windows::Win32::Media::Multimedia::WAVE_FORMAT_IEEE_FLOAT;
    use windows::Win32::System::Threading::{
        CreateEventW, GetCurrentThread, SetThreadPriority, WaitForSingleObject, THREAD_PRIORITY_TIME_CRITICAL,
    };
    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = {00000003-0000-0010-8000-00aa00389b71}
    const KSDATAFORMAT_SUBTYPE_IEEE_FLOAT: GUID = GUID::from_values(
        0x0000_0003, 0x0000, 0x0010, [0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71],
    );
    use windows::Win32::System::Com::{
        CoCreateInstance, CoInitializeEx, CoTaskMemFree, CLSCTX_ALL, COINIT_MULTITHREADED, STGM_READ,
    };
    use windows::Win32::System::Com::StructuredStorage::PropVariantClear;
    use windows::Win32::System::Variant::VT_LPWSTR;
    use windows::core::GUID;

    // Matches the priority cpal's own WASAPI backend gives the mic/input capture thread (see
    // cpal::host::wasapi::stream::boost_current_thread_priority — THREAD_PRIORITY_TIME_CRITICAL).
    // Without this, this hand-rolled loopback thread ran at plain default priority while every
    // other thread on the streaming critical path (video encode loop, RTMP writer) runs elevated
    // — under CPU pressure the OS scheduler starves this one first, and a starved-then-resumed
    // capture thread delivers audio in stall/burst bursts instead of a steady stream. That skew
    // matters more than it would for an isolated audio glitch: video and audio packets share one
    // av_interleaved_write_frame call (see streaming.rs's rtmp-writer thread), so a temporally
    // skewed run of audio packets can stall that call waiting to reorder against video — visible
    // to viewers as buffering, not just a local audio hiccup. Only ever exercised for "output:"
    // (loopback) sources — mic/"input:" capture already goes through cpal, which does this itself.
    let _ = SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);

    let _ = CoInitializeEx(None, COINIT_MULTITHREADED);

    let enumerator: IMMDeviceEnumerator =
        CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;

    // Find the render endpoint matching dev_name, or fall back to default.
    let mm_device = if dev_name.is_empty() {
        enumerator.GetDefaultAudioEndpoint(eRender, eConsole)?
    } else {
        let collection = enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)?;
        let count = collection.GetCount().unwrap_or(0);

        // PKEY_Device_FriendlyName = {a45c254e-df1c-4efd-8020-67d146a850e0}, pid=14
        let pkey = PROPERTYKEY {
            fmtid: GUID::from_u128(0xa45c254e_df1c_4efd_8020_67d146a850e0),
            pid: 14,
        };

        let mut found = None;
        let mut found_friendly = String::new();
        for i in 0..count {
            if let Ok(dev) = collection.Item(i) {
                if let Ok(ps) = dev.OpenPropertyStore(STGM_READ) {
                    // GetValue returns PROPVARIANT directly in windows 0.61
                    if let Ok(mut pv) = ps.GetValue(&pkey) {
                        // vt == VT_LPWSTR (31) — device friendly name is a wide string ptr
                        if pv.Anonymous.Anonymous.vt == VT_LPWSTR {
                            let ptr = pv.Anonymous.Anonymous.Anonymous.pwszVal;
                            if !ptr.is_null() {
                                if let Ok(friendly) = ptr.to_string() {
                                    if friendly.contains(dev_name) || dev_name.contains(&friendly) {
                                        let _ = PropVariantClear(&mut pv);
                                        found_friendly = friendly;
                                        found = Some(dev);
                                        break;
                                    }
                                }
                            }
                        }
                        let _ = PropVariantClear(&mut pv);
                    }
                }
            }
        }

        match found {
            Some(d) => {
                tracing::info!("WASAPI: matched render endpoint '{found_friendly}' for request '{dev_name}'");
                d
            }
            None => {
                tracing::warn!(
                    "WASAPI: no render endpoint matched '{dev_name}', falling back to default"
                );
                enumerator.GetDefaultAudioEndpoint(eRender, eConsole)?
            }
        }
    };

    let audio_client: IAudioClient = mm_device.Activate::<IAudioClient>(CLSCTX_ALL, None)?;

    // In shared mode, Initialize() requires the engine's actual mix format.
    // Building a custom WAVEFORMATEX with WAVE_FORMAT_IEEE_FLOAT fails because
    // Windows uses WAVE_FORMAT_EXTENSIBLE internally — GetMixFormat() returns the
    // correct format to use.
    let mix_fmt_ptr: *mut WAVEFORMATEX = audio_client.GetMixFormat()?;

    // RAII guard: ensures CoTaskMemFree is called even if Initialize returns Err.
    struct ComMem(*mut std::ffi::c_void);
    impl Drop for ComMem {
        fn drop(&mut self) { unsafe { CoTaskMemFree(Some(self.0)); } }
    }
    let _fmt_guard = ComMem(mix_fmt_ptr.cast());

    let (is_float_format, bits_per_sample, actual_channels) = {
        // WAVEFORMATEX (and WAVEFORMATEXTENSIBLE) are repr(packed) in windows-rs —
        // copy the struct to the stack via read_unaligned before accessing fields.
        let fmt: WAVEFORMATEX = std::ptr::read_unaligned(mix_fmt_ptr);
        const WAVE_FORMAT_EXTENSIBLE_TAG: u16 = 0xFFFE;
        let is_float = if fmt.wFormatTag == WAVE_FORMAT_EXTENSIBLE_TAG {
            let ext_ptr = mix_fmt_ptr as *const WAVEFORMATEXTENSIBLE;
            let sub_fmt: GUID = std::ptr::read_unaligned(
                std::ptr::addr_of!((*ext_ptr).SubFormat)
            );
            sub_fmt == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
        } else {
            fmt.wFormatTag == WAVE_FORMAT_IEEE_FLOAT as u16
        };
        // Copy fields out of the packed struct before use in macros (which take references).
        let mix_channels = fmt.nChannels;
        let mix_rate = fmt.nSamplesPerSec;
        let mix_bits = fmt.wBitsPerSample;
        let fmt_tag_name = if fmt.wFormatTag == WAVE_FORMAT_EXTENSIBLE_TAG { "EXTENSIBLE" }
            else if fmt.wFormatTag == WAVE_FORMAT_IEEE_FLOAT as u16 { "IEEE_FLOAT" }
            else { "PCM" };
        tracing::info!(
            "WASAPI mix format: {} ch @ {} Hz, {} bits/sample, tag={} float={} (device='{dev_name}')",
            mix_channels, mix_rate, mix_bits, fmt_tag_name, is_float
        );
        if mix_channels as u32 != channels || mix_rate != sample_rate {
            tracing::warn!(
                "WASAPI mix format mismatch: CPAL reported {} ch @ {} Hz, WASAPI reports {} ch @ {} Hz - using WASAPI values",
                channels, sample_rate, mix_channels, mix_rate
            );
        }
        // Always use the actual mix channel count for buffer math — WASAPI allocated
        // the buffer with mix_channels frames, not the CPAL-reported channel count.
        (is_float, mix_bits, mix_channels as u32)
    };

    // 200ms buffer in 100-ns units.
    audio_client.Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
        2_000_000,
        0,
        mix_fmt_ptr,
        None,
    )?;
    // _fmt_guard drops here (or on any earlier ? propagation), freeing mix_fmt_ptr.

    // Event-driven instead of polling: without this, the capture loop below would need to
    // sleep-and-recheck GetBuffer on a fixed interval, waking (and making a COM call) far more
    // often than audio is actually ready — measurably burns CPU per monitored/streamed device,
    // multiplied by however many are open at once. WASAPI signals this event each time a new
    // buffer is ready, so the loop can block until there's real work instead.
    let capture_event = CreateEventW(None, false, false, None)?;
    struct EventGuard(HANDLE);
    impl Drop for EventGuard {
        fn drop(&mut self) { unsafe { let _ = CloseHandle(self.0); } }
    }
    let _event_guard = EventGuard(capture_event);
    audio_client.SetEventHandle(capture_event)?;

    let capture_client: IAudioCaptureClient = audio_client.GetService()?;
    audio_client.Start()?;

    tracing::info!(
        "WASAPI loopback started: {} ch @ {} Hz (device='{}')",
        actual_channels, sample_rate, dev_name
    );

    // ── Diagnostic: log peak level every 3 s so we can tell if audio is flowing ──
    let mut peak_frames:    u64  = 0;
    let mut silent_frames:  u64  = 0; // frames with AUDCLNT_BUFFERFLAGS_SILENT set
    let mut peak_max:       f32  = 0.0; // 3-s log accumulator
    let mut level_peak_max: f32  = 0.0; // 100-ms UI meter accumulator
    let mut last_peak_log        = std::time::Instant::now();
    let mut last_level_emit      = std::time::Instant::now();

    loop {
        if stop_rx.try_recv().is_ok() { break; }

        let mut p_data: *mut u8 = std::ptr::null_mut();
        let mut num_frames: u32 = 0;
        let mut flags: u32 = 0;

        match capture_client.GetBuffer(&mut p_data, &mut num_frames, &mut flags, None, None) {
            Ok(()) if num_frames > 0 => {
                let n_samples = num_frames as usize * actual_channels as usize;
                peak_frames += num_frames as u64;

                // AUDCLNT_BUFFERFLAGS_SILENT (0x2) — device explicitly reported silence.
                if flags & 0x2 != 0 {
                    silent_frames += num_frames as u64;
                    for _ in 0..n_samples { let _ = prod.try_push(0.0f32); }
                } else if is_float_format {
                    let slice = std::slice::from_raw_parts(p_data as *const f32, n_samples);
                    for &s in slice {
                        let a = s.abs();
                        if a > peak_max { peak_max = a; }
                        if a > level_peak_max { level_peak_max = a; }
                        // Use try_push for consistent overflow behavior: drop samples if the
                        // consumer is slow, rather than blocking or panicking.
                        let _ = prod.try_push(s);
                    }
                } else if bits_per_sample == 16 {
                    let slice = std::slice::from_raw_parts(p_data as *const i16, n_samples);
                    for &s in slice {
                        let f = s as f32 / 32768.0;
                        let a = f.abs();
                        if a > peak_max { peak_max = a; }
                        if a > level_peak_max { level_peak_max = a; }
                        let _ = prod.try_push(f);
                    }
                } else if bits_per_sample == 32 {
                    let slice = std::slice::from_raw_parts(p_data as *const i32, n_samples);
                    // Divide by 2^31 (not i32::MAX) — standard audio convention keeping
                    // the range (-1.0, 1.0]: i32::MIN maps to exactly -1.0, i32::MAX to ~1.0.
                    for &s in slice {
                        let f = s as f32 / 2_147_483_648.0;
                        let a = f.abs();
                        if a > peak_max { peak_max = a; }
                        if a > level_peak_max { level_peak_max = a; }
                        let _ = prod.try_push(f);
                    }
                } else {
                    tracing::warn!("WASAPI: unsupported PCM bit depth {bits_per_sample}, skipping frame");
                    for _ in 0..n_samples { let _ = prod.try_push(0.0f32); }
                }
                let _ = capture_client.ReleaseBuffer(num_frames);
            }
            Ok(()) => {
                // num_frames == 0: nothing ready yet. Block on the event WASAPI signals when
                // a new buffer is available, instead of polling — bounded to 200ms so stop_rx
                // above still gets rechecked periodically even if the device stops signaling.
                let _ = WaitForSingleObject(capture_event, 200);
            }
            Err(e) => {
                // Real device error (e.g. AUDCLNT_E_DEVICE_INVALIDATED). Break out so
                // the thread exits cleanly rather than looping forever on 1ms sleeps.
                tracing::error!("WASAPI GetBuffer error: {e}");
                break;
            }
        }

        // Emit live peak level for the UI level meter.
        if last_level_emit.elapsed() >= METER_EMIT_INTERVAL {
            if let Some(ref tx) = peak_tx {
                let _ = tx.try_send(level_peak_max);
            }
            level_peak_max = 0.0;
            last_level_emit = std::time::Instant::now();
        }

        // Emit a peak-level heartbeat every 3 seconds.
        if last_peak_log.elapsed() >= std::time::Duration::from_secs(3) {
            let db = if peak_max > 0.0 {
                20.0 * peak_max.log10()
            } else {
                f32::NEG_INFINITY
            };
            tracing::info!(
                "WASAPI audio peak: {:.1} dBFS | frames: total={} silent={} (device='{dev_name}')",
                db, peak_frames, silent_frames
            );
            peak_max      = 0.0;
            peak_frames   = 0;
            silent_frames = 0;
            last_peak_log = std::time::Instant::now();
        }
    }

    let _ = audio_client.Stop();
    tracing::info!("WASAPI loopback stopped (device='{dev_name}')");
    Ok(())
}