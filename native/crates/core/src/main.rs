mod buffer_pool;
mod capture;
mod capture_mf;
mod compositor;
mod d2d;
mod gl_blur;
mod process_stats;
mod sources;
mod spout;
mod static_overlay;
mod streaming;
mod video_overlay;
mod audio;
mod audio_encoder;
mod voicemeeter;
mod waveform;

use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc,
};

use anyhow::{anyhow, Context, Result};
use streamflow_ipc::{
    decode_command, encode_event, CaptureSource, Command, ErrorCode, Event, PROTOCOL_VERSION,
};
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::windows::named_pipe::ServerOptions;
use tokio::sync::broadcast;
use tracing::{error, info, warn, trace};

use capture::{RawFrame, SharedShmOverlay, ShmOverlay};
use streaming::{StreamEvent, StreamOptions, StreamSession};

// ── Startup ───────────────────────────────────────────────────────────────────

#[tokio::main]
async fn main() -> Result<()> {
    let verbose = std::env::args().any(|a| a == "--verbose");
    let default_level = if verbose { "info" } else { "warn" };

    tracing_subscriber::fmt()
        .with_target(false)
        .with_writer(std::io::stderr)
        // stderr here is always a pipe to the C# host (never an interactive terminal), which
        // both displays it verbatim (Core Diagnostics panel) and persists it to a log file
        // (CoreBridgeService) — ANSI color codes would just show up as literal escape-sequence
        // garbage in both, since nothing on the reading end interprets them.
        .with_ansi(false)
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new(default_level))
        )
        .init();

    // Safety net for the one exit route none of the explicit std::process::exit call sites (nor
    // the normal Ok(()) return) can cover: an unexpected panic anywhere in the process. Panics
    // unwind by default in this workspace (no panic = "abort"), but unwinding still never drops
    // `static`s — same reason every deliberate exit path needs its own explicit
    // voicemeeter::shutdown() call. Chained onto the default hook so the panic message itself is
    // still printed exactly as before.
    let default_panic_hook = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        voicemeeter::shutdown();
        default_panic_hook(info);
    }));

    info!(
        version = env!("CARGO_PKG_VERSION"),
        "StreamFlow Core starting"
    );

    streaming::start_config_watcher();

    let (token, pipe_name, pipe_id) =
        read_auth_from_stdin().await.context("Auth handshake failed")?;

    let data_pipe = ServerOptions::new()
        .first_pipe_instance(true)
        .create(&pipe_name)
        .with_context(|| format!("Failed to create data pipe: {pipe_name}"))?;

    info!(pipe = %pipe_name, "Data pipe bound");

    // Create the overlay shared memory section that Electron will write to.
    const SHM_SIZE: u32 = 1920 * 1080 * 4 + 12;
    let shm_name = format!("Local\\StreamFlowOverlay-{pipe_id}");
    let shm_overlay = create_shm_overlay(&shm_name, SHM_SIZE)
        .context("Failed to create overlay shared memory")?;

    // Broadcast channel for raw frames: WGC  preview pipe and/or encoder. Also carries
    // one-shot AddStaticOverlay registrations (image/text/color/chat/timer overlays) into the
    // same compositor consumer.
    //
    // Capacity used to be 4 ("if a subscriber falls behind, old frames are dropped rather than
    // backing up the WGC callback") — fine for continuous live-capture frames, where a dropped
    // frame is immediately replaced by the next one a moment later. It's NOT fine for the
    // one-shot overlay registrations sharing this same channel: those have no "next frame"
    // coming, so if the compositor's receiver hasn't drained the ring buffer before a 60fps
    // primary capture session (re)starting fills 4 slots — exactly what happens right after a
    // scene switch, when a capture restart and a fresh batch of AddStaticOverlay sends land
    // together — that overlay's frame is evicted and permanently lost, silently. Confirmed via
    // [diag] composite-call counters: a re-registered image overlay's counter simply never
    // resumed after a scene reactivation, while a Timer overlay's did (its periodic re-send
    // gives it a retry that a one-shot Image/Text/Color/Chat overlay doesn't have). Bumped to
    // give a ~530ms cushion at 60fps instead of ~66ms — comfortably absorbs that startup burst
    // without meaningfully changing memory use (each slot is an Arc<RawFrame> pointer, not a
    // pixel copy).
    let (frame_tx, _) = broadcast::channel::<Arc<RawFrame>>(32);

    // Preview gate - only set when Electron has sent EnablePreview.
    let preview_enabled = Arc::new(AtomicBool::new(false));

    // Channel for events produced by the stream encoder thread  stdout.
    let (stream_evt_tx, stream_evt_rx) =
        tokio::sync::mpsc::unbounded_channel::<StreamEvent>();
    // Channel for Spout texture-ready notifications produced by the compositor thread  stdout
    // (Event::SpoutTextureReady) — same shape as stream_evt_tx above, just a different producer
    // thread. UnboundedSender::send is a plain sync call, safe to use from the compositor's
    // std::thread (it's not itself async).
    let (spout_evt_tx, spout_evt_rx) =
        tokio::sync::mpsc::unbounded_channel::<(u32, u32, u32, i64)>();

    let compositor_cfg = Arc::new(std::sync::Mutex::new(compositor::CompositorConfig {
        sources: Vec::new(), blur_regions: Vec::new(), canvas_width: None, canvas_height: None,
        pending_transition: None, spout_enabled: false, spout_sender_name: "StreamFlow".to_string(),
    }));
    // Wakes the compositor thread to recompute the composited frame from cached inputs
    // immediately on a Config change, rather than waiting for WGC's next primary frame.
    let config_notify = Arc::new(tokio::sync::Notify::new());
    let composited_tx = compositor::start_compositor(
        frame_tx.subscribe(),
        Arc::clone(&shm_overlay),
        Arc::clone(&compositor_cfg),
        Arc::clone(&config_notify),
        spout_evt_tx,
    );

    // Data pipe is secondary: if the client disconnects, the core keeps running.
    let preview_enabled_pipe = Arc::clone(&preview_enabled);
    let pipe_frame_rx = composited_tx.subscribe();
    // A second subscription to the *pre-composite* broadcast, so PiP sources can get their own
    // live thumbnail in the editor instead of only the primary's (composited) preview.
    let pip_raw_rx = frame_tx.subscribe();
    let pipe_compositor_cfg = Arc::clone(&compositor_cfg);
    tokio::spawn(async move {
        if let Err(e) = run_data_pipe(data_pipe, token, pipe_frame_rx, pip_raw_rx, preview_enabled_pipe, pipe_compositor_cfg).await {
            warn!("Data pipe exited: {e}");
        }
    });

    // Emitted only now, immediately before the stdin command loop starts — not right after the
    // data pipe/shm are created. The client (CoreBridgeService.cs) treats seeing this event as
    // proof "core's stdin reader is up" and immediately flushes any commands queued during
    // startup (e.g. GetSources/GetAudioDevices sent from a ViewModel constructor) — emitting it
    // earlier left a real gap, since spawning the compositor thread alone creates a whole second
    // Tokio runtime (a genuinely variable-cost OS operation): those flushed commands would sit
    // unread in the stdin pipe with nothing yet polling for them, occasionally slow enough for
    // GoLiveViewModel's very first GetSources/GetAudioDevices to appear to just never get a
    // reply. Nothing between here and run_stdin_commands' loop below does anything that can
    // stall — no thread spawns, no awaits — so this closes the gap down to a single synchronous
    // function call.
    println!(
        "{}",
        serde_json::to_string(&Event::Ready {
            version: PROTOCOL_VERSION,
            pid: std::process::id(),
            pipe: pipe_name.clone(),
            shm_name: shm_name.clone(),
            shm_size: SHM_SIZE,
        })?
    );

    tokio::select! {
        result = run_stdin_commands(frame_tx.clone(), composited_tx.clone(), Arc::clone(&preview_enabled), stream_evt_tx, stream_evt_rx, spout_evt_rx, Arc::clone(&compositor_cfg), Arc::clone(&config_notify), verbose) => {
            if let Err(e) = result {
                error!("Control plane error: {e:#}");
                voicemeeter::shutdown();
                std::process::exit(1);
            }
        }
        _ = tokio::signal::ctrl_c() => {
            info!("Ctrl-C received");
        }
    }

    // Reached by Ctrl-C and by run_stdin_commands returning Ok — every OTHER way this process
    // ends goes through an explicit std::process::exit instead (see run_stdin_commands' own
    // exit points), each of which needs this same call: SESSION is a `static`, and statics are
    // never dropped even on a normal process exit, let alone process::exit's abrupt one.
    voicemeeter::shutdown();
    info!("StreamFlow Core exiting");
    Ok(())
}

// ── Auth ──────────────────────────────────────────────────────────────────────

/// Returns `(token, pipe_name, pipe_id)`.
async fn read_auth_from_stdin() -> Result<(String, String, String)> {
    let mut stdin = BufReader::new(tokio::io::stdin());
    let mut line = String::new();

    let read = tokio::time::timeout(
        std::time::Duration::from_secs(5),
        stdin.read_line(&mut line),
    )
    .await
    .map_err(|_| anyhow!("Timed out waiting for Auth on stdin"))?
    .context("Failed to read Auth line from stdin")?;

    if read == 0 {
        return Err(anyhow!("stdin closed before Auth was received"));
    }

    match decode_command(line.trim()).context("Failed to decode Auth command")? {
        Command::Auth { token, pipe_id } => {
            if token.is_empty() || pipe_id.is_empty() {
                return Err(anyhow!("Auth token or pipe_id is empty"));
            }
            let pipe_name = format!(r"\\.\pipe\streamflow-{pipe_id}");
            info!(pipe = %pipe_name, "Auth received");
            Ok((token, pipe_name, pipe_id))
        }
        other => Err(anyhow!(
            "Expected Auth as first stdin message, got: {other:?}"
        )),
    }
}

// ── Control plane (stdin  stdout) ───────────────────────────────────────────

async fn run_stdin_commands(
    frame_tx: broadcast::Sender<Arc<RawFrame>>,
    composited_tx: broadcast::Sender<Arc<RawFrame>>,
    preview_enabled: Arc<AtomicBool>,
    stream_evt_tx: tokio::sync::mpsc::UnboundedSender<StreamEvent>,
    mut stream_evt_rx: tokio::sync::mpsc::UnboundedReceiver<StreamEvent>,
    mut spout_evt_rx: tokio::sync::mpsc::UnboundedReceiver<(u32, u32, u32, i64)>,
    compositor_cfg: compositor::SharedCompositorConfig,
    config_notify: Arc<tokio::sync::Notify>,
    verbose: bool,
) -> Result<()> {
    // `.lines()` (not a bare `stdin.read_line(&mut line)` re-created fresh inside the
    // `select!` below) — `read_line` is documented as NOT cancellation-safe: if some other
    // `select!` branch (here, `stream_evt_rx.recv()`, which fires continuously once an audio
    // monitor or stream is running) completes first while a read_line future has already
    // consumed some bytes from stdin looking for a newline, those bytes are gone from the pipe
    // but never make it into the caller's buffer once the future is dropped — silently
    // corrupting whatever line comes next. `Lines` keeps its accumulation buffer across calls
    // instead of inside a per-call future, so it's cancel-safe by construction. This was the
    // actual cause of an intermittent "serialization failed: expected value at line 1 column 1"
    // error for large commands (e.g. a static overlay's base64 image) landing right after
    // audio-level/volume events started flowing.
    let mut lines = BufReader::new(tokio::io::stdin()).lines();
    let mut stdout = tokio::io::stdout();
    let mut active_sessions: std::collections::HashMap<String, Box<dyn CaptureSessionTrait>> = std::collections::HashMap::new();
    let mut active_stream: Option<StreamSession> = None;
    // Standalone per-device level monitors (Command::StartAudioMonitor), independent of
    // streaming — dropping the ActiveStream here stops that device's capture and, via peak_tx
    // disconnecting, its meter-reporting thread. Deliberately separate from `active_stream`'s
    // own audio captures: opening the same device twice (once monitored, once streamed) is
    // fine, so no dedup/ref-counting is needed between the two.
    let mut audio_monitor_sessions: std::collections::HashMap<String, crate::audio::ActiveStream> = std::collections::HashMap::new();
    let mut last_keepalive = tokio::time::Instant::now();

    // Periodic resource snapshot for the C# host's status bar (see ipc::Event::CoreStats) —
    // deliberately coarse (every 3s), nothing here is time-sensitive. The DXGI adapter is
    // resolved once and reused for every VRAM query rather than depending on whatever capture
    // sessions happen to be active — `None` if this machine/driver doesn't support the query.
    let mut stats_sampler = crate::process_stats::ProcessStatsSampler::new();
    let dxgi_adapter = crate::process_stats::create_dxgi_adapter();
    let mut stats_interval = tokio::time::interval(std::time::Duration::from_secs(3));
    stats_interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);

    loop {
        let timeout = tokio::time::sleep_until(last_keepalive + std::time::Duration::from_secs(30));

        let line = tokio::select! {
            _ = timeout => {
                info!("Watchdog timeout: no keep-alive received in 30s. Exiting.");
                voicemeeter::shutdown();
                std::process::exit(1);
            }
            _ = stats_interval.tick() => {
                let stats = stats_sampler.sample();
                let (vram_used_mb, vram_total_mb) = dxgi_adapter.as_ref()
                    .and_then(crate::process_stats::sample_vram)
                    .map_or((None, None), |(used, total)| (Some(used), Some(total)));
                let ev = encode_event(&Event::CoreStats {
                    cpu_percent: stats.cpu_percent as f32,
                    working_set_mb: stats.working_set_mb as f32,
                    vram_used_mb,
                    vram_total_mb,
                })?;
                stdout.write_all(&ev).await?;
                stdout.flush().await?;
                continue;
            }
            // ── Stream events from the encoder thread  stdout ────────────────
            Some(evt) = stream_evt_rx.recv() => {
                match evt {
                    StreamEvent::Started { width, height } => {
                        let frame = encode_event(&Event::StreamStarted { width, height })?;
                        stdout.write_all(&frame).await?;
                        stdout.flush().await?;
                    }
                    StreamEvent::Status { frame, fps, bitrate_kbps } => {
                        let ev = encode_event(&Event::StreamStatus { frame, fps, bitrate_kbps, dropped: 0 })?;
                        stdout.write_all(&ev).await?;
                        stdout.flush().await?;
                    }
                    StreamEvent::AudioLevel { peak_db } => {
                        let ev = encode_event(&Event::AudioLevel { peak_db })?;
                        stdout.write_all(&ev).await?;
                        stdout.flush().await?;
                    }
                    StreamEvent::AudioDeviceLevel { device_id, peak_db } => {
                        let ev = encode_event(&Event::AudioDeviceLevel { device_id, peak_db })?;
                        stdout.write_all(&ev).await?;
                        stdout.flush().await?;
                    }
                    StreamEvent::AudioDeviceVolume { device_id, volume, muted } => {
                        let ev = encode_event(&Event::AudioDeviceVolume { device_id, volume, muted })?;
                        stdout.write_all(&ev).await?;
                        stdout.flush().await?;
                    }
                    StreamEvent::Error(msg) => {
                        let frame = encode_event(&Event::Error {
                            code: ErrorCode::EncoderError,
                            message: msg,
                        })?;
                        stdout.write_all(&frame).await?;
                        stdout.flush().await?;
                    }
                    StreamEvent::Stopped => {
                        active_stream = None;
                        let frame = encode_event(&Event::StreamStopped)?;
                        stdout.write_all(&frame).await?;
                        stdout.flush().await?;
                    }
                }
                continue;
            }
            Some((share_handle, width, height, adapter_luid)) = spout_evt_rx.recv() => {
                let frame = encode_event(&Event::SpoutTextureReady { share_handle, width, height, adapter_luid })?;
                stdout.write_all(&frame).await?;
                stdout.flush().await?;
                continue;
            }
            // ── Commands from Electron on stdin ───────────────────────────────
            read_result = lines.next_line() => {
                match read_result {
                    Ok(None) => {
                        info!("stdin closed - Electron exited");
                        voicemeeter::shutdown();
                        std::process::exit(0);
                    }
                    Err(e) => {
                        error!("stdin read error: {e}");
                        voicemeeter::shutdown();
                        std::process::exit(1);
                    }
                    Ok(Some(l)) => l,
                }
            }
        };

        let trimmed = line.trim();
        if trimmed.is_empty() {
            continue;
        }
        match decode_command(trimmed) {
                    Ok(Command::Ping) => {
                        let frame = encode_event(&Event::Pong {
                            version: PROTOCOL_VERSION,
                        })?;
                        stdout.write_all(&frame).await?;
                        stdout.flush().await?;
                    }
                    Ok(Command::Shutdown) | Ok(Command::Exit) => {
                        info!("Exit/Shutdown command received");
                        voicemeeter::shutdown();
                        std::process::exit(0);
                    }
                    Ok(Command::Standby) => {
                        last_keepalive = tokio::time::Instant::now();
                        if verbose {
                            let frame = encode_event(&Event::Acknowledge { command: "Standby".into() })?;
                            stdout.write_all(&frame).await?;
                            stdout.flush().await?;
                        }
                    }
                    Ok(Command::Config { sources, canvas_width, canvas_height, transition }) => {
                        last_keepalive = tokio::time::Instant::now();
                        // Elevated to warn! (not info!) so this is visible in the C# Core
                        // Diagnostics panel by default — that panel only shows warn+ (the app
                        // runs without --verbose normally), and there was previously no signal
                        // at all here to confirm a scene switch's Config command actually made
                        // it to the compositor rather than being silently lost/never sent.
                        let primary = sources.iter().find(|s| s.is_primary).map(|s| s.source_id.as_str()).unwrap_or("(none)");
                        warn!(
                            "[diag] Config received: {} source(s), primary={primary}, canvas={canvas_width:?}x{canvas_height:?}, transition={}",
                            sources.len(),
                            transition.is_some(),
                        );
                        {
                            let mut cfg = compositor_cfg.lock().unwrap();
                            cfg.sources = sources;
                            cfg.canvas_width = canvas_width;
                            cfg.canvas_height = canvas_height;
                            cfg.pending_transition = transition;
                        }
                        config_notify.notify_one();
                        if verbose {
                            let frame = encode_event(&Event::Acknowledge { command: "Config".into() })?;
                            stdout.write_all(&frame).await?;
                            stdout.flush().await?;
                        }
                    }
                    Ok(Command::SetSpoutOutput { enabled, sender_name }) => {
                        last_keepalive = tokio::time::Instant::now();
                        {
                            let mut cfg = compositor_cfg.lock().unwrap();
                            cfg.spout_enabled = enabled;
                            if !sender_name.is_empty() {
                                cfg.spout_sender_name = sender_name;
                            }
                        }
                        // No accompanying Config in the common case (this is usually toggled on
                        // its own) — wake the compositor thread now instead of waiting for the
                        // next frame/config event to notice the change.
                        config_notify.notify_one();
                        if verbose {
                            let frame = encode_event(&Event::Acknowledge { command: "SetSpoutOutput".into() })?;
                            stdout.write_all(&frame).await?;
                            stdout.flush().await?;
                        }
                    }
                    Ok(Command::GetSources) => {
                        let items: Vec<CaptureSource> = sources::enumerate();
                        let frame = encode_event(&Event::Sources { items })?;
                        stdout.write_all(&frame).await?;
                        stdout.flush().await?;
                    }
                    Ok(Command::GetAudioDevices) => {
                        let items = audio::get_audio_devices().unwrap_or_else(|e| {
                            error!("Failed to enumerate audio devices: {e}");
                            Vec::new()
                        });
                        let frame = encode_event(&Event::AudioDevices { items })?;
                        stdout.write_all(&frame).await?;
                        stdout.flush().await?;
                    }
                    Ok(Command::StartCapture { source_id, overlay_hwnd }) => {
                        let mut video_resolution: Option<(u32, u32)> = None;

                        let session_result: Result<Box<dyn CaptureSessionTrait>> = if source_id.starts_with("webcam:") {
                            let sym_link = source_id.trim_start_matches("webcam:");
                            capture_mf::MFCaptureSession::new(source_id.clone(), sym_link, frame_tx.clone())
                                .map(|s| Box::new(s) as Box<dyn CaptureSessionTrait>)
                        } else if source_id.starts_with("video:") {
                            // The file path is base64-encoded into the id itself (same approach
                            // as webcam's symlink) rather than needing a separate command.
                            base64::Engine::decode(
                                &base64::engine::general_purpose::URL_SAFE_NO_PAD,
                                source_id.trim_start_matches("video:"),
                            )
                            .map_err(|e| anyhow!("Invalid video overlay id: {e}"))
                            .and_then(|bytes| String::from_utf8(bytes).map_err(|e| anyhow!("Invalid video overlay path: {e}")))
                            .and_then(|path| {
                                // Probed synchronously here (not inside the decode thread) so
                                // the resolution can ride along on the same CaptureStarted
                                // event the app already waits on to know capture is live —
                                // video files, unlike monitor/window sources, have no earlier
                                // GetSources-time opportunity to report this.
                                video_resolution = video_overlay::probe_dimensions(&path).ok();
                                video_overlay::VideoOverlaySession::new(source_id.clone(), path, frame_tx.clone())
                                    .map(|s| Box::new(s) as Box<dyn CaptureSessionTrait>)
                            })
                        } else {
                            sources::capture_item_for_id(&source_id)
                                .map_err(Into::into)
                                .and_then(|item| {
                                    let _ = overlay_hwnd; // no longer used; overlay arrives via data pipe
                                    capture::CaptureSession::new(source_id.clone(), item, frame_tx.clone())
                                        .map(|s| Box::new(s) as Box<dyn CaptureSessionTrait>)
                                })
                        };

                        match session_result {
                            Ok(session) => {
                                active_sessions.insert(source_id.clone(), session);
                                let frame = encode_event(&Event::CaptureStarted {
                                    source_id: source_id.clone(),
                                    width: video_resolution.map(|(w, _)| w),
                                    height: video_resolution.map(|(_, h)| h),
                                })?;
                                stdout.write_all(&frame).await?;
                                stdout.flush().await?;
                                info!(source = %source_id, "Capture started");
                            }
                            Err(e) => {
                                warn!("Failed to start capture for {source_id}: {e}");
                                let frame = encode_event(&Event::Error {
                                    code: ErrorCode::CaptureError,
                                    message: format!("StartCapture failed: {e}"),
                                })?;
                                stdout.write_all(&frame).await?;
                                stdout.flush().await?;
                            }
                        }
                    }
                    Ok(Command::AddStaticOverlay { source_id, width, height, pixels_base64 }) => {
                        match static_overlay::decode_as_raw_frame(&source_id, width, height, &pixels_base64) {
                            Ok(frame) => {
                                // No ongoing session needed — one broadcast is enough for the
                                // compositor to cache and keep reusing it every tick.
                                let _ = frame_tx.send(frame);
                                // Width/height omitted here: the app already knows (and applies)
                                // each static overlay's own aspect ratio client-side before ever
                                // sending this command — including deliberately NOT applying one
                                // for color fills, which have no intrinsic ratio. Reporting the
                                // raw buffer size back would fight that (a flat color's buffer is
                                // an arbitrary square, not a "real" ratio to lock to).
                                let evt = encode_event(&Event::CaptureStarted {
                                    source_id: source_id.clone(),
                                    width: None,
                                    height: None,
                                })?;
                                stdout.write_all(&evt).await?;
                                stdout.flush().await?;
                                // warn!, not info! — see the matching note on Command::Config
                                // above for why (invisible in the diag panel otherwise).
                                warn!("[diag] Static overlay registered: source_id={source_id} {width}x{height}");
                            }
                            Err(e) => {
                                warn!("Failed to register static overlay {source_id}: {e:#}");
                                let evt = encode_event(&Event::Error {
                                    code: ErrorCode::CaptureError,
                                    message: format!("AddStaticOverlay failed: {e}"),
                                })?;
                                stdout.write_all(&evt).await?;
                                stdout.flush().await?;
                            }
                        }
                    }
                    Ok(Command::StopCapture { source_id }) => {
                        if let Some(mut session) = active_sessions.remove(&source_id) {
                            if let Err(e) = session.stop() {
                                warn!("Failed to stop capture {source_id}: {e}");
                            }
                        }
                        if active_sessions.is_empty() {
                            preview_enabled.store(false, Ordering::Relaxed);
                        }
                        let frame = encode_event(&Event::CaptureStopped)?;
                        stdout.write_all(&frame).await?;
                        stdout.flush().await?;
                        info!("Capture stopped: {source_id}");
                    }
                    Ok(Command::EnablePreview) => {
                        preview_enabled.store(true, Ordering::Relaxed);
                        info!("Preview enabled");
                    }
                    Ok(Command::DisablePreview) => {
                        preview_enabled.store(false, Ordering::Relaxed);
                        info!("Preview disabled");
                    }
                    Ok(Command::StartStream {
                        rtmp_url,
                        bitrate_kbps,
                        fps,
                        output_width,
                        output_height,
                        fit_mode,
                        encoder,
                        sources,
                        audio_sources,
                        record_path,
                    }) => {
                        // A primary-less scene can still stream from a synthesized blank canvas
                        // (see compositor.rs) as long as a canvas resolution is actually known —
                        // either a primary is configured (its capture session will deliver real
                        // dimensions shortly) or one was set manually/pre-selected from a device.
                        let has_known_canvas = {
                            let cfg = compositor_cfg.lock().unwrap();
                            cfg.sources.iter().any(|s| s.is_primary)
                                || (cfg.canvas_width.is_some() && cfg.canvas_height.is_some())
                        };
                        if active_sessions.is_empty() && !has_known_canvas {
                            let frame = encode_event(&Event::Error {
                                code: ErrorCode::EncoderError,
                                message: "StartStream requires either an active capture session or a configured canvas resolution".into(),
                            })?;
                            stdout.write_all(&frame).await?;
                            stdout.flush().await?;
                        } else {
                            // Stop any previous stream first.
                            if let Some(mut s) = active_stream.take() {
                                s.stop();
                            }
                            let opts = StreamOptions {
                                rtmp_url,
                                bitrate_kbps,
                                fps,
                                output_width,
                                output_height,
                                fit_mode,
                                encoder,
                                sources,
                                audio_sources,
                                record_path,
                            };
                            active_stream = Some(StreamSession::start(
                                opts,
                                composited_tx.subscribe(),
                                stream_evt_tx.clone(),
                            ));
                            info!("Stream encoder started");
                        }
                    }
                    Ok(Command::StopStream) => {
                        if let Some(mut s) = active_stream.take() {
                            s.stop();
                            // StreamStopped event is emitted by the encoder thread.
                        }
                    }
                    Ok(Command::StartAudioMonitor { device_id }) => {
                        if !audio_monitor_sessions.contains_key(&device_id) {
                            let (peak_tx, peak_rx) = std::sync::mpsc::sync_channel::<f32>(4);
                            // Voicemeeter's virtual render endpoints don't carry real audio
                            // through WASAPI loopback (see voicemeeter.rs) — for those, read
                            // Voicemeeter's own internal meter instead. Everything else keeps
                            // using the existing WASAPI/CPAL capture path unchanged.
                            let capture_result: Result<crate::audio::ActiveStream> =
                                if crate::audio::is_voicemeeter_output_device(&device_id) {
                                    Ok(crate::audio::start_voicemeeter_monitor(&device_id, peak_tx))
                                } else {
                                    crate::audio::start_audio_capture(&device_id, Some(peak_tx))
                                        .map(|(stream, _cons, _config)| stream)
                                };
                            match capture_result {
                                Ok(stream) => {
                                    // For the WASAPI/CPAL path, the discarded ring-buffer
                                    // consumer is intentional — monitoring only needs the
                                    // peak_tx side channel, not the samples themselves. The
                                    // ring buffer fills and simply stops being drained, which
                                    // is fine since nothing reads it and it's bounded.
                                    let bridge_evt_tx = stream_evt_tx.clone();
                                    let bridge_device_id = device_id.clone();
                                    std::thread::Builder::new()
                                        .name("audio-monitor-bridge".into())
                                        .spawn(move || {
                                            // Report initial device volume/mute immediately so the
                                            // UI doesn't wait for the first poll tick below.
                                            let mut last_volume: Option<(f32, bool)> = None;
                                            if let Ok((volume, muted)) = crate::audio::get_device_volume(&bridge_device_id) {
                                                last_volume = Some((volume, muted));
                                                let _ = bridge_evt_tx.send(StreamEvent::AudioDeviceVolume {
                                                    device_id: bridge_device_id.clone(),
                                                    volume, muted,
                                                });
                                            }

                                            // Volume/mute change slowly (user- or externally-driven,
                                            // e.g. Windows Volume Mixer or physical volume keys)
                                            // compared to the peak meter — polling every 5th tick
                                            // (~500ms, given the 100ms recv_timeout below) is plenty
                                            // responsive without spamming redundant COM calls, and
                                            // only emits again when the value actually changed.
                                            const VOLUME_POLL_EVERY: u32 = 5;
                                            let mut tick: u32 = 0;
                                            loop {
                                                match peak_rx.recv_timeout(std::time::Duration::from_millis(100)) {
                                                    Ok(peak) => {
                                                        // -96 dB floor instead of -infinity: serde_json
                                                        // encodes non-finite floats as `null`, which fails
                                                        // to deserialize into C#'s non-nullable float field.
                                                        let db = if peak > 0.0 { (20.0 * peak.log10()).max(-96.0) } else { -96.0 };
                                                        let _ = bridge_evt_tx.send(StreamEvent::AudioDeviceLevel {
                                                            device_id: bridge_device_id.clone(),
                                                            peak_db: db,
                                                        });
                                                    }
                                                    Err(std::sync::mpsc::RecvTimeoutError::Timeout) => {}
                                                    Err(std::sync::mpsc::RecvTimeoutError::Disconnected) => break,
                                                }

                                                tick = tick.wrapping_add(1);
                                                if tick % VOLUME_POLL_EVERY == 0 {
                                                    if let Ok((volume, muted)) = crate::audio::get_device_volume(&bridge_device_id) {
                                                        if last_volume != Some((volume, muted)) {
                                                            last_volume = Some((volume, muted));
                                                            let _ = bridge_evt_tx.send(StreamEvent::AudioDeviceVolume {
                                                                device_id: bridge_device_id.clone(),
                                                                volume, muted,
                                                            });
                                                        }
                                                    }
                                                }
                                            }
                                        })
                                        .ok();
                                    tracing::warn!("Audio monitor started: device='{device_id}'");
                                    audio_monitor_sessions.insert(device_id, stream);
                                }
                                Err(e) => {
                                    warn!("Failed to start audio monitor for {device_id}: {e}");
                                    let frame = encode_event(&Event::Error {
                                        code: ErrorCode::CaptureError,
                                        message: format!("StartAudioMonitor failed: {e}"),
                                    })?;
                                    stdout.write_all(&frame).await?;
                                    stdout.flush().await?;
                                }
                            }
                        }
                    }
                    Ok(Command::StopAudioMonitor { device_id }) => {
                        // Dropping the ActiveStream stops capture; the bridge thread's peak_rx
                        // then disconnects and it exits on its own.
                        if audio_monitor_sessions.remove(&device_id).is_some() {
                            tracing::warn!("Audio monitor stopped: device='{device_id}'");
                        }
                    }
                    Ok(Command::SetDeviceVolume { device_id, volume }) => {
                        if let Err(e) = crate::audio::set_device_volume(&device_id, volume) {
                            warn!("SetDeviceVolume failed for {device_id}: {e}");
                        }
                    }
                    Ok(Command::SetDeviceMute { device_id, muted }) => {
                        if let Err(e) = crate::audio::set_device_mute(&device_id, muted) {
                            warn!("SetDeviceMute failed for {device_id}: {e}");
                        }
                    }
                    Ok(Command::SetAudioMix { device_id, gain, muted, solo }) => {
                        if let Some(session) = &active_stream {
                            session.set_audio_mix(&device_id, gain, muted, solo);
                        }
                    }
                    Ok(Command::SetBlurRegions { regions }) => {
                        compositor_cfg.lock().unwrap().blur_regions = regions;
                    }
                    Ok(Command::GetWaveformPeaks { path, pixels_per_second }) => {
                        info!("Computing waveform peaks for {} at {} pps", path, pixels_per_second);
                        tokio::task::spawn_blocking(move || {
                            let peaks = match crate::waveform::compute_peaks(&path, pixels_per_second) {
                                Ok(p) => p,
                                Err(e) => {
                                    tracing::error!("Failed to compute waveform: {}", e);
                                    Vec::new()
                                }
                            };
                            let frame = encode_event(&Event::WaveformPeaks {
                                path,
                                peaks,
                            }).unwrap();
                            tokio::spawn(async move {
                                let mut out = tokio::io::stdout();
                                let _ = out.write_all(&frame).await;
                                let _ = out.flush().await;
                            });
                        });
                    }
                    Ok(other) => {
                        warn!("Unexpected command on control plane: {other:?}");
                        let frame = encode_event(&Event::Error {
                            code: ErrorCode::IpcError,
                            message: format!("unexpected command: {other:?}"),
                        })?;
                        stdout.write_all(&frame).await?;
                        stdout.flush().await?;
                    }
                    Err(e) => {
                        warn!("Unrecognised command: {e}");
                        let frame = encode_event(&Event::Error {
                            code: ErrorCode::IpcError,
                            message: format!("unknown command: {e}"),
                        })?;
                        stdout.write_all(&frame).await?;
                        stdout.flush().await?;
                    }
                }
    }

    if let Some(mut s) = active_stream.take() {
        s.stop();
    }
    for (_, mut session) in active_sessions {
        let _ = session.stop();
    }

    Ok(())
}

// ── Data pipe (preview-only frame transport) ──────────────────────────────────

/// Downsamples a raw BGRA frame by `scale` (nearest-neighbor decimation) and writes it to the
/// data pipe as a type-1 VideoPreview frame, tagged with its own source_id. Shared by both the
/// primary's composited preview and PiP sources' own raw thumbnails — the client already
/// distinguishes them by source_id.
///
/// Pixels are sent in the same BGRA order they arrive in — they used to get reordered to RGBA
/// here and then immediately swapped back to BGRA on the C# side (to match WPF's Bgra32 pixel
/// format), which was a pure round-trip: the data starts and ends BGRA, so the RGBA detour never
/// did anything but cost two wasted passes over every frame.
async fn write_scaled_frame<W: tokio::io::AsyncWrite + Unpin>(
    writer: &mut W,
    raw: &RawFrame,
    scale: u32,
) -> Result<()> {
    // Use ceiling division so scaled_w/h match the actual pixel count the step_by loop emits
    // (floor would cause a row-stride mismatch → slant).
    let scaled_w = ((raw.width + scale - 1) / scale).max(1);
    let scaled_h = ((raw.height + scale - 1) / scale).max(1);
    let mut scaled_pixels = crate::buffer_pool::acquire_empty((scaled_w * scaled_h * 4) as usize);

    for y in (0..raw.height).step_by(scale as usize) {
        for x in (0..raw.width).step_by(scale as usize) {
            let i = ((y * raw.width + x) * 4) as usize;
            if i + 3 < raw.pixels.len() {
                scaled_pixels.extend_from_slice(&raw.pixels[i..i + 4]);
            }
        }
    }

    // Serialize: FrameHeader(8) + u8 source_id_len + [source_id_bytes] + u32 width + u32 height + BGRA pixels.
    let source_id_bytes = raw.source_id.as_bytes();
    let source_id_len = source_id_bytes.len() as u8;
    let payload_len = 1 + source_id_len as u32 + 8 + scaled_pixels.len() as u32;
    let mut frame_bytes = crate::buffer_pool::acquire_empty(8 + payload_len as usize);
    frame_bytes.extend_from_slice(&streamflow_ipc::encode_frame_header(
        &streamflow_ipc::FrameHeader {
            frame_type: streamflow_ipc::FrameType::VideoPreview,
            payload_len,
        },
    ));
    frame_bytes.push(source_id_len);
    frame_bytes.extend_from_slice(source_id_bytes);
    frame_bytes.extend_from_slice(&scaled_w.to_le_bytes());
    frame_bytes.extend_from_slice(&scaled_h.to_le_bytes());
    frame_bytes.extend_from_slice(&scaled_pixels);

    let result = writer.write_all(&frame_bytes).await;
    crate::buffer_pool::release(scaled_pixels);
    crate::buffer_pool::release(frame_bytes);
    result?;
    Ok(())
}

/// Accept one connection, verify Hello, then forward type-1 preview frames to Electron while
/// `preview_enabled` is set: the fully composited output (`composited_rx`), plus each currently-
/// placed source's own raw frame (`raw_rx`, filtered to whatever's in the current Config — primary
/// included, since it's just another positioned layer now) so every box can show its own live
/// thumbnail instead of a static placeholder. Overlay pixels arrive via shared memory (no type-2
/// frames on the pipe).
async fn run_data_pipe(
    server: tokio::net::windows::named_pipe::NamedPipeServer,
    expected_token: String,
    mut composited_rx: broadcast::Receiver<Arc<RawFrame>>,
    mut raw_rx: broadcast::Receiver<Arc<RawFrame>>,
    preview_enabled: Arc<AtomicBool>,
    compositor_cfg: compositor::SharedCompositorConfig,
) -> Result<()> {
    server.connect().await.context("Data pipe accept failed")?;
    info!("Data pipe client connected");

    let (reader, mut writer) = tokio::io::split(server);
    let mut buf_reader = BufReader::new(reader);

    // Read the Hello authentication line.
    let mut first_line = String::new();
    let bytes_read = tokio::time::timeout(
        std::time::Duration::from_secs(5),
        buf_reader.read_line(&mut first_line),
    )
    .await
    .map_err(|_| anyhow!("Timed out waiting for Hello on data pipe"))?
    .context("Data pipe read error")?;

    if bytes_read == 0 {
        return Err(anyhow!("Data pipe closed before Hello was received"));
    }

    match decode_command(first_line.trim()) {
        Ok(Command::Hello { token }) => {
            if !tokens_equal(token.as_bytes(), expected_token.as_bytes()) {
                return Err(anyhow!("Data pipe Hello token mismatch - closing"));
            }
            info!("Data pipe authenticated");
        }
        Ok(other) => {
            return Err(anyhow!(
                "Expected Hello as first data pipe message, got: {other:?}"
            ));
        }
        Err(e) => {
            return Err(anyhow!("Failed to decode Hello: {e}"));
        }
    }

    // Drain any data Electron sends (shouldn't be any now that overlay uses SHM).
    drop(buf_reader);

    info!("Data pipe ready -> waiting for preview to be enabled");

    let mut last_preview_time = tokio::time::Instant::now();
    let preview_interval = std::time::Duration::from_millis(66); // ~15 FPS (primary)

    let mut last_pip_times: std::collections::HashMap<String, tokio::time::Instant> =
        std::collections::HashMap::new();
    let pip_interval = std::time::Duration::from_millis(125); // ~8 FPS (PiP thumbnails)

    loop {
        tokio::select! {
            result = composited_rx.recv() => {
                let raw = match result {
                    Ok(f) => f,
                    // Lagged: broadcast dropped old frames — just skip and keep going.
                    Err(broadcast::error::RecvError::Lagged(n)) => {
                        trace!("Preview pipe lagged, dropped {n} frames");
                        continue;
                    }
                    Err(broadcast::error::RecvError::Closed) => break,
                };

                if !preview_enabled.load(Ordering::Relaxed) {
                    // Capture is running (e.g. streaming-only) but preview is off.
                    // Frame consumed to prevent channel back-pressure; not written to pipe.
                    continue;
                }
                // "Show Preview" (the same checkbox that drives the public Spout registration —
                // see GoLiveViewModel.IsSpoutOutputEnabled) replaces this CPU downsample+pipe
                // path for the *composited* frame with the C# host reading the GPU-shared Spout
                // texture directly (Event::SpoutTextureReady). PiP raw-frame thumbnails below are
                // unaffected — Spout only ever carries the final composited output, never each
                // individual PiP source, so that feed still needs the pipe regardless.
                if compositor_cfg.lock().unwrap().spout_enabled {
                    continue;
                }
                if last_preview_time.elapsed() < preview_interval {
                    continue;
                }
                last_preview_time = tokio::time::Instant::now();

                // The overlay editor's full-resolution preview comes via the JPEG
                // compositor path, not this pipe, so a coarse downsample is fine here.
                if let Err(e) = write_scaled_frame(&mut writer, &raw, 3).await {
                    warn!("Preview pipe write failed: {e}");
                    break;
                }
            }
            result = raw_rx.recv() => {
                let raw = match result {
                    Ok(f) => f,
                    Err(broadcast::error::RecvError::Lagged(n)) => {
                        trace!("PiP thumbnail feed lagged, dropped {n} frames");
                        continue;
                    }
                    // The raw feed closing doesn't mean the pipe should — keep serving the
                    // composited stream even if this side goes away.
                    Err(broadcast::error::RecvError::Closed) => continue,
                };

                if !preview_enabled.load(Ordering::Relaxed) {
                    continue;
                }

                // Only forward sources currently placed in the scene — the primary is no longer
                // structurally different from a PiP (it's just another positioned layer), so its
                // own raw frames get the same live-thumbnail treatment now; anything not
                // currently placed shouldn't leak a thumbnail either way.
                let is_placed = {
                    let cfg = compositor_cfg.lock().unwrap();
                    cfg.sources.iter().any(|s| s.source_id == raw.source_id)
                };
                if !is_placed {
                    continue;
                }

                let now = tokio::time::Instant::now();
                let due = last_pip_times
                    .get(&raw.source_id)
                    .map_or(true, |last| now.duration_since(*last) >= pip_interval);
                if !due {
                    continue;
                }
                last_pip_times.insert(raw.source_id.clone(), now);

                // PiP boxes are much smaller on screen than the primary preview, so a coarser
                // downsample keeps pipe traffic reasonable with several PiPs active at once.
                if let Err(e) = write_scaled_frame(&mut writer, &raw, 6).await {
                    warn!("PiP thumbnail write failed: {e}");
                    break;
                }
            }
        }
    }

    Ok(())
}

/// Create a named page-file-backed shared memory section for the overlay.
/// Electron will open it by name and write BGRA frames via a seqlock.
/// Rust maps a read-only view so the compositor can read without kernel I/O.
#[allow(unsafe_code)]
fn create_shm_overlay(name: &str, size: u32) -> Result<SharedShmOverlay> {
    use windows::Win32::Foundation::INVALID_HANDLE_VALUE;
    use windows::Win32::System::Memory::{
        CreateFileMappingW, MapViewOfFile, FILE_MAP_READ, PAGE_READWRITE,
    };
    use windows::core::PCWSTR;

    let name_wide: Vec<u16> = name.encode_utf16().chain(std::iter::once(0u16)).collect();

    let mapping = unsafe {
        CreateFileMappingW(
            INVALID_HANDLE_VALUE,
            None,
            PAGE_READWRITE,
            0,
            size,
            PCWSTR(name_wide.as_ptr()),
        )
        .context("CreateFileMappingW failed for overlay SHM")?
    };

    let view = unsafe { MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, 0) };
    if view.Value.is_null() {
        return Err(anyhow!("MapViewOfFile failed for overlay SHM"));
    }

    // The section handle is intentionally leaked here: the view holds an implicit
    // reference so the named section remains accessible to Electron via
    // OpenFileMappingW for the entire process lifetime.
    std::mem::forget(mapping);

    Ok(Arc::new(ShmOverlay {
        view: view.Value as *const u8,
        size: size as usize,
    }))
}

/// Constant-time byte slice equality — prevents timing-based token oracle.
fn tokens_equal(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    let diff = a.iter().zip(b.iter()).fold(0u8, |acc, (x, y)| acc | (x ^ y));
    diff == 0
}

// ── Tests ──────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;
    use streamflow_ipc::{decode_event, encode_command, Command, Event, FrameError, PROTOCOL_VERSION};

    #[test]
    fn ready_event_has_correct_version() {
        let event = Event::Ready {
            version: PROTOCOL_VERSION,
            pid: std::process::id(),
            pipe: r"\\.\pipe\streamflow-test".into(),
            shm_name: r"Local\StreamFlowOverlay-test".into(),
            shm_size: 8_294_412,
        };
        match event {
            Event::Ready { version, .. } => assert_eq!(version, PROTOCOL_VERSION),
            _ => panic!("unexpected event variant"),
        }
    }

    #[test]
    fn ping_encodes_and_pong_decodes() {
        let cmd_bytes = encode_command(&Command::Ping).unwrap();
        let pong_bytes = {
            let mut b = serde_json::to_vec(&Event::Pong {
                version: PROTOCOL_VERSION,
            })
            .unwrap();
            b.push(b'\n');
            b
        };
        let line = std::str::from_utf8(&pong_bytes[..pong_bytes.len() - 1]).unwrap();
        let decoded = decode_event(line).unwrap();
        assert!(matches!(decoded, Event::Pong { .. }));
        assert!(!cmd_bytes.is_empty());
        assert_eq!(*cmd_bytes.last().unwrap(), b'\n');
    }

    #[test]
    fn tokens_equal_same() {
        assert!(tokens_equal(b"abc", b"abc"));
    }

    #[test]
    fn tokens_equal_different_content() {
        assert!(!tokens_equal(b"abc", b"abd"));
    }

    #[test]
    fn tokens_equal_different_length() {
        assert!(!tokens_equal(b"abc", b"abcd"));
    }

    #[test]
    fn tokens_equal_empty() {
        assert!(tokens_equal(b"", b""));
    }

    #[test]
    fn auth_command_encodes_correctly() {
        let cmd = Command::Auth {
            token: "tok".into(),
            pipe_id: "pipeid".into(),
        };
        let encoded = encode_command(&cmd).unwrap();
        let line = std::str::from_utf8(&encoded[..encoded.len() - 1]).unwrap();
        assert!(matches!(
            decode_command(line).unwrap(),
            Command::Auth { .. }
        ));
    }

    #[test]
    fn frame_error_display() {
        let e = FrameError::UnknownFrameType(42);
        assert!(e.to_string().contains("42"));
    }
}

pub trait CaptureSessionTrait: Send + Sync {
    fn stop(&mut self) -> anyhow::Result<()>;
}

impl CaptureSessionTrait for capture::CaptureSession {
    fn stop(&mut self) -> anyhow::Result<()> {
        capture::CaptureSession::stop(self)
    }
}

impl CaptureSessionTrait for capture_mf::MFCaptureSession {
    fn stop(&mut self) -> anyhow::Result<()> {
        capture_mf::MFCaptureSession::stop(self)
    }
}

impl CaptureSessionTrait for video_overlay::VideoOverlaySession {
    fn stop(&mut self) -> anyhow::Result<()> {
        video_overlay::VideoOverlaySession::stop(self)
    }
}
