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
        // 1. Read from ringbuf
        let available = self.cons.occupied_len() as i32 / self.channels;
        if available > 0 {
            let mut interleaved = vec![0.0f32; (available * self.channels) as usize];
            let read = self.cons.pop_slice(&mut interleaved);
            let samples_read = read as i32 / self.channels;

            // Convert and push to FIFO
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

        let sample_rate = (*self.codec_ctx).sample_rate;
        let track_time = (self.pts + av_audio_fifo_size(self.fifo) as i64) as f64 / sample_rate as f64;
        let diff = elapsed_secs - track_time;

        if diff > 0.05 {
            let silence_samples = (diff * sample_rate as f64) as i32;
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
        let frame_size = (*self.codec_ctx).frame_size;
        while av_audio_fifo_size(self.fifo) >= frame_size {
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
        }

        Ok(())
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
