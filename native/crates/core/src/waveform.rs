use anyhow::{anyhow, Result};
use ffmpeg_sys_next::*;
use std::ffi::CString;
use std::ptr;

pub fn compute_peaks(path: &str, pixels_per_second: u32) -> Result<Vec<f32>> {
    unsafe { compute_peaks_unsafe(path, pixels_per_second) }
}

unsafe fn compute_peaks_unsafe(path: &str, pixels_per_second: u32) -> Result<Vec<f32>> {
    let c_path = CString::new(path)?;
    let mut fmt_ctx: *mut AVFormatContext = ptr::null_mut();

    if avformat_open_input(&mut fmt_ctx, c_path.as_ptr(), ptr::null_mut(), ptr::null_mut()) < 0 {
        return Err(anyhow!("Failed to open input file: {}", path));
    }

    if avformat_find_stream_info(fmt_ctx, ptr::null_mut()) < 0 {
        avformat_close_input(&mut fmt_ctx);
        return Err(anyhow!("Failed to find stream info"));
    }

    // find audio stream
    let mut audio_stream_idx = -1;
    for i in 0..(*fmt_ctx).nb_streams {
        let stream = *(*fmt_ctx).streams.add(i as usize);
        if (*(*stream).codecpar).codec_type == AVMediaType::AVMEDIA_TYPE_AUDIO {
            audio_stream_idx = i as i32;
            break;
        }
    }

    if audio_stream_idx == -1 {
        avformat_close_input(&mut fmt_ctx);
        return Err(anyhow!("No audio stream found"));
    }

    let stream = *(*fmt_ctx).streams.add(audio_stream_idx as usize);
    let codecpar = (*stream).codecpar;
    let codec = avcodec_find_decoder((*codecpar).codec_id);
    if codec.is_null() {
        avformat_close_input(&mut fmt_ctx);
        return Err(anyhow!("Audio decoder not found"));
    }

    let mut codec_ctx = avcodec_alloc_context3(codec);
    if avcodec_parameters_to_context(codec_ctx, codecpar) < 0 {
        avcodec_free_context(&mut codec_ctx);
        avformat_close_input(&mut fmt_ctx);
        return Err(anyhow!("Failed to copy codec params to context"));
    }

    if avcodec_open2(codec_ctx, codec, ptr::null_mut()) < 0 {
        avcodec_free_context(&mut codec_ctx);
        avformat_close_input(&mut fmt_ctx);
        return Err(anyhow!("Failed to open decoder"));
    }

    let sample_rate = (*codec_ctx).sample_rate;
    
    // We want output in FLT format (single precision float) and mono.
    let mut swr = swr_alloc();
    let mut out_ch_layout: AVChannelLayout = std::mem::zeroed();
    av_channel_layout_default(&mut out_ch_layout, 1);
    
    swr_alloc_set_opts2(
        &mut swr,
        &out_ch_layout,
        AVSampleFormat::AV_SAMPLE_FMT_FLT,
        sample_rate,
        &(*codec_ctx).ch_layout,
        (*codec_ctx).sample_fmt,
        sample_rate,
        0,
        ptr::null_mut()
    );

    if swr_init(swr) < 0 {
        swr_free(&mut swr);
        avcodec_free_context(&mut codec_ctx);
        avformat_close_input(&mut fmt_ctx);
        return Err(anyhow!("Failed to init swresample"));
    }

    let mut frame = av_frame_alloc();
    let mut pkt = av_packet_alloc();

    let mut peaks = Vec::new();
    let samples_per_pixel = (sample_rate as f32 / pixels_per_second as f32).ceil() as usize;

    let mut current_max = 0.0f32;
    let mut current_min = 0.0f32;
    let mut sample_count = 0;

    loop {
        if av_read_frame(fmt_ctx, pkt) < 0 {
            break;
        }

        if (*pkt).stream_index == audio_stream_idx {
            if avcodec_send_packet(codec_ctx, pkt) == 0 {
                while avcodec_receive_frame(codec_ctx, frame) == 0 {
                    let mut out_buffer: *mut u8 = ptr::null_mut();
                    let out_samples = swr_get_out_samples(swr, (*frame).nb_samples);
                    av_samples_alloc(
                        &mut out_buffer,
                        ptr::null_mut(),
                        1,
                        out_samples,
                        AVSampleFormat::AV_SAMPLE_FMT_FLT,
                        0,
                    );

                    let in_data = (*frame).data.as_ptr() as *const *const u8;
                    let ret = swr_convert(
                        swr,
                        &mut out_buffer,
                        out_samples,
                        in_data,
                        (*frame).nb_samples,
                    );

                    if ret > 0 {
                        let num_floats = ret as usize;
                        let floats: &[f32] = std::slice::from_raw_parts(out_buffer as *const f32, num_floats);

                        for &f in floats {
                            if f > current_max { current_max = f; }
                            if f < current_min { current_min = f; }

                            sample_count += 1;
                            if sample_count >= samples_per_pixel {
                                // push the absolute peak
                                peaks.push(current_max.abs().max(current_min.abs()));
                                current_max = 0.0;
                                current_min = 0.0;
                                sample_count = 0;
                            }
                        }
                    }

                    if !out_buffer.is_null() {
                        av_freep(&mut out_buffer as *mut _ as *mut _);
                    }
                    av_frame_unref(frame);
                }
            }
        }
        av_packet_unref(pkt);
    }

    // Flush decoder
    if avcodec_send_packet(codec_ctx, ptr::null()) == 0 {
        while avcodec_receive_frame(codec_ctx, frame) == 0 {
            let mut out_buffer: *mut u8 = ptr::null_mut();
            let out_samples = swr_get_out_samples(swr, (*frame).nb_samples);
            av_samples_alloc(
                &mut out_buffer,
                ptr::null_mut(),
                1,
                out_samples,
                AVSampleFormat::AV_SAMPLE_FMT_FLT,
                0,
            );

            let in_data = (*frame).data.as_ptr() as *const *const u8;
            let ret = swr_convert(
                swr,
                &mut out_buffer,
                out_samples,
                in_data,
                (*frame).nb_samples,
            );

            if ret > 0 {
                let num_floats = ret as usize;
                let floats: &[f32] = std::slice::from_raw_parts(out_buffer as *const f32, num_floats);

                for &f in floats {
                    if f > current_max { current_max = f; }
                    if f < current_min { current_min = f; }

                    sample_count += 1;
                    if sample_count >= samples_per_pixel {
                        peaks.push(current_max.abs().max(current_min.abs()));
                        current_max = 0.0;
                        current_min = 0.0;
                        sample_count = 0;
                    }
                }
            }

            if !out_buffer.is_null() {
                av_freep(&mut out_buffer as *mut _ as *mut _);
            }
            av_frame_unref(frame);
        }
    }

    if sample_count > 0 {
        peaks.push(current_max.abs().max(current_min.abs()));
    }

    av_packet_free(&mut pkt);
    av_frame_free(&mut frame);
    swr_free(&mut swr);
    avcodec_free_context(&mut codec_ctx);
    avformat_close_input(&mut fmt_ctx);

    Ok(peaks)
}
