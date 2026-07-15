use anyhow::{anyhow, Result};
use ffmpeg_sys_next::*;
use ringbuf::{traits::{Consumer, Observer}, HeapCons};
use std::ptr;

pub struct AudioEncoder {
    codec_ctx: *mut AVCodecContext,
    stream_idx: i32,
    stream_tb: AVRational,
    fifo: *mut AVAudioFifo,
    swr: *mut SwrContext,
    frame: *mut AVFrame,
    pkt: *mut AVPacket,
    pts: i64,
    channels: i32,
    cons: HeapCons<f32>,
    pub frame_count: u64,
    /// Cumulative samples injected by the small-drift silence-insertion path (diff between 0.05s
    /// and the hard-resync threshold) — exposed so the periodic status log can show how often
    /// this is actually firing. Added to check a live-Twitch-only "audio drops every so often"
    /// report against real evidence instead of guessing: each insertion is genuine injected
    /// silence, audible as a brief dropout if it fires often enough.
    pub silence_samples_inserted: u64,
}

unsafe impl Send for AudioEncoder {}

impl AudioEncoder {
    pub unsafe fn new(
        ofmt_ctx: *mut AVFormatContext,
        config: &cpal::StreamConfig,
        cons: HeapCons<f32>,
    ) -> Result<Self> {
        let codec = avcodec_find_encoder(AVCodecID::AV_CODEC_ID_AAC);
        if codec.is_null() {
            return Err(anyhow!("AAC encoder not found"));
        }

        let out_stream = avformat_new_stream(ofmt_ctx, ptr::null());
        if out_stream.is_null() {
            return Err(anyhow!("Failed to create audio stream"));
        }
        let stream_idx = (*out_stream).index;

        let codec_ctx = avcodec_alloc_context3(codec);
        if codec_ctx.is_null() {
            return Err(anyhow!("Failed to allocate AAC codec context"));
        }

        let channels = config.channels as i32;
        let sample_rate = config.sample_rate.0 as i32;

        let mut ch_layout: AVChannelLayout = std::mem::zeroed();
        av_channel_layout_default(&mut ch_layout, channels);

        (*codec_ctx).codec_id = AVCodecID::AV_CODEC_ID_AAC;
        (*codec_ctx).sample_fmt = AVSampleFormat::AV_SAMPLE_FMT_FLTP;
        (*codec_ctx).sample_rate = sample_rate;
        av_channel_layout_copy(&mut (*codec_ctx).ch_layout, &ch_layout);
        (*codec_ctx).bit_rate = 160_000; // 160 kbps AAC

        if !(*ofmt_ctx).oformat.is_null() && ((*(*ofmt_ctx).oformat).flags & AVFMT_GLOBALHEADER as i32) != 0 {
            (*codec_ctx).flags |= AV_CODEC_FLAG_GLOBAL_HEADER as i32;
        }

        (*codec_ctx).time_base = AVRational { num: 1, den: sample_rate };

        if avcodec_open2(codec_ctx, codec, ptr::null_mut()) < 0 {
            avcodec_free_context(&mut (codec_ctx as *mut _));
            av_channel_layout_uninit(&mut ch_layout);
            return Err(anyhow!("Failed to open AAC encoder"));
        }

        avcodec_parameters_from_context((*out_stream).codecpar, codec_ctx);
        (*out_stream).time_base = AVRational { num: 1, den: sample_rate };

        // FIFO setup
        let fifo = av_audio_fifo_alloc(AVSampleFormat::AV_SAMPLE_FMT_FLTP, channels, 1);
        if fifo.is_null() {
            avcodec_free_context(&mut (codec_ctx as *mut _));
            av_channel_layout_uninit(&mut ch_layout);
            return Err(anyhow!("Failed to allocate audio FIFO"));
        }

        // SwrContext setup: cpal is interleaved f32, AAC wants planar f32
        let mut swr: *mut SwrContext = ptr::null_mut();
        let ret = swr_alloc_set_opts2(
            &mut swr,
            &ch_layout,
            AVSampleFormat::AV_SAMPLE_FMT_FLTP,
            sample_rate,
            &ch_layout,
            AVSampleFormat::AV_SAMPLE_FMT_FLT, // cpal format
            sample_rate,
            0,
            ptr::null_mut(),
        );

        if ret < 0 || swr.is_null() || swr_init(swr) < 0 {
            if !swr.is_null() { swr_free(&mut swr); }
            av_audio_fifo_free(fifo);
            avcodec_free_context(&mut (codec_ctx as *mut _));
            av_channel_layout_uninit(&mut ch_layout);
            return Err(anyhow!("Failed to init resampler"));
        }

        let frame = av_frame_alloc();
        (*frame).nb_samples = (*codec_ctx).frame_size;
        (*frame).format = (*codec_ctx).sample_fmt as i32;
        av_channel_layout_copy(&mut (*frame).ch_layout, &ch_layout);
        (*frame).sample_rate = sample_rate;
        av_frame_get_buffer(frame, 0);

        let pkt = av_packet_alloc();

        av_channel_layout_uninit(&mut ch_layout);

        tracing::info!("Audio encoder initialized: AAC {} channels {} Hz", channels, sample_rate);

        Ok(Self {
            codec_ctx,
            stream_idx,
            stream_tb: (*out_stream).time_base,
            fifo,
            swr,
            frame,
            pkt,
            pts: 0,
            channels,
            cons,
            frame_count: 0,
            silence_samples_inserted: 0,
        })
    }

    pub unsafe fn update_stream_timebase(&mut self, ofmt_ctx: *mut AVFormatContext) {
        let stream = *(*ofmt_ctx).streams.add(self.stream_idx as usize);
        self.stream_tb = (*stream).time_base;
        tracing::info!("Audio stream time_base updated to {}/{}", self.stream_tb.num, self.stream_tb.den);
    }

    pub unsafe fn poll<F>(&mut self, elapsed_secs: f64, send_packet: &mut F) -> Result<()>
    where
        F: FnMut(*mut AVPacket) -> Result<()>
    {
        let sample_rate = (*self.codec_ctx).sample_rate;

        // Checked before touching the ring buffer this tick: a source delivering samples at a
        // genuinely wrong rate (e.g. a capture device whose real engine clock doesn't match the
        // nominal rate CPAL/WASAPI negotiated — observed with a Voicemeeter virtual bus running
        // audio ~1.56x realtime, sustained and constant from the very first tick, not a stall)
        // races audio's own pts steadily *ahead* of wall-clock. Once already-emitted packets carry
        // a given pts, self.pts can only ever move forward — rewinding it to resync would emit a
        // packet with a *lower* pts than one already sent, which FLV/RTMP's monotonically-
        // increasing DTS requirement rejects outright (observed in practice: "Application provided
        // invalid, non monotonically increasing dts to muxer", the write erroring out, and the
        // whole RTMP writer thread aborting — Twitch saw the stream end while this app kept
        // running, unaware its own writer thread had already died). So when running ahead, correct
        // by discarding freshly-arrived raw input instead: skip converting/queuing it into the
        // FIFO this tick, which pauses audio's own clock (self.pts stops advancing) until wall-
        // clock catches back up on its own — pts only ever holds still or advances, never reverses.
        let pre_fifo_size = av_audio_fifo_size(self.fifo) as i64;
        let pre_track_time = (self.pts + pre_fifo_size) as f64 / sample_rate as f64;
        if elapsed_secs - pre_track_time < -1.0 {
            let discard = self.cons.occupied_len();
            if discard > 0 {
                let mut scratch = vec![0.0f32; discard];
                self.cons.pop_slice(&mut scratch);
            }
            tracing::warn!(
                "Audio ran {:.2}s ahead of wall-clock - discarding new input instead of rewinding pts",
                pre_track_time - elapsed_secs
            );
        } else {
            // Rate-limit consumption to what wall-clock says we currently need.
            //
            // Previously this read ALL available samples unconditionally. That works fine when
            // sources run at exactly realtime, but a source whose real engine clock runs faster
            // than realtime (VoiceMeeter observed at ~1.33x: 21ms of audio delivered every 16ms
            // of wall-clock) pre-loads the FIFO with "future" audio. Each tick, pts advances by
            // 21ms while wall-clock only advanced 16ms. After ~4 seconds: pts is 1s+ ahead →
            // the ">1s ahead" hard-discard above fires on every tick → constant audible cuts.
            //
            // Fix: only pop as many frames as the current wall-clock deficit allows. Excess audio
            // stays in the ring buffer; the mixer's HWM backpressure handles draining it. The
            // encoder stays at wall-clock pace regardless of the source's actual delivery rate.
            let current_track_secs = (self.pts + pre_fifo_size) as f64 / sample_rate as f64;
            let deficit_frames = ((elapsed_secs - current_track_secs) * sample_rate as f64)
                .max(0.0) as usize;
            let available_frames = self.cons.occupied_len() / self.channels as usize;
            let frames_to_read = available_frames.min(deficit_frames);

            if frames_to_read > 0 {
                let samples_to_read = frames_to_read * self.channels as usize;
                let mut interleaved = vec![0.0f32; samples_to_read];
                let read = self.cons.pop_slice(&mut interleaved);
                let samples_read = read as i32 / self.channels;

                // Convert interleaved f32 → planar f32 and write to the AAC encoder FIFO.
                let in_data_ptrs: [*const u8; 1] = [interleaved.as_ptr() as *const u8];
                let mut out_data: [*mut u8; 8] = [ptr::null_mut(); 8];
                let mut out_linesize = 0;
                av_samples_alloc(
                    out_data.as_mut_ptr(),
                    &mut out_linesize,
                    self.channels,
                    samples_read,
                    AVSampleFormat::AV_SAMPLE_FMT_FLTP,
                    0,
                );

                let out_samples = swr_convert(
                    self.swr,
                    out_data.as_mut_ptr(),
                    samples_read,
                    in_data_ptrs.as_ptr(),
                    samples_read,
                );

                if out_samples > 0 {
                    av_audio_fifo_write(
                        self.fifo,
                        out_data.as_mut_ptr() as *mut *mut std::ffi::c_void,
                        out_samples,
                    );
                } else if out_samples < 0 {
                    tracing::error!("swr_convert failed with code {}", out_samples);
                }

                av_freep(&mut out_data[0] as *mut _ as *mut std::ffi::c_void);
            }
        }

        let track_time = (self.pts + av_audio_fifo_size(self.fifo) as i64) as f64 / sample_rate as f64;
        let diff = elapsed_secs - track_time;

        // Above a small drift, the encode-and-send loop below (capped at
        // MAX_AUDIO_FRAMES_PER_POLL frames/poll, by design — see its own comment) can only ever
        // claw back a couple of AAC frames per tick. That's enough to correct the sub-100ms drift
        // it was built for, but a genuine multi-second gap (a stalled capture device/mixer thread
        // resuming with a large backlog, anything upstream that stalls for a while) would take
        // many seconds-to-minutes to fully drain at that rate. During all of that time this
        // stream's packets carry pts values that lag video's by the full gap — and since both
        // streams share one av_interleaved_write_frame call (see streaming.rs's rtmp-writer
        // thread), FFmpeg's own interleave buffer has to hold that gap open, hitting its hardcoded
        // 10s max_interleave_delta and force-flushing on nearly every write once the gap grows
        // that large. That's a stable, self-perpetuating state, not a transient one — observed in
        // practice as "[flv] ... forcing output" spammed indefinitely while every one of this
        // app's own health metrics (fps, queue depth, drops) looked completely normal, because
        // this stream's own throughput was fine, just chronically offset from video. Once the gap
        // is this large, gradual catch-up isn't going to close it on any reasonable timescale —
        // hard-resync instead: drop whatever's queued and jump straight to now, the same fix
        // clear_backlog() already applies once at stream start, just re-triggerable any time the
        // gap reopens mid-stream instead of only before the first frame. Forward-only: jumping
        // pts *ahead* is always monotonic-safe (see the ahead-of-wall-clock check above for why
        // the reverse direction can't use this same jump).
        const HARD_RESYNC_THRESHOLD_SECS: f64 = 1.0;
        if diff > HARD_RESYNC_THRESHOLD_SECS {
            let queued = av_audio_fifo_size(self.fifo);
            if queued > 0 {
                av_audio_fifo_drain(self.fifo, queued);
            }
            self.pts = (elapsed_secs * sample_rate as f64) as i64;
            tracing::warn!(
                "Audio fell {diff:.2}s behind wall-clock - hard-resyncing instead of gradual catch-up"
            );
        } else if diff > 0.05 {
            let silence_samples = (diff * sample_rate as f64) as i32;
            self.silence_samples_inserted += silence_samples.max(0) as u64;
            let mut silence_data: [*mut u8; 8] = [ptr::null_mut(); 8];
            let mut silence_linesize = 0;
            av_samples_alloc(
                silence_data.as_mut_ptr(),
                &mut silence_linesize,
                self.channels,
                silence_samples,
                AVSampleFormat::AV_SAMPLE_FMT_FLTP,
                0,
            );
            av_samples_set_silence(
                silence_data.as_mut_ptr(),
                0,
                silence_samples,
                self.channels,
                AVSampleFormat::AV_SAMPLE_FMT_FLTP,
            );
            av_audio_fifo_write(
                self.fifo,
                silence_data.as_mut_ptr() as *mut *mut std::ffi::c_void,
                silence_samples,
            );
            av_freep(&mut silence_data[0] as *mut _ as *mut std::ffi::c_void);
        }

        // 2. Encode from FIFO if enough samples
        //
        // Capped per poll() call — unlike the video path (streaming.rs's own encode loop, which
        // paces itself against wall-clock time via spin_sleep and only ever "catches up" by
        // skipping PTS ranges, never by bursting), this loop used to drain the *entire* FIFO
        // unconditionally, however much had backed up. poll() is only ever called once per video
        // tick, so if the ring buffer (self.cons, fed by a separate capture thread) had
        // accumulated any backlog — a momentary capture-thread scheduling hiccup, OS jitter,
        // anything — this loop would encode and send all of it in one tight, completely unpaced
        // burst: multiple seconds of audio hitting the RTMP socket within milliseconds, on the
        // same connection as video. That's precisely "sending data faster than realtime" (verbatim
        // YouTube ingest error) — audio has no equivalent of video's own real-time throttle. Fix:
        // bound how many AAC frames get sent per call, so a backlog drains gradually over several
        // (already wall-clock-paced, once per video tick) poll() calls instead of all at once.
        // 2 is deliberately a little above the ~1 frame/tick steady-state ratio (an AAC frame is
        // ~21-23ms at typical sample rates; a video tick is ~16-33ms) so genuinely transient drift
        // still recovers within a couple of ticks, without ever permitting an unbounded burst.
        const MAX_AUDIO_FRAMES_PER_POLL: i32 = 2;
        let frame_size = (*self.codec_ctx).frame_size;
        let mut frames_sent_this_poll = 0;
        while av_audio_fifo_size(self.fifo) >= frame_size && frames_sent_this_poll < MAX_AUDIO_FRAMES_PER_POLL {
            if av_frame_make_writable(self.frame) < 0 {
                tracing::error!("Failed to make audio frame writable");
                break;
            }

            av_audio_fifo_read(
                self.fifo,
                (*self.frame).data.as_mut_ptr() as *mut *mut std::ffi::c_void,
                frame_size,
            );

            (*self.frame).pts = self.pts;
            self.pts += frame_size as i64;

            let ret = avcodec_send_frame(self.codec_ctx, self.frame);
            if ret >= 0 {
                while avcodec_receive_packet(self.codec_ctx, self.pkt) >= 0 {
                    let new_pkt = av_packet_alloc();
                    av_packet_move_ref(new_pkt, self.pkt);

                    av_packet_rescale_ts(new_pkt, (*self.codec_ctx).time_base, self.stream_tb);
                    (*new_pkt).stream_index = self.stream_idx;
                    if let Err(e) = send_packet(new_pkt) {
                        tracing::error!("Failed to send audio packet: {}", e);
                    }
                    self.frame_count += 1;
                }
            } else {
                tracing::error!("avcodec_send_frame failed: {}", ret);
            }
            frames_sent_this_poll += 1;
        }

        Ok(())
    }

    /// Audio's own logical position, in seconds since stream start — the same `track_time`
    /// computed inside `poll()`, exposed so the encode loop's periodic status log can report it
    /// alongside video's pts. Added to actually observe the two streams' pts values diverge
    /// (rather than infer it from ffmpeg's own "forcing output" spam, which only ever says
    /// "gap > 10s" and never the true magnitude) after a mid-stream desync was seen with no
    /// corresponding drift ever detected by poll()'s own wall-clock-vs-track_time check.
    pub unsafe fn track_seconds(&self) -> f64 {
        (self.pts + av_audio_fifo_size(self.fifo) as i64) as f64 / (*self.codec_ctx).sample_rate as f64
    }

    pub fn clear_backlog(&mut self) {
        let available = self.cons.occupied_len();
        if available > 0 {
            let mut temp = vec![0.0f32; available];
            self.cons.pop_slice(&mut temp);
            tracing::warn!("Discarded audio backlog of {} samples to align timelines", available);
        }
    }
}

impl Drop for AudioEncoder {
    fn drop(&mut self) {
        unsafe {
            if !self.pkt.is_null() { av_packet_free(&mut self.pkt); }
            if !self.frame.is_null() { av_frame_free(&mut self.frame); }
            if !self.swr.is_null() { swr_free(&mut self.swr); }
            if !self.fifo.is_null() { av_audio_fifo_free(self.fifo); }
            if !self.codec_ctx.is_null() { avcodec_free_context(&mut self.codec_ctx); }
        }
    }
}
