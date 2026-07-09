#![allow(unsafe_code)]

use std::ffi::CString;
use std::ptr;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread;
use std::time::{Duration, Instant};

use anyhow::{anyhow, Result};
use ffmpeg_sys_next::*;
use tokio::sync::broadcast;

use crate::capture::RawFrame;

/// Plays a local video file on loop, decoded via FFmpeg and pushed into the same broadcast
/// channel live capture sessions use — the compositor treats it exactly like a PiP source (see
/// StreamSourceDef's source_id matching). Unlike the other overlay kinds (image/text/color),
/// this needs an ongoing decode thread rather than being registered once, so it's structured
/// like `capture_mf::MFCaptureSession` instead of going through AddStaticOverlay.
pub struct VideoOverlaySession {
    stop_flag: Arc<AtomicBool>,
    thread_handle: Option<thread::JoinHandle<()>>,
}

impl VideoOverlaySession {
    pub fn new(source_id: String, path: String, tx: broadcast::Sender<Arc<RawFrame>>) -> Result<Self> {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let thread_flag = Arc::clone(&stop_flag);

        let handle = thread::spawn(move || {
            if let Err(e) = run_decode_loop(&source_id, &path, &thread_flag, &tx) {
                tracing::warn!(source = %source_id, %path, "Video overlay decode loop exited: {e:#}");
            }
        });

        Ok(Self {
            stop_flag,
            thread_handle: Some(handle),
        })
    }

    pub fn stop(&mut self) -> Result<()> {
        self.stop_flag.store(true, Ordering::Relaxed);
        if let Some(h) = self.thread_handle.take() {
            let _ = h.join();
        }
        Ok(())
    }
}

/// Reads native width/height from the file's stream metadata without decoding any frames —
/// cheap enough to run synchronously on the command thread before spawning the decode loop, so
/// the app can lock the overlay's aspect ratio as soon as it's added instead of only once a
/// frame arrives (unlike monitor/window sources, video files aren't enumerable via GetSources
/// ahead of time to report this earlier).
pub fn probe_dimensions(path: &str) -> Result<(u32, u32)> {
    unsafe {
        let path_c = CString::new(path).map_err(|_| anyhow!("path contains a NUL byte"))?;

        let mut fmt_ctx_ptr: *mut AVFormatContext = ptr::null_mut();
        check(
            avformat_open_input(&mut fmt_ctx_ptr, path_c.as_ptr(), ptr::null_mut(), ptr::null_mut()),
            "avformat_open_input",
        )?;
        let fmt_ctx = FormatCtx(fmt_ctx_ptr);

        check(avformat_find_stream_info(fmt_ctx.0, ptr::null_mut()), "avformat_find_stream_info")?;

        let mut decoder: *const AVCodec = ptr::null();
        let stream_index = av_find_best_stream(fmt_ctx.0, AVMediaType::AVMEDIA_TYPE_VIDEO, -1, -1, &mut decoder, 0);
        if stream_index < 0 {
            return Err(anyhow!("No playable video stream found in {path}"));
        }

        let stream = *(*fmt_ctx.0).streams.offset(stream_index as isize);
        let params = (*stream).codecpar;
        let (width, height) = ((*params).width, (*params).height);
        if width <= 0 || height <= 0 {
            return Err(anyhow!("Video stream reports invalid dimensions {width}x{height}"));
        }

        Ok((width as u32, height as u32))
    }
}

fn check(ret: i32, op: &str) -> Result<()> {
    if ret < 0 {
        Err(anyhow!("{op} failed (ffmpeg error {ret})"))
    } else {
        Ok(())
    }
}

// RAII guards for the FFmpeg resources this needs, mirroring the OwnedPacket/PipScaler pattern
// used elsewhere in this codebase — freed automatically on any early return via `?`.

struct FormatCtx(*mut AVFormatContext);
impl Drop for FormatCtx {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe { avformat_close_input(&mut self.0) };
        }
    }
}

struct CodecCtx(*mut AVCodecContext);
impl Drop for CodecCtx {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe { avcodec_free_context(&mut self.0) };
        }
    }
}

struct SwsCtx(*mut SwsContext);
impl Drop for SwsCtx {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe { sws_freeContext(self.0) };
        }
    }
}

struct OwnedAvFrame(*mut AVFrame);
impl Drop for OwnedAvFrame {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe { av_frame_free(&mut self.0) };
        }
    }
}

struct OwnedAvPacket(*mut AVPacket);
impl Drop for OwnedAvPacket {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe { av_packet_free(&mut self.0) };
        }
    }
}

fn run_decode_loop(
    source_id: &str,
    path: &str,
    stop_flag: &Arc<AtomicBool>,
    tx: &broadcast::Sender<Arc<RawFrame>>,
) -> Result<()> {
    unsafe {
        let path_c = CString::new(path).map_err(|_| anyhow!("path contains a NUL byte"))?;

        let mut fmt_ctx_ptr: *mut AVFormatContext = ptr::null_mut();
        check(
            avformat_open_input(&mut fmt_ctx_ptr, path_c.as_ptr(), ptr::null_mut(), ptr::null_mut()),
            "avformat_open_input",
        )?;
        let fmt_ctx = FormatCtx(fmt_ctx_ptr);

        check(avformat_find_stream_info(fmt_ctx.0, ptr::null_mut()), "avformat_find_stream_info")?;

        let mut decoder: *const AVCodec = ptr::null();
        let stream_index = av_find_best_stream(fmt_ctx.0, AVMediaType::AVMEDIA_TYPE_VIDEO, -1, -1, &mut decoder, 0);
        if stream_index < 0 {
            return Err(anyhow!("No playable video stream found in {path}"));
        }
        if decoder.is_null() {
            return Err(anyhow!("No decoder available for the video stream in {path}"));
        }

        let stream = *(*fmt_ctx.0).streams.add(stream_index as usize);
        let time_base = (*stream).time_base;

        let codec_ctx_ptr = avcodec_alloc_context3(decoder);
        if codec_ctx_ptr.is_null() {
            return Err(anyhow!("avcodec_alloc_context3 failed"));
        }
        let codec_ctx = CodecCtx(codec_ctx_ptr);

        check(avcodec_parameters_to_context(codec_ctx.0, (*stream).codecpar), "avcodec_parameters_to_context")?;
        check(avcodec_open2(codec_ctx.0, decoder, ptr::null_mut()), "avcodec_open2")?;

        let width = (*codec_ctx.0).width;
        let height = (*codec_ctx.0).height;
        if width <= 0 || height <= 0 {
            return Err(anyhow!("Decoded video has invalid dimensions"));
        }

        let mut sws_ctx: Option<SwsCtx> = None;
        let mut bgra_buf = vec![0u8; (width * height * 4) as usize];

        let packet = OwnedAvPacket(av_packet_alloc());
        let frame = OwnedAvFrame(av_frame_alloc());
        if packet.0.is_null() || frame.0.is_null() {
            return Err(anyhow!("Failed to allocate an AVPacket/AVFrame"));
        }

        let mut playback_started = Instant::now();

        'playback: loop {
            if stop_flag.load(Ordering::Relaxed) {
                break 'playback;
            }

            let read_ret = av_read_frame(fmt_ctx.0, packet.0);
            if read_ret < 0 {
                // End of file (or a transient read error, treated the same way) — loop
                // playback by seeking back to the start instead of ending the session.
                av_packet_unref(packet.0);
                let seek_ret = av_seek_frame(fmt_ctx.0, stream_index, 0, AVSEEK_FLAG_BACKWARD);
                if seek_ret < 0 {
                    return Err(anyhow!("Failed to loop video playback (seek error {seek_ret})"));
                }
                avcodec_flush_buffers(codec_ctx.0);
                playback_started = Instant::now();
                continue;
            }

            if (*packet.0).stream_index != stream_index {
                av_packet_unref(packet.0);
                continue;
            }

            let send_ret = avcodec_send_packet(codec_ctx.0, packet.0);
            av_packet_unref(packet.0);
            if send_ret < 0 {
                continue; // Skip an unreadable packet rather than aborting playback entirely.
            }

            loop {
                let recv_ret = avcodec_receive_frame(codec_ctx.0, frame.0);
                if recv_ret == AVERROR(EAGAIN) || recv_ret == AVERROR_EOF {
                    break;
                }
                check(recv_ret, "avcodec_receive_frame")?;

                if sws_ctx.is_none() {
                    let src_format: AVPixelFormat = std::mem::transmute((*frame.0).format);
                    let ctx = sws_getContext(
                        width, height, src_format,
                        width, height, AVPixelFormat::AV_PIX_FMT_BGRA,
                        SwsFlags::SWS_BILINEAR as i32, ptr::null_mut(), ptr::null_mut(), ptr::null(),
                    );
                    if ctx.is_null() {
                        return Err(anyhow!("sws_getContext failed for the decoded pixel format"));
                    }
                    sws_ctx = Some(SwsCtx(ctx));
                }

                let dst_stride = width * 4;
                let mut dst_data: [*mut u8; 4] = [bgra_buf.as_mut_ptr(), ptr::null_mut(), ptr::null_mut(), ptr::null_mut()];
                let dst_linesize: [i32; 4] = [dst_stride, 0, 0, 0];

                sws_scale(
                    sws_ctx.as_ref().unwrap().0,
                    (*frame.0).data.as_ptr().cast(),
                    (*frame.0).linesize.as_ptr(),
                    0, height,
                    dst_data.as_mut_ptr(),
                    dst_linesize.as_ptr(),
                );

                // Pace playback to roughly real-time using the frame's own presentation
                // timestamp, rather than decoding (and thus visibly playing) as fast as possible.
                let pts = (*frame.0).best_effort_timestamp;
                if pts != AV_NOPTS_VALUE {
                    let pts_seconds = (pts as f64 * av_q2d(time_base)).max(0.0);
                    let target = playback_started + Duration::from_secs_f64(pts_seconds);
                    let now = Instant::now();
                    if target > now {
                        thread::sleep(target - now);
                    }
                }

                let _ = tx.send(Arc::new(RawFrame {
                    source_id: source_id.to_string(),
                    width: width as u32,
                    height: height as u32,
                    pixels: bgra_buf.clone(),
                    timestamp_100ns: 0,
                }));

                if stop_flag.load(Ordering::Relaxed) {
                    break 'playback;
                }
            }
        }
    }

    Ok(())
}
