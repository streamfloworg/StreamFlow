#![allow(unsafe_code)]

use std::collections::HashMap;
use std::ffi::CString;
use std::ptr;
use std::sync::atomic::{AtomicI32, AtomicI64, Ordering};
use std::sync::{Arc, Mutex, OnceLock, RwLock};
use std::time::{Duration, Instant};

use anyhow::{anyhow, Context, Result};
use ffmpeg_sys_next::*;
use serde::Deserialize;
use tokio::sync::{broadcast, mpsc};
use windows::Win32::System::Threading::{GetCurrentThread, SetThreadPriority, THREAD_PRIORITY_ABOVE_NORMAL, THREAD_PRIORITY_HIGHEST};

use crate::capture::RawFrame;
use streamflow_ipc::{AudioSourceConfig, StreamSourceDef};

// Removed CompositeFrame

/// Per-encoder tuning loaded from `encoder-config.json` at stream start.
/// Settings that come from Electron (bitrate, fps, resolution, encoder name)
/// are not here - they live in StreamOptions.
#[derive(Debug, Deserialize, Default, Clone)]
pub struct EncoderPreset {
    /// Arbitrary key/value pairs forwarded to av_opt_set on the codec's priv_data.
    #[serde(default)]
    pub options: HashMap<String, String>,
}

#[derive(Debug, Deserialize, Default, Clone)]
pub struct EncoderConfig {
    #[serde(default)]
    pub bitrate_kbps: u32,
    #[serde(default)]
    pub libx264: EncoderPreset,
    #[serde(default)]
    pub h264_nvenc: EncoderPreset,
    #[serde(default)]
    pub h264_amf: EncoderPreset,
    #[serde(default)]
    pub h264_qsv: EncoderPreset,
}

fn load_encoder_config() -> EncoderConfig {
    // Look next to the binary first, then in the current working directory.
    // In dev, Electron spawns core from the project root, so encoder-config.json
    // placed there is found via current_dir.
    let candidates: Vec<std::path::PathBuf> = [
        std::env::current_exe()
            .ok()
            .and_then(|p| p.parent().map(|d| d.join("encoder-config.json"))),
        std::env::current_dir().ok().map(|d| d.join("encoder-config.json")),
    ]
    .into_iter()
    .flatten()
    .collect();

    for path in &candidates {
        match std::fs::read_to_string(path) {
            Ok(content) => match serde_json::from_str::<EncoderConfig>(&content) {
                Ok(cfg) => {
                    tracing::info!("encoder config loaded from {}", path.display());
                    return cfg;
                }
                Err(e) => tracing::warn!("encoder-config.json parse error at {}: {e}", path.display()),
            },
            Err(_) => {}
        }
    }

    tracing::info!(
        "encoder-config.json not found (searched: {}); using built-in defaults",
        candidates.iter().map(|p| p.display().to_string()).collect::<Vec<_>>().join(", ")
    );
    EncoderConfig::default()
}

// ── Global config cache ───────────────────────────────────────────────────────

static ENCODER_CONFIG: OnceLock<Arc<RwLock<EncoderConfig>>> = OnceLock::new();

fn config_cache() -> Arc<RwLock<EncoderConfig>> {
    Arc::clone(ENCODER_CONFIG.get_or_init(|| {
        Arc::new(RwLock::new(load_encoder_config()))
    }))
}

/// Start a background thread that watches for `encoder-config.json` changes and
/// reloads the in-memory cache. Changes apply on the next stream start - FFmpeg
/// does not allow re-configuring an encoder that is already open.
pub fn start_config_watcher() {
    use notify::{recommended_watcher, RecursiveMode, Watcher};

    // Collect candidate directories to watch (binary dir and CWD), deduplicated.
    let mut watch_dirs: Vec<std::path::PathBuf> = Vec::new();
    if let Some(exe_dir) = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(std::path::Path::to_path_buf))
    {
        watch_dirs.push(exe_dir);
    }
    if let Ok(cwd) = std::env::current_dir() {
        if !watch_dirs.contains(&cwd) {
            watch_dirs.push(cwd);
        }
    }
    if watch_dirs.is_empty() {
        tracing::warn!("no watchable directories found; encoder config live-reload disabled");
        return;
    }

    let cache = config_cache();

    std::thread::spawn(move || {
        let (tx, rx) = std::sync::mpsc::channel::<notify::Result<notify::Event>>();
        let mut watcher = match recommended_watcher(move |res| {
            let _ = tx.send(res);
        }) {
            Ok(w) => w,
            Err(e) => {
                tracing::warn!("file watcher init failed: {e}");
                return;
            }
        };

        for dir in &watch_dirs {
            match watcher.watch(dir.as_path(), RecursiveMode::NonRecursive) {
                Ok(()) => tracing::info!("encoder config watcher: watching {}", dir.display()),
                Err(e) => tracing::warn!("encoder config watcher: cannot watch {}: {e}", dir.display()),
            }
        }

        for res in rx {
            match res {
                Ok(event) => {
                    let is_config = event.paths.iter().any(|p| {
                        p.file_name()
                            .map(|n| n == "encoder-config.json")
                            .unwrap_or(false)
                    });
                    if !is_config {
                        continue;
                    }
                    // Try each candidate path and load the first that parses cleanly.
                    for dir in &watch_dirs {
                        let path = dir.join("encoder-config.json");
                        match std::fs::read_to_string(&path) {
                            Ok(content) => match serde_json::from_str::<EncoderConfig>(&content) {
                                Ok(cfg) => {
                                    tracing::info!(
                                        "encoder config reloaded from {} — \
                                         bitrate_kbps={} \
                                         nvenc_opts={:?} \
                                         x264_opts={:?} \
                                         amf_opts={:?} \
                                         qsv_opts={:?}",
                                        path.display(),
                                        cfg.bitrate_kbps,
                                        cfg.h264_nvenc.options,
                                        cfg.libx264.options,
                                        cfg.h264_amf.options,
                                        cfg.h264_qsv.options,
                                    );
                                    if let Ok(mut guard) = cache.write() {
                                        *guard = cfg;
                                    }
                                    break;
                                }
                                Err(e) => tracing::warn!("encoder-config.json parse error: {e}"),
                            },
                            Err(_) => {}
                        }
                    }
                }
                Err(e) => tracing::warn!("file watcher error: {e}"),
            }
        }
    });
}

// ── Async RTMP write types ────────────────────────────────────────────────────

/// An encoded AVPacket delivered to the RTMP writer thread via channel.
/// Drop calls av_packet_free, which calls av_packet_unref internally.
struct OwnedPacket(*mut AVPacket);
unsafe impl Send for OwnedPacket {}
impl Drop for OwnedPacket {
    fn drop(&mut self) {
        unsafe { av_packet_free(&mut self.0); }
    }
}

/// Carries the raw AVFormatContext pointer across the thread boundary.
/// The RTMP write thread takes ownership after the header is written.
struct FormatCtxSend(*mut AVFormatContext);
unsafe impl Send for FormatCtxSend {}
impl FormatCtxSend {
    /// Consumes the wrapper and returns the raw pointer.
    /// Using a method here ensures the closure captures `FormatCtxSend` (Send),
    /// not the inner `*mut AVFormatContext` field (not Send per RFC 2229).
    fn into_raw(self) -> *mut AVFormatContext { self.0 }
}

struct PipScaler {
    sws: *mut SwsContext,
    src_w: i32,
    src_h: i32,
    dst_w: i32,
    dst_h: i32,
}

impl Drop for PipScaler {
    fn drop(&mut self) {
        if !self.sws.is_null() {
            unsafe { sws_freeContext(self.sws); }
        }
    }
}

// ── Public interface ──────────────────────────────────────────────────────────

pub struct StreamOptions {
    pub rtmp_url: String,
    pub bitrate_kbps: u32,
    pub fps: u32,
    pub output_width: Option<u32>,
    pub output_height: Option<u32>,
    pub fit_mode: Option<String>,
    /// "libx264" | "h264_nvenc" | "h264_amf" | "h264_qsv"
    pub encoder: String,
    pub sources: Vec<StreamSourceDef>,
    pub audio_sources: Vec<AudioSourceConfig>,
    /// If set, record the stream to this file path via stream copy (MP4).
    pub record_path: Option<String>,
}

#[derive(Debug)]
pub enum StreamEvent {
    Started { width: u32, height: u32 },
    Status { frame: u64, fps: f32, bitrate_kbps: u32 },
    AudioLevel { peak_db: f32 },
    /// Same shape as AudioLevel, but tagged per-device — used by standalone audio monitor
    /// sessions (see Command::StartAudioMonitor in main.rs), which reuse this same StreamEvent
    /// channel/bridge pattern even though they're independent of whether a stream is active.
    AudioDeviceLevel { device_id: String, peak_db: f32 },
    /// Periodic OS-level volume/mute state for a monitored device — see
    /// `Event::AudioDeviceVolume` in the ipc crate for the full rationale.
    AudioDeviceVolume { device_id: String, volume: f32, muted: bool },
    Error(String),
    Stopped,
}

/// One mixer input's live gain/mute/solo, shared between `StreamSession` and the mixer thread
/// so [`StreamSession::set_audio_mix`] can update it after the stream has already started
/// (see `Command::SetAudioMix`) instead of only baking these in once at `StartStream`.
struct MixState {
    device_id: String,
    gain: f32,
    muted: bool,
    solo: bool,
}

pub struct StreamSession {
    stop_tx: std::sync::mpsc::SyncSender<()>,
    thread: Option<std::thread::JoinHandle<()>>,
    _audio_streams: Vec<crate::audio::ActiveStream>,
    _mixer_stop_tx: Option<std::sync::mpsc::SyncSender<()>>,
    mix_state: Option<Arc<Mutex<Vec<MixState>>>>,
}

impl StreamSession {
    pub fn start(
        opts: StreamOptions,
        frame_rx: broadcast::Receiver<Arc<RawFrame>>,
        event_tx: mpsc::UnboundedSender<StreamEvent>,
    ) -> Self {
        let (stop_tx, stop_rx) = std::sync::mpsc::sync_channel::<()>(1);

        // ── Audio capture (one per audio_sources entry) ────────────────────────
        // Logged at warn (not info) deliberately — the core only runs at the "warn" tracing
        // level unless launched with --verbose, so this is the only way "were any audio
        // devices even requested" is visible in the log the app already captures, without
        // needing a separate verbose-mode round trip to diagnose a silent video-only stream.
        tracing::warn!(
            "StartStream requested {} audio device(s): {:?}",
            opts.audio_sources.len(),
            opts.audio_sources.iter().map(|a| &a.device_id).collect::<Vec<_>>(),
        );

        let (peak_tx, peak_rx) = std::sync::mpsc::sync_channel::<f32>(4 * opts.audio_sources.len().max(1));
        // (stream, consumer, config, device_id, gain, muted, solo) — mix settings (and the
        // device id, so a later Command::SetAudioMix can find the right slot) travel alongside
        // each capture so they survive the format-mismatch filtering below in the same order as
        // whatever streams/consumers actually get kept.
        let mut raw_captures: Vec<(crate::audio::ActiveStream, ringbuf::HeapCons<f32>, cpal::StreamConfig, String, f32, bool, bool)> = Vec::new();

        for src in &opts.audio_sources {
            match crate::audio::start_audio_capture(&src.device_id, Some(peak_tx.clone())) {
                Ok((stream, cons, config)) => {
                    tracing::warn!("audio capture started: device='{}'", src.device_id);
                    raw_captures.push((stream, cons, config, src.device_id.clone(), src.gain, src.muted, src.solo));
                }
                Err(e) => tracing::error!("audio capture failed: device='{}' error={e}", src.device_id),
            }
        }

        // Bridge: peak values from any capture thread → StreamEvent::AudioLevel.
        if !raw_captures.is_empty() {
            let audio_level_evt_tx = event_tx.clone();
            std::thread::Builder::new()
                .name("audio-peak-bridge".into())
                .spawn(move || {
                    while let Ok(peak) = peak_rx.recv() {
                        // -96 dB floor instead of -infinity: serde_json encodes non-finite
                        // floats as `null`, which fails to deserialize into C#'s non-nullable
                        // float field.
                        let db = if peak > 0.0 { (20.0 * peak.log10()).max(-96.0) } else { -96.0 };
                        let _ = audio_level_evt_tx.send(StreamEvent::AudioLevel { peak_db: db });
                    }
                })
                .ok();
        }
        drop(peak_tx); // each WASAPI/CPAL thread holds a clone; release ours

        // Always routed through the mixer stage below, even for a single device — gain/mute/
        // solo need to apply uniformly, so there's no "just pass the raw stream through"
        // shortcut anymore (the extra buffer copy for the N=1 case is not worth the duplicated
        // logic path).
        let mut mixer_stop: Option<std::sync::mpsc::SyncSender<()>> = None;
        let mut mix_state: Option<Arc<Mutex<Vec<MixState>>>> = None;
        let (audio_cons, audio_config, audio_streams) = if raw_captures.is_empty() {
            (None, None, Vec::new())
        } else {
            let primary_channels = raw_captures[0].2.channels;
            let primary_rate     = raw_captures[0].2.sample_rate;

            let mut streams: Vec<crate::audio::ActiveStream> = Vec::new();
            let mut consumers: Vec<ringbuf::HeapCons<f32>> = Vec::new();
            let mut states: Vec<MixState> = Vec::new();
            for (stream, cons, config, device_id, gain, is_muted, is_solo) in raw_captures {
                if config.channels == primary_channels && config.sample_rate == primary_rate {
                    streams.push(stream);
                    consumers.push(cons);
                    states.push(MixState { device_id, gain, muted: is_muted, solo: is_solo });
                } else {
                    tracing::warn!(
                        "audio mixer: skipping device with mismatched format \
                         ({} ch @ {}Hz vs primary {} ch @ {}Hz)",
                        config.channels, config.sample_rate.0,
                        primary_channels, primary_rate.0,
                    );
                    // stream drops here → that capture thread stops
                }
            }
            let shared_state = Arc::new(Mutex::new(states));
            mix_state = Some(shared_state.clone());

            let n_ch = primary_channels as usize;
            let mix_buf_samples = primary_rate.0 as usize * n_ch * 4; // 4-second ring
            let mix_rb = ringbuf::HeapRb::<f32>::new(mix_buf_samples);
            let (mut mix_prod, mix_cons) = {
                use ringbuf::traits::Split;
                mix_rb.split()
            };

            let (mixer_stop_tx, mixer_stop_rx) = std::sync::mpsc::sync_channel::<()>(1);

            std::thread::Builder::new()
                .name("audio-mixer".into())
                .spawn(move || {
                    use ringbuf::traits::{Consumer as _, Observer as _, Producer as _};
                    use std::sync::mpsc::TryRecvError;
                    const CHUNK: usize = 512;
                    let mut sources = consumers;
                    let mut bufs: Vec<Vec<f32>> = (0..sources.len())
                        .map(|_| vec![0.0f32; CHUNK])
                        .collect();

                    loop {
                        match mixer_stop_rx.try_recv() {
                            Ok(()) | Err(TryRecvError::Disconnected) => break,
                            Err(TryRecvError::Empty) => {}
                        }

                        // Gated on the *maximum* occupied length across sources, not the minimum
                        // — a WASAPI loopback capture on an "output" device that isn't currently
                        // playing anything genuinely delivers little to no packets while idle
                        // (Windows suspends the audio engine on a render endpoint with nothing
                        // actively rendering). Gating on the minimum meant one idle/silent
                        // selected device could permanently stall `available` at 0, which starved
                        // every other source's contribution too — the entire mix would produce
                        // nothing until that one device resumed, easily the whole stream if it
                        // never did. Each source below now only contributes what it actually has
                        // this chunk and is zero-padded otherwise, instead of every source being
                        // required to keep pace with the busiest one.
                        let available = sources.iter()
                            .map(|c| c.occupied_len())
                            .max()
                            .unwrap_or(0);

                        if available == 0 {
                            std::thread::sleep(std::time::Duration::from_millis(5));
                            continue;
                        }

                        let to_mix = available.min(CHUNK);
                        let mut mixed = vec![0.0f32; to_mix];

                        // Re-read live so a mid-stream Command::SetAudioMix (see
                        // StreamSession::set_audio_mix) takes effect on the very next chunk —
                        // cheap enough per 512-sample chunk (a few hundred times/sec at most)
                        // that there's no need to cache/diff against the previous read.
                        let (gains, audible): (Vec<f32>, Vec<bool>) = {
                            let states = shared_state.lock().unwrap();
                            let any_solo = states.iter().any(|s| s.solo);
                            states.iter()
                                .map(|s| (s.gain, if any_solo { s.solo } else { !s.muted }))
                                .unzip()
                        };

                        for (i, src) in sources.iter_mut().enumerate() {
                            let buf = &mut bufs[i];
                            buf.clear();
                            buf.resize(to_mix, 0.0);
                            // Pop only what this source actually has ready (may be less than
                            // to_mix if it's idle/lagging this chunk) and leave the remainder
                            // zero-filled — draining still happens every iteration regardless of
                            // audibility, same as before, so a muted-but-still-capturing source's
                            // ring buffer doesn't back up.
                            let have = src.occupied_len().min(to_mix);
                            if have > 0 {
                                src.pop_slice(&mut buf[..have]);
                            }
                            if !audible[i] { continue; }

                            let gain = gains[i];
                            for (m, s) in mixed.iter_mut().zip(buf[..to_mix].iter()) {
                                *m = (*m + s * gain).clamp(-1.0, 1.0);
                            }
                        }

                        mix_prod.push_slice(&mixed);
                    }
                })
                .ok();

            let primary_config = cpal::StreamConfig {
                channels: primary_channels,
                sample_rate: primary_rate,
                buffer_size: cpal::BufferSize::Default,
            };
            mixer_stop = Some(mixer_stop_tx);
            (Some(mix_cons), Some(primary_config), streams)
        };

        // Bridge: async broadcast receiver -> bounded sync channel for the encoder thread.
        let (bridge_tx, bridge_rx) = std::sync::mpsc::sync_channel::<Arc<RawFrame>>(8);
        let bridge_evt = event_tx.clone();

        tokio::spawn(async move {
            let mut rx = frame_rx;
            loop {
                match rx.recv().await {
                    Ok(frame) => {
                        if bridge_tx.try_send(frame).is_err() {
                            tracing::trace!("encoder lagging - frame dropped");
                        }
                    }
                    Err(tokio::sync::broadcast::error::RecvError::Lagged(n)) => {
                        tracing::trace!("encoder missed {n} broadcast frames (lagged)");
                    }
                    Err(broadcast::error::RecvError::Closed) => {
                        let _ = bridge_evt.send(StreamEvent::Stopped);
                        break;
                    }
                }
            }
        });

        let thread = std::thread::spawn(move || {
            if let Err(e) = run_encoder(opts, bridge_rx, audio_cons, audio_config, stop_rx, &event_tx) {
                tracing::error!("stream encoder error: {e:#}");
                let _ = event_tx.send(StreamEvent::Error(format!("{e:#}")));
            }
            let _ = event_tx.send(StreamEvent::Stopped);
        });

        Self { stop_tx, thread: Some(thread), _audio_streams: audio_streams, _mixer_stop_tx: mixer_stop, mix_state }
    }

    pub fn stop(&mut self) {
        let _ = self.stop_tx.try_send(());
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
        // Signal mixer thread to exit (drops the sender, disconnecting the channel).
        let _ = self._mixer_stop_tx.take();
    }

    /// Live-updates one audio source's mix gain/mute/solo (see `Command::SetAudioMix`) — a
    /// no-op if no stream is active, no mixer was ever started (e.g. no audio sources), or
    /// `device_id` doesn't match any source passed to the original `StartStream`.
    pub fn set_audio_mix(&self, device_id: &str, gain: f32, muted: bool, solo: bool) {
        let Some(state) = &self.mix_state else { return };
        let mut states = state.lock().unwrap();
        if let Some(entry) = states.iter_mut().find(|s| s.device_id == device_id) {
            entry.gain = gain;
            entry.muted = muted;
            entry.solo = solo;
        }
    }
}

impl Drop for StreamSession {
    fn drop(&mut self) {
        self.stop();
    }
}

// ── Encoder thread ────────────────────────────────────────────────────────────

fn run_encoder(
    opts: StreamOptions,
    frame_rx: std::sync::mpsc::Receiver<Arc<RawFrame>>,
    audio_cons: Option<ringbuf::HeapCons<f32>>,
    audio_config: Option<cpal::StreamConfig>,
    stop_rx: std::sync::mpsc::Receiver<()>,
    event_tx: &mpsc::UnboundedSender<StreamEvent>,
) -> Result<()> {
    // Clone config while the lock is briefly held - avoids holding RwLock across unsafe FFI.
    let enc_cfg = config_cache()
        .read()
        .map_err(|_| anyhow!("encoder config lock poisoned"))?
        .clone();
    unsafe { run_encoder_unsafe(opts, enc_cfg, frame_rx, audio_cons, audio_config, stop_rx, event_tx) }
}

unsafe fn run_encoder_unsafe(
    opts: StreamOptions,
    enc_cfg: EncoderConfig,
    frame_rx: std::sync::mpsc::Receiver<Arc<RawFrame>>,
    audio_cons: Option<ringbuf::HeapCons<f32>>,
    audio_config: Option<cpal::StreamConfig>,
    stop_rx: std::sync::mpsc::Receiver<()>,
    event_tx: &mpsc::UnboundedSender<StreamEvent>,
) -> Result<()> {
    // Elevate thread priority to prevent Windows Game Mode from starving the 
    // background encode thread while the user plays the game.
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST);

    // Bumped from AV_LOG_WARNING while diagnosing the "connects but the platform shows no
    // data" class of bug (RTMP handshake completing successfully client-side tells us
    // nothing about why the receiving end silently drops the stream) — this surfaces
    // FFmpeg's actual handshake/publish dialogue (connect result, publish response,
    // codec negotiation) rather than only hard failures. It's captured by
    // CoreBridgeService.ReadStderrLoopAsync exactly like our own tracing output, so it
    // shows up in the same running log without any separate capture step.
    av_log_set_level(AV_LOG_DEBUG);

    // ── Wait for the first frame (gives us source dimensions) ─────────────────
    let first = frame_rx
        .recv_timeout(Duration::from_secs(5))
        .map_err(|_| anyhow!("timed out waiting for first frame before stream start"))?;

    let src_w = first.width as i32;
    let src_h = first.height as i32;
    let out_w = (opts.output_width.unwrap_or(first.width) & !1) as i32;
    let out_h = (opts.output_height.unwrap_or(first.height) & !1) as i32;
    let fit_mode = opts.fit_mode.unwrap_or_else(|| "contain".to_string());

    let out_aspect = out_w as f32 / out_h as f32;
    let src_aspect = src_w as f32 / src_h as f32;

    let mut crop_w = src_w;
    let mut crop_h = src_h;
    let mut crop_x = 0;
    let mut crop_y = 0;

    let mut dst_w = out_w;
    let mut dst_h = out_h;
    let mut dst_x = 0;
    let mut dst_y = 0;

    if fit_mode == "cover" {
        if src_aspect > out_aspect {
            crop_w = (src_h as f32 * out_aspect).round() as i32 & !1;
            crop_x = (src_w - crop_w) / 2 & !1;
        } else if src_aspect < out_aspect {
            crop_h = (src_w as f32 / out_aspect).round() as i32 & !1;
            crop_y = (src_h - crop_h) / 2 & !1;
        }
    } else if fit_mode == "contain" {
        if src_aspect > out_aspect {
            dst_h = (out_w as f32 / src_aspect).round() as i32 & !1;
            dst_y = (out_h - dst_h) / 2 & !1;
        } else if src_aspect < out_aspect {
            dst_w = (out_h as f32 * src_aspect).round() as i32 & !1;
            dst_x = (out_w - dst_w) / 2 & !1;
        }
    }

    let target_fps = opts.fps.max(1);
    // How long to wait for the next WGC frame before repeating the last one.
    // Using 2× the frame interval so a brief capture hitch doesn't immediately
    // repeat, but a genuine stall (e.g. WGC stopped) keeps the CBR bitrate filled.
    let frame_wait = Duration::from_millis((2000 / target_fps) as u64);

    // ── Output format context (FLV over RTMP) ─────────────────────────────────
    let url_c = CString::new(opts.rtmp_url.as_str()).context("invalid RTMP URL")?;
    let flv_c = CString::new("flv")?;
    let mut ofmt_ctx: *mut AVFormatContext = ptr::null_mut();
    check(
        avformat_alloc_output_context2(&mut ofmt_ctx, ptr::null(), flv_c.as_ptr(), url_c.as_ptr()),
        "avformat_alloc_output_context2",
    )?;

    // ── Find encoder ──────────────────────────────────────────────────────────
    let enc_name = CString::new(opts.encoder.as_str())?;
    let mut codec = avcodec_find_encoder_by_name(enc_name.as_ptr());
    if codec.is_null() {
        tracing::warn!("encoder '{}' not found - falling back to libx264", opts.encoder);
        let fallback = CString::new("libx264")?;
        codec = avcodec_find_encoder_by_name(fallback.as_ptr());
    }
    if codec.is_null() {
        avformat_free_context(ofmt_ctx);
        return Err(anyhow!("no H.264 encoder found"));
    }

    // ── Add video stream & codec context ─────────────────────────────────────
    let out_stream = avformat_new_stream(ofmt_ctx, ptr::null());
    if out_stream.is_null() {
        avformat_free_context(ofmt_ctx);
        return Err(anyhow!("avformat_new_stream failed"));
    }

    let codec_ctx = avcodec_alloc_context3(codec);
    if codec_ctx.is_null() {
        avformat_free_context(ofmt_ctx);
        return Err(anyhow!("avcodec_alloc_context3 failed"));
    }

    (*codec_ctx).codec_id   = (*codec).id;
    (*codec_ctx).bit_rate   = (opts.bitrate_kbps as i64) * 1000;
    (*codec_ctx).width      = out_w;
    (*codec_ctx).height     = out_h;
    (*codec_ctx).time_base  = AVRational { num: 1, den: target_fps as i32 };
    (*codec_ctx).framerate  = AVRational { num: target_fps as i32, den: 1 };
    (*codec_ctx).pix_fmt    = AVPixelFormat::AV_PIX_FMT_YUV420P;

    if !(*ofmt_ctx).oformat.is_null()
        && ((*(*ofmt_ctx).oformat).flags & AVFMT_GLOBALHEADER as i32) != 0
    {
        (*codec_ctx).flags |= AV_CODEC_FLAG_GLOBAL_HEADER as i32;
    }

    let preset = match opts.encoder.as_str() {
        "h264_nvenc" => &enc_cfg.h264_nvenc,
        "h264_amf"   => &enc_cfg.h264_amf,
        "h264_qsv"   => &enc_cfg.h264_qsv,
        _            => &enc_cfg.libx264,   // "libx264" | "" | unknown
    };

    // 2-second keyframe interval for all encoders
    (*codec_ctx).gop_size = (target_fps * 2) as i32;

    // B-frames require DTS reordering that FLV+av_write_frame cannot handle without
    // av_interleaved_write_frame (which causes 300ms+ TCP burst stalls) — this constraint is
    // about the muxing path, not the encoder, so it applies regardless of which one is active.
    // This was previously scoped to the h264_nvenc branch only, which meant libx264 (and
    // h264_amf/h264_qsv) kept their own default B-frame counts and produced non-monotonic
    // DTS/PTS in the FLV output — RTMP servers that validate timestamps strictly (YouTube, in
    // particular) can silently accept the publish handshake and then never surface the stream
    // as live, while more lenient ones (Twitch) mostly get away with it.
    (*codec_ctx).max_b_frames = 0;

    // Encoder-specific tuning applied before av_opt_set options
    if opts.encoder == "h264_nvenc" {
        // NVENC CBR: rc_max_rate = rc_buffer_size = bit_rate for stable CBR
        (*codec_ctx).rc_max_rate = (*codec_ctx).bit_rate;
        (*codec_ctx).rc_buffer_size = (*codec_ctx).bit_rate as i32;
        // Adaptive Quantization: redistributes bitrate from flat/dark regions to
        // high-detail areas (text, faces, edges) within each frame.
        // aq-strength 8 is a balanced default (range 1-15).
        set_opt(codec_ctx, "spatial-aq", "1");
        set_opt(codec_ctx, "temporal-aq", "1");
        set_opt(codec_ctx, "aq-strength", "8");
    }

    for (key, val) in &preset.options {
        set_opt(codec_ctx, key, val);
    }

    // ── Dump full encoder config before opening codec ─────────────────────────
    {
        let mut opts_sorted: Vec<(&String, &String)> = preset.options.iter().collect();
        opts_sorted.sort_by_key(|(k, _)| k.as_str());
        let opts_str = opts_sorted
            .iter()
            .map(|(k, v)| format!("{k}={v}"))
            .collect::<Vec<_>>()
            .join(", ");

        tracing::info!(
            "Encoder config  encoder={} bitrate={}kbps fps={} gop={} \
             size={}x{}{}x{} rc_max_rate={}kbps rc_buf={}kbps max_b={} \
             opts=[{}]",
            opts.encoder,
            opts.bitrate_kbps,
            target_fps,
            (*codec_ctx).gop_size,
            src_w, src_h, out_w, out_h,
            (*codec_ctx).rc_max_rate / 1000,
            (*codec_ctx).rc_buffer_size as i64 / 1000,
            (*codec_ctx).max_b_frames,
            opts_str,
        );
    }

    check(avcodec_open2(codec_ctx, codec, ptr::null_mut()), "avcodec_open2")?;
    check(avcodec_parameters_from_context((*out_stream).codecpar, codec_ctx),
          "avcodec_parameters_from_context")?;
    (*out_stream).time_base = (*codec_ctx).time_base;

    // ── Setup Audio Encoder ───────────────────────────────────────────────────
    let had_audio_source = audio_cons.is_some() && audio_config.is_some();
    let mut audio_enc = if let (Some(cons), Some(config)) = (audio_cons, audio_config) {
        match crate::audio_encoder::AudioEncoder::new(ofmt_ctx, &config, cons) {
            Ok(enc) => Some(enc),
            Err(e) => {
                tracing::error!("Failed to initialize audio encoder: {e}");
                None
            }
        }
    } else {
        None
    };

    if audio_enc.is_some() {
        tracing::warn!("Audio stream added to RTMP output");
    } else {
        tracing::warn!(
            "No audio stream in RTMP output — streaming video-only (had_audio_source={had_audio_source}). \
             Some platforms (YouTube in particular) accept the publish but never surface it as live for \
             a video-only stream."
        );
    }

    // ── Open RTMP output & write header ───────────────────────────────────────
    // rw_timeout (µs): caps how long avio blocks on a single TCP read/write.
    // Without this, av_interleaved_write_frame can block indefinitely on a
    // stalled RTMP server. 5 s is generous for live streaming.
    let rw_timeout_us = CString::new("5000000")?;
    let rw_timeout_key = CString::new("rw_timeout")?;
    let mut io_opts: *mut AVDictionary = ptr::null_mut();
    av_dict_set(&mut io_opts, rw_timeout_key.as_ptr(), rw_timeout_us.as_ptr(), 0);
    let ret = avio_open2(&mut (*ofmt_ctx).pb, url_c.as_ptr(), AVIO_FLAG_WRITE as i32,
                         ptr::null(), &mut io_opts);
    av_dict_free(&mut io_opts);
    check(ret, "avio_open2 - check RTMP URL and network")?;
    check(avformat_write_header(ofmt_ctx, ptr::null_mut()),
          "avformat_write_header - RTMP server rejected connection")?;

    if let Some(ref mut enc) = audio_enc {
        unsafe { enc.update_stream_timebase(ofmt_ctx); }
    }

    // ── Optional MP4 recording context (stream copy) ──────────────────────────
    // Build a second output context mirroring the RTMP context's streams.
    // Packets written to RTMP will also be cloned and written here.
    let rec_fmt_send: Option<FormatCtxSend> = if let Some(ref rec_path) = opts.record_path {
        let result: Option<FormatCtxSend> = (|| unsafe {
            let path_c = CString::new(rec_path.as_str()).ok()?;
            let mp4_c = CString::new("mp4").ok()?;
            let mut rec_ctx: *mut AVFormatContext = ptr::null_mut();
            if avformat_alloc_output_context2(&mut rec_ctx, ptr::null(), mp4_c.as_ptr(), path_c.as_ptr()) < 0
                || rec_ctx.is_null()
            {
                tracing::error!("[Recording] Failed to allocate MP4 output context");
                return None;
            }
            // Mirror all streams from the RTMP context so stream indices match.
            // Zero out codec_tag so the MP4 muxer picks the correct container tag
            // (RTMP/FLV uses 0x07 for H.264 which is incompatible with MP4's avc1).
            let num_streams = (*ofmt_ctx).nb_streams as usize;
            for i in 0..num_streams {
                let src = *(*ofmt_ctx).streams.add(i);
                let dst = avformat_new_stream(rec_ctx, ptr::null());
                if dst.is_null() { continue; }
                avcodec_parameters_copy((*dst).codecpar, (*src).codecpar);
                (*(*dst).codecpar).codec_tag = 0;
                (*dst).time_base = (*src).time_base;
            }
            if avio_open(&mut (*rec_ctx).pb, path_c.as_ptr(), AVIO_FLAG_WRITE as i32) < 0 {
                tracing::error!("[Recording] Failed to open file: {}", rec_path);
                avformat_free_context(rec_ctx);
                return None;
            }
            if avformat_write_header(rec_ctx, ptr::null_mut()) < 0 {
                tracing::error!("[Recording] Failed to write MP4 header");
                avio_closep(&mut (*rec_ctx).pb);
                avformat_free_context(rec_ctx);
                return None;
            }
            tracing::info!("[Recording] Recording to: {}", rec_path);
            Some(FormatCtxSend(rec_ctx))
        })();
        result
    } else {
        None
    };

    tracing::info!(
        "Stream started: {}x{} (mode: {})  {}x{} @{}fps via {} at {}kbps{}",
        src_w, src_h, fit_mode, out_w, out_h, target_fps, opts.encoder, opts.bitrate_kbps,
        if opts.record_path.is_some() { " [recording]" } else { "" }
    );
    event_tx.send(StreamEvent::Started { width: out_w as u32, height: out_h as u32 }).ok();

    // ── Async RTMP write thread ───────────────────────────────────────────────
    // av_interleaved_write_frame blocks the calling thread on TCP backpressure
    // (measured at 110–1138ms per call). Moving RTMP I/O to a dedicated thread
    // lets the encode loop run at full capture rate regardless of network speed.
    //
    // Capture stream/codec metadata before transferring ofmt_ctx ownership.
    let out_stream_tb  = (*out_stream).time_base;
    let out_stream_idx = (*out_stream).index;
    let codec_tb       = (*codec_ctx).time_base;

    // 300 packets ≈ 5 s of headroom at 60 fps. The encode loop skips encoding when
    // the queue is >80% full so we never burn CPU on sws_scale for frames that would
    // just be dropped.
    const QUEUE_CAP: i32 = 300;
    const QUEUE_SKIP_THRESHOLD: i32 = 240; // 80%
    let (pkt_tx, pkt_rx) = std::sync::mpsc::sync_channel::<OwnedPacket>(QUEUE_CAP as usize);
    let fmt_send = FormatCtxSend(ofmt_ctx);
    // ofmt_ctx is now logically owned by rtmp_writer; do not touch it in this thread.

    // Approximate queue occupancy shared between encode and RTMP threads.
    let queue_depth      = Arc::new(AtomicI32::new(0));
    let queue_depth_rtmp = Arc::clone(&queue_depth);

    // rtmp_writer's own av_write_frame timing, surfaced in the encode loop's periodic status log
    // below (rather than only at thread exit) so a future "sending faster than realtime" report
    // has real write-latency evidence to diagnose from immediately.
    let rtmp_slow_writes      = Arc::new(AtomicI64::new(0));
    let rtmp_slow_writes_rtmp = Arc::clone(&rtmp_slow_writes);
    let rtmp_total_pkts       = Arc::new(AtomicI64::new(0));
    let rtmp_total_pkts_rtmp  = Arc::clone(&rtmp_total_pkts);

    // When the user stops the stream, we set this flag so the RTMP thread exits
    // immediately rather than draining the entire queue (which at 300ms/write
    // would take up to 90 seconds for a full 300-packet queue).
    let rtmp_stop      = Arc::new(std::sync::atomic::AtomicBool::new(false));
    let rtmp_stop_rtmp = Arc::clone(&rtmp_stop);

    // ── Async recording write thread (separate from RTMP) ─────────────────────
    // Previously the recording's av_write_frame ran inline on the RTMP writer thread, BEFORE
    // the RTMP write itself — any latency there (disk contention, AV scanner, a slower/network
    // drive) delayed every single RTMP packet by exactly that amount and was never measured by
    // the `slow_writes`/ms tracking below (that only ever timed the RTMP write). Under sustained
    // disk contention this starves the RTMP queue (queue_depth climbs, the encode loop starts
    // skipping frames via frames_skipped_backpressure) and looks exactly like a bitrate/
    // buffering problem on the stream itself, even though the network path was never the
    // bottleneck. Splitting onto its own thread/channel means a slow recording write can only
    // ever cost the recording (a dropped frame there, still on its own steady cadence for
    // everything that does make it through) and can no longer touch RTMP delivery at all.
    let (rec_pkt_tx, recording_writer) = if let Some(fmt_send) = rec_fmt_send {
        let (tx, rx) = std::sync::mpsc::sync_channel::<OwnedPacket>(QUEUE_CAP as usize);
        let handle = std::thread::Builder::new()
            .name("recording-writer".into())
            .spawn(move || {
                let rctx = fmt_send.into_raw();
                let mut total: u64 = 0;
                while let Ok(owned) = rx.recv() {
                    unsafe {
                        if av_interleaved_write_frame(rctx, owned.0) < 0 {
                            tracing::warn!("[Recording] av_interleaved_write_frame failed");
                        }
                    }
                    total += 1;
                    // owned drops here  av_packet_free called
                }
                unsafe {
                    av_write_trailer(rctx);
                    avio_closep(&mut (*rctx).pb);
                    avformat_free_context(rctx);
                }
                tracing::info!("[Recording] File closed ({total} pkts written)");
            })
            .expect("failed to spawn recording-writer thread");
        (Some(tx), Some(handle))
    } else {
        (None, None)
    };

    let rtmp_writer = std::thread::Builder::new()
        .name("rtmp-writer".into())
        .spawn(move || {
            // Elevated priority, matching the encode loop's own THREAD_PRIORITY_HIGHEST further
            // down — this thread is the other half of the same critical path (encoding a frame
            // that never actually reaches the network is no better than not encoding it at all).
            // Previously ran at plain default priority, same as every other thread this process
            // spawns (compositor, Spout publisher, recording writer) — under sustained CPU
            // contention the OS scheduler favors the highest-priority thread, so this one could
            // get starved of the CPU time its own av_write_frame calls and the OS's underlying
            // network-stack processing need to keep up, even though av_write_frame itself still
            // returns quickly (it's mostly just copying into the OS's own socket send buffer;
            // actual transmission happens asynchronously and isn't visible to that timing at all).
            unsafe { let _ = SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_ABOVE_NORMAL); }
            // into_raw() is a method call, so Rust captures FormatCtxSend (Send)
            // rather than the inner field *mut AVFormatContext (not Send).
            let ofmt_ctx = fmt_send.into_raw();
            let mut total_pkts: u64 = 0;
            let mut slow_writes: u64 = 0;

            while let Ok(owned) = pkt_rx.recv() {
                if rtmp_stop_rtmp.load(Ordering::Relaxed) {
                    // Forced stop: drop this and all remaining queued packets without writing.
                    // pkt_rx goes out of scope when the closure returns, draining the rest.
                    break;
                }
                queue_depth_rtmp.fetch_sub(1, Ordering::Relaxed);

                // Clone the packet and hand it to the recording thread — non-blocking, so a
                // recording write that's falling behind only ever drops a frame from the
                // recording, never delays the RTMP write below.
                if let Some(ref tx) = rec_pkt_tx {
                    unsafe {
                        let rec_pkt = av_packet_clone(owned.0);
                        if !rec_pkt.is_null() {
                            let _ = tx.try_send(OwnedPacket(rec_pkt));
                            // On Err (queue full), the returned OwnedPacket drops right here,
                            // freeing it — same as a normal successful send further downstream.
                        }
                    }
                }

                // NOTE: three consecutive attempts at real-time-pacing this write (absolute-PTS
                // based, then a combined leaky bucket, then a per-stream leaky bucket with
                // spin_sleep) each made streaming measurably worse — queue_depth climbing from the
                // very first status tick rather than staying low, well beyond what any of those
                // designs should have caused on paper. Reverted entirely rather than iterate
                // further blind; something about pacing this specific write loop isn't behaving
                // as the isolated logic suggested, and this thread draining pkt_rx as fast as
                // physically possible (queue_depth staying low/near-zero) was the proven-healthy
                // behavior in every earlier test this session. slow_writes/total_pkts below are
                // now surfaced in the periodic status log (see rtmp_slow_writes/rtmp_total_pkts)
                // so a future real recurrence of "sending faster than realtime" has actual write-
                // timing evidence to diagnose from, instead of guessing at a fix again.
                let t = Instant::now();
                unsafe {
                    if av_interleaved_write_frame(ofmt_ctx, owned.0) < 0 {
                        tracing::error!("av_interleaved_write_frame error (pkt #{}) - aborting RTMP writer", total_pkts);
                        break;
                    }
                }
                let ms = t.elapsed().as_millis();
                total_pkts += 1;
                if ms > 50 {
                    slow_writes += 1;
                    rtmp_slow_writes_rtmp.fetch_add(1, Ordering::Relaxed);
                }
                rtmp_total_pkts_rtmp.fetch_add(1, Ordering::Relaxed);
                // owned drops here  av_packet_free called
            }

            tracing::info!("RTMP writer done: {total_pkts} pkts written, {slow_writes} slow (>50ms)");
            unsafe {
                av_write_trailer(ofmt_ctx);
                avio_closep(&mut (*ofmt_ctx).pb);
                avformat_free_context(ofmt_ctx);
            }
            // rec_pkt_tx (if any) drops here — disconnects the recording-writer thread's
            // channel so it drains whatever's left, writes the trailer, and closes the file.
        })
        .expect("failed to spawn rtmp-writer thread");

    // ── Scaling context (BGRA  YUV420P) ─────────────────────────────────────
    let sws = sws_getContext(
        crop_w, crop_h, AVPixelFormat::AV_PIX_FMT_BGRA,
        dst_w, dst_h, AVPixelFormat::AV_PIX_FMT_YUV420P,
        SwsFlags::SWS_FAST_BILINEAR as i32,
        ptr::null_mut(), ptr::null_mut(), ptr::null(),
    );
    if sws.is_null() {
        return Err(anyhow!("sws_getContext failed"));
    }

    // ── Allocate reusable YUV frame and packet ────────────────────────────────
    let yuv_frame = av_frame_alloc();
    (*yuv_frame).format = AVPixelFormat::AV_PIX_FMT_YUV420P as i32;
    (*yuv_frame).width  = out_w;
    (*yuv_frame).height = out_h;
    av_frame_get_buffer(yuv_frame, 0);

    // Initialize frame to black so that letterboxing/pillarboxing ("contain" mode) 
    // has clean black edges outside the sws_scale target area.
    std::ptr::write_bytes((*yuv_frame).data[0], 16, ((*yuv_frame).linesize[0] * out_h) as usize);
    std::ptr::write_bytes((*yuv_frame).data[1], 128, ((*yuv_frame).linesize[1] * out_h / 2) as usize);
    std::ptr::write_bytes((*yuv_frame).data[2], 128, ((*yuv_frame).linesize[2] * out_h / 2) as usize);

    let pkt = av_packet_alloc();

    if let Some(ref mut enc) = audio_enc {
        enc.clear_backlog();
    }

    // ── Encode loop ───────────────────────────────────────────────────────────
    let stream_start = Instant::now();
    let mut last_pts: i64 = -1;
    let mut frames_sent:   u64 = 0;  // packets successfully queued to RTMP thread
    let mut calls_this_interval: u64 = 0;  // actual encode_frame invocations
    let mut frames_this_interval: u64 = 0; // successful sends (for fps + bitrate)
    let mut total_frames: u64 = 0;
    let mut bytes_since_status: u64 = 0;
    let mut last_status = Instant::now();
    let mut sws_ms_acc:   f64 = 0.0;
    let mut nvenc_ms_acc: f64 = 0.0;
    let mut recv_ms_acc:  f64 = 0.0;
    let mut pkts_dropped:  u64 = 0; // dropped inside encode_frame (queue full at send time)
    // Split into two distinct counters (previously merged into one `frames_skipped`) so a
    // "recording looks sped up, static periods missing" report can be diagnosed from the status
    // log alone: a PTS range skipped via frames_skipped_catchup is a genuine gap in the encoded
    // timeline (this loop fell behind real wall-clock time and gave up on those frames entirely,
    // never encoding anything for that PTS range) — very different from
    // frames_skipped_backpressure, where the loop stayed on schedule but chose not to encode
    // because the RTMP/recording write queue was backed up (also a real gap, but points at
    // downstream I/O being the bottleneck rather than the pacing loop itself).
    let mut frames_skipped_catchup: u64 = 0;
    let mut frames_skipped_backpressure: u64 = 0;

    // Encode the first frame.
    calls_this_interval += 1;
    let mut first_pts = 0i64;
    let mut pip_scalers: HashMap<String, PipScaler> = HashMap::new();
    encode_frame(codec_ctx, sws, yuv_frame, pkt,
                 crop_h, crop_x, crop_y, dst_h, dst_x, dst_y, &first, &mut frames_this_interval, &mut bytes_since_status,
                 &mut first_pts, &mut sws_ms_acc, &mut nvenc_ms_acc, &mut recv_ms_acc,
                 &pkt_tx, codec_tb, out_stream_tb, out_stream_idx,
                 &mut pkts_dropped, &queue_depth)?;
    last_pts = 0;
    total_frames += 1;

    // ── Jitter buffer: ring of recent WGC/composited frames ───────────────────
    // Each slot stores a (frame, arrival_instant) pair. At each encode tick,
    // we select the slot whose arrival time is closest to the slot's ideal
    // wall-clock time (stream_start + current_pts/fps). This smooths burst-and-
    // gap delivery from GPU-bound games without depending on WGC timestamps.
    //
    // Was 3 slots, drained by "dump everything currently buffered into the ring
    // since the last tick" (below) — fine for steady-state WGC delivery, but a
    // scene transition drives the compositor at a 16ms tick (~62/sec, see
    // compositor.rs's transition_interval) rather than WGC's own pace, so any
    // encode tick slower than ~3×16ms (this loop paces itself to target_fps,
    // and can lag further under the extra per-frame blend_transition cost) let
    // more than 3 fresh frames pile up between drains — the ring silently
    // overwrote the older ones before the closest-arrival selection ever saw
    // them, permanently discarding chunks of the transition's intermediate
    // frames rather than just picking a slightly-off one. Same fix shape as
    // the frame_tx broadcast capacity bump in main.rs: give it much more
    // headroom (~256ms at a 16ms cadence) so a transition's burst can't
    // outrun it. Memory cost is negligible either way — each slot is just an
    // Arc pointer + an Instant, not a pixel copy.
    const JITTER_SLOTS: usize = 16;
    let first_arrival = Instant::now();
    let mut jitter: [(Arc<RawFrame>, Instant); JITTER_SLOTS] =
        std::array::from_fn(|_| (first.clone(), first_arrival));
    let mut jitter_head: usize = 0; // next slot to overwrite (ring)

    let mut sleeper = spin_sleep::SpinSleeper::default();

    let mut forced_stop = false;
    loop {
        if stop_rx.try_recv().is_ok() { forced_stop = true; break; }

        if let Some(ref mut enc) = audio_enc {
            let mut send_audio = |pkt: *mut AVPacket| -> Result<()> {
                queue_depth.fetch_add(1, Ordering::Relaxed);
                bytes_since_status += unsafe { (*pkt).size } as u64;
                if pkt_tx.try_send(OwnedPacket(pkt)).is_err() {
                    pkts_dropped += 1;
                    queue_depth.fetch_sub(1, Ordering::Relaxed);
                }
                Ok(())
            };
            if let Err(e) = enc.poll(stream_start.elapsed().as_secs_f64(), &mut send_audio) {
                tracing::warn!("Audio encode error: {}", e);
            }
        }

        let elapsed_secs = stream_start.elapsed().as_secs_f64();
        let ideal_pts = (elapsed_secs * target_fps as f64).round() as i64;
        
        let mut current_pts = last_pts + 1;
        if ideal_pts > current_pts + 1 {
            // We fell behind real-time. Drop exactly ONE frame to smoothly pace catch-up
            // without chunked stutters (e.g., effectively outputs 30 FPS instead of 60 FPS
            // uniformly, rather than dropping 3 frames at once and causing a huge visual jerk).
            frames_skipped_catchup += 1;
            last_pts = current_pts;
            continue; // Physically skip encoding this frame to save CPU
        }

        let target_time_secs = current_pts as f64 / target_fps as f64;
        let target_duration = Duration::from_secs_f64(target_time_secs);
        let elapsed = stream_start.elapsed();

        if target_duration > elapsed {
            sleeper.sleep(target_duration - elapsed);
        }

        // Drain all pending WGC frames into the jitter buffer with arrival timestamp.
        while let Ok(f) = frame_rx.try_recv() {
            jitter[jitter_head] = (f, Instant::now());
            jitter_head = (jitter_head + 1) % JITTER_SLOTS;
        }

        // Select the slot whose arrival time is closest to this slot's ideal wall time.
        // This picks the frame that best represents what was happening at current_pts/fps
        // seconds since stream start, smoothing over bursty WGC delivery.
        let ideal_elapsed = Duration::from_secs_f64(current_pts as f64 / target_fps as f64);
        let ideal_instant = stream_start + ideal_elapsed;
        let raw = jitter
            .iter()
            .min_by_key(|(_, arrival)| {
                // Saturating distance to avoid u128 overflow on early frames
                if *arrival >= ideal_instant {
                    arrival.duration_since(ideal_instant).as_nanos()
                } else {
                    ideal_instant.duration_since(*arrival).as_nanos()
                }
            })
            .map(|(f, _)| f.clone())
            .unwrap_or_else(|| jitter[0].0.clone());

        let enc_pts = current_pts;
        last_pts = current_pts;

        // Skip encoding entirely when the RTMP queue is >=80% full.
        if queue_depth.load(Ordering::Relaxed) >= QUEUE_SKIP_THRESHOLD {
            frames_skipped_backpressure += 1;
        } else {
            calls_this_interval += 1;
            let mut enc_pts_arg = enc_pts;
            encode_frame(codec_ctx, sws, yuv_frame, pkt,
                         crop_h, crop_x, crop_y, dst_h, dst_x, dst_y, &raw, &mut frames_this_interval,
                         &mut bytes_since_status, &mut enc_pts_arg,
                         &mut sws_ms_acc, &mut nvenc_ms_acc, &mut recv_ms_acc,
                         &pkt_tx, codec_tb, out_stream_tb, out_stream_idx,
                         &mut pkts_dropped, &queue_depth)?;
            total_frames += 1;
        }

        // Emit status roughly once per second.
        let elapsed = last_status.elapsed();
        if elapsed >= Duration::from_secs(1) {
            let bitrate_kbps =
                ((bytes_since_status * 8) / elapsed.as_millis().max(1) as u64) as u32;
            let fps = frames_this_interval as f32 / elapsed.as_secs_f32();
            let n = calls_this_interval.max(1) as f64;
            let qd = queue_depth.load(Ordering::Relaxed);
            let rtmp_slow = rtmp_slow_writes.load(Ordering::Relaxed);
            let rtmp_total = rtmp_total_pkts.load(Ordering::Relaxed);
            // warn! (not debug!) so this is visible in the C# Core Diagnostics panel by default
            // — diagnosing a "recording looks sped up / static periods missing" report needs to
            // see fps/skipped over time, and debug! is invisible without --verbose. If fps stays
            // near target_fps with skipped~0 even through a static period, the encode loop itself
            // is fine and the bug is downstream (MP4 muxing); if fps drops and skipped climbs,
            // this loop is genuinely falling behind and dropping PTS ranges. rtmp_slow/rtmp_total
            // are av_write_frame's OWN timing (rtmp_writer thread) — previously only logged once,
            // at thread exit; surfaced here every tick so a "sending faster than realtime" report
            // has direct evidence of whether the actual network write is the bottleneck.
            tracing::warn!(
                "[diag] Stream status: {fps:.1}fps (target {target_fps}) {bitrate_kbps}kbps | \
                 sws={:.1}ms send={:.1}ms recv={:.1}ms | \
                 queue={}/{QUEUE_CAP} skipped_catchup={frames_skipped_catchup} \
                 skipped_backpressure={frames_skipped_backpressure} dropped={pkts_dropped} | \
                 rtmp_writes={rtmp_total} rtmp_slow(>50ms)={rtmp_slow}",
                sws_ms_acc / n, nvenc_ms_acc / n, recv_ms_acc / n, qd,
            );
            event_tx.send(StreamEvent::Status {
                frame: total_frames,
                fps,
                bitrate_kbps,
            }).ok();
            last_status = Instant::now();
            bytes_since_status = 0;
            frames_this_interval = 0;
            calls_this_interval  = 0;
            sws_ms_acc    = 0.0;
            nvenc_ms_acc  = 0.0;
            recv_ms_acc   = 0.0;
            pkts_dropped  = 0;
            frames_skipped_catchup = 0;
            frames_skipped_backpressure = 0;
        }
    }

    if forced_stop {
        // User stopped the stream: signal RTMP thread to exit immediately.
        // Remaining queued packets are dropped by the RTMP thread and when
        // pkt_rx goes out of scope. Avoids a 30-90 second drain at 300ms/write.
        rtmp_stop.store(true, Ordering::Relaxed);
    } else {
        // Normal end (capture disconnected): flush remaining NVENC frames.
        avcodec_send_frame(codec_ctx, ptr::null_mut());
        loop {
            let ret = avcodec_receive_packet(codec_ctx, pkt);
            if ret == AVERROR(EAGAIN) || ret == AVERROR_EOF { break; }
            if ret < 0 { break; }
            let new_pkt = av_packet_alloc();
            av_packet_move_ref(new_pkt, pkt);
            av_packet_rescale_ts(new_pkt, codec_tb, out_stream_tb);
            (*new_pkt).stream_index = out_stream_idx;
            // try_send: if queue is full at flush time, drop the packet rather
            // than blocking the encode thread indefinitely at shutdown.
            let _ = pkt_tx.try_send(OwnedPacket(new_pkt));
        }
    }

    // Signal RTMP thread to drain and exit.
    drop(pkt_tx);

    // ── Cleanup encode resources (ofmt_ctx is owned by rtmp_writer) ──────────
    av_frame_free(&mut (yuv_frame as *mut _));
    av_packet_free(&mut (pkt as *mut _));
    sws_freeContext(sws);
    avcodec_free_context(&mut (codec_ctx as *mut _));

    // Wait for RTMP thread to write remaining packets, trailer, and close.
    let _ = rtmp_writer.join();

    // Wait for the recording thread (if any) to drain, write its trailer, and close the file —
    // rtmp_writer dropping rec_pkt_tx above is what lets its channel disconnect and exit.
    if let Some(rw) = recording_writer {
        let _ = rw.join();
    }

    tracing::info!("Stream encoder exited cleanly");
    Ok(())
}

// Overlays are handled by the compositor now.

unsafe fn encode_frame(
    codec_ctx: *mut AVCodecContext,
    sws: *mut SwsContext,
    yuv_frame: *mut AVFrame,
    pkt: *mut AVPacket,
    crop_h: i32,
    crop_x: i32,
    crop_y: i32,
    _dst_h: i32,
    dst_x: i32,
    dst_y: i32,
    raw: &Arc<RawFrame>,
    frame_count: &mut u64,
    bytes_since_status: &mut u64,
    pts: &mut i64,
    sws_ms_acc: &mut f64,
    nvenc_ms_acc: &mut f64,
    recv_ms_acc: &mut f64,
    pkt_tx: &std::sync::mpsc::SyncSender<OwnedPacket>,
    codec_tb: AVRational,
    stream_tb: AVRational,
    stream_index: i32,
    pkts_dropped: &mut u64,
    queue_depth: &AtomicI32,
) -> Result<()> {
    let t0 = Instant::now();

    let start_offset = (crop_y * raw.width as i32 + crop_x) * 4;
    let src_data: [*const u8; 4] = [
        raw.pixels.as_ptr().add(start_offset as usize),
        ptr::null(), ptr::null(), ptr::null()
    ];
    let src_stride: [i32; 4] = [raw.width as i32 * 4, 0, 0, 0];

    let dst_offset_y = dst_y * (*yuv_frame).linesize[0] + dst_x;
    let dst_offset_u = (dst_y / 2) * (*yuv_frame).linesize[1] + (dst_x / 2);
    let dst_offset_v = (dst_y / 2) * (*yuv_frame).linesize[2] + (dst_x / 2);

    let mut dst_data: [*mut u8; 4] = [
        (*yuv_frame).data[0].add(dst_offset_y as usize),
        (*yuv_frame).data[1].add(dst_offset_u as usize),
        (*yuv_frame).data[2].add(dst_offset_v as usize),
        ptr::null_mut()
    ];

    sws_scale(
        sws,
        src_data.as_ptr(),
        src_stride.as_ptr(),
        0, crop_h,
        dst_data.as_mut_ptr(),
        (*yuv_frame).linesize.as_ptr(),
    );

    let t1 = Instant::now();

    (*yuv_frame).pts = *pts;
    *pts += 1;

    check(avcodec_send_frame(codec_ctx, yuv_frame), "avcodec_send_frame")?;

    let t2 = Instant::now();

    let mut recv_ms: f64 = 0.0;

    loop {
        let ta = Instant::now();
        let ret = avcodec_receive_packet(codec_ctx, pkt);
        recv_ms += ta.elapsed().as_secs_f64() * 1000.0;

        if ret == AVERROR(EAGAIN) || ret == AVERROR_EOF { break; }
        check(ret, "avcodec_receive_packet")?;

        let pkt_size = (*pkt).size as u64;

        // Move encoded data into a new packet for the RTMP thread.
        let new_pkt = av_packet_alloc();
        av_packet_move_ref(new_pkt, pkt);
        // pkt is now blank; avcodec_receive_packet will repopulate it next iteration.

        av_packet_rescale_ts(new_pkt, codec_tb, stream_tb);
        (*new_pkt).stream_index = stream_index;

        // Non-blocking send. If queue is full, OwnedPacket drops and frees new_pkt.
        if pkt_tx.try_send(OwnedPacket(new_pkt)).is_ok() {
            queue_depth.fetch_add(1, Ordering::Relaxed);
            *bytes_since_status += pkt_size;
            *frame_count += 1;
        } else {
            *pkts_dropped += 1;
        }
    }

    *sws_ms_acc   += (t1 - t0).as_secs_f64() * 1000.0;
    *nvenc_ms_acc += (t2 - t1).as_secs_f64() * 1000.0;
    *recv_ms_acc  += recv_ms;

    Ok(())
}

// ── Helpers ───────────────────────────────────────────────────────────────────

fn check(ret: i32, op: &str) -> Result<()> {
    if ret < 0 {
        Err(anyhow!("{op} failed (ffmpeg error {ret})"))
    } else {
        Ok(())
    }
}

unsafe fn set_opt(ctx: *mut AVCodecContext, key: &str, val: &str) {
    if let (Ok(k), Ok(v)) = (CString::new(key), CString::new(val)) {
        let ret = av_opt_set((*ctx).priv_data, k.as_ptr(), v.as_ptr(), 0);
        if ret < 0 {
            tracing::warn!("av_opt_set({key}={val}) ignored ({})", ret);
        }
    }
}
