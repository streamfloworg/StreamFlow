#![allow(unsafe_code)]

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::thread;

use anyhow::{Context, Result};
use windows::core::HSTRING;
use windows::Win32::Media::MediaFoundation::{
    IMFAttributes, IMFMediaSource, IMFSourceReader,
    MFCreateAttributes, MFCreateDeviceSource, MFCreateSourceReaderFromMediaSource,
    MFStartup, MF_VERSION, MFSTARTUP_NOSOCKET,
    MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID,
    MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
    MF_SOURCE_READER_FIRST_VIDEO_STREAM,
    MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING,
    MF_SOURCE_READERF_ERROR,
    MFVideoFormat_RGB32, IMFMediaType, MFCreateMediaType,
    MF_MT_MAJOR_TYPE, MFMediaType_Video, MF_MT_SUBTYPE, MF_MT_FRAME_SIZE,
};
use windows::Win32::System::Com::{CoInitializeEx, COINIT_MULTITHREADED};

use crate::capture::RawFrame;

pub struct MFCaptureSession {
    stop_flag: Arc<AtomicBool>,
    thread_handle: Option<thread::JoinHandle<()>>,
}

impl MFCaptureSession {
    pub fn new(
        source_id: String,
        sym_link: &str,
        tx: tokio::sync::broadcast::Sender<Arc<RawFrame>>,
    ) -> Result<Self> {
        let sym_link = sym_link.to_string();
        let stop_flag = Arc::new(AtomicBool::new(false));
        let thread_flag = stop_flag.clone();

        let handle = thread::spawn(move || {
            if let Err(e) = run_capture_loop(&source_id, &sym_link, thread_flag, tx) {
                tracing::warn!("Webcam capture loop exited with error: {:?}", e);
            }
        });

        Ok(Self {
            stop_flag,
            thread_handle: Some(handle),
        })
    }

    pub fn stop(&mut self) -> Result<()> {
        self.stop_flag.store(true, Ordering::Relaxed);
        if let Some(handle) = self.thread_handle.take() {
            let _ = handle.join();
        }
        Ok(())
    }
}

fn run_capture_loop(
    source_id: &str,
    sym_link: &str,
    stop_flag: Arc<AtomicBool>,
    tx: tokio::sync::broadcast::Sender<Arc<RawFrame>>,
) -> Result<()> {
    unsafe {
        // Initialize COM for this thread
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);

        MFStartup(MF_VERSION, MFSTARTUP_NOSOCKET).context("MFStartup failed")?;

        // 1. Create IMFAttributes for the symbolic link
        let mut attrs: Option<IMFAttributes> = None;
        MFCreateAttributes(&mut attrs, 2)?;
        let attrs = attrs.unwrap();

        attrs.SetGUID(&MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID)?;
        let sym_hstring = HSTRING::from(sym_link);
        attrs.SetString(&MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, &sym_hstring)?;

        // 2. Create MediaSource
        let source: IMFMediaSource = MFCreateDeviceSource(&attrs).context("Failed to create device source for webcam")?;

        // 3. Create SourceReader with video processing enabled so MF can
        //    automatically convert from the camera's native format (NV12/YUY2/MJPEG)
        //    to RGB32 without requiring a manual conversion step.
        let mut reader_attrs: Option<IMFAttributes> = None;
        MFCreateAttributes(&mut reader_attrs, 1)?;
        let reader_attrs = reader_attrs.unwrap();
        let _ = reader_attrs.SetUINT32(&MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);

        let reader: IMFSourceReader = MFCreateSourceReaderFromMediaSource(&source, Some(&reader_attrs)).context("Failed to create source reader")?;

        // 4. Set MediaType to RGB32 (BGRA)
        let media_type: IMFMediaType = MFCreateMediaType()?;
        media_type.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)?;
        media_type.SetGUID(&MF_MT_SUBTYPE, &MFVideoFormat_RGB32)?;
        
        let stream_index = MF_SOURCE_READER_FIRST_VIDEO_STREAM.0 as u32;
        reader.SetCurrentMediaType(stream_index, None, &media_type).context("Failed to set webcam format to RGB32")?;

        // 5. Get actual frame size
        let actual_type = reader.GetCurrentMediaType(stream_index)?;
        let size_attr = actual_type.GetUINT64(&MF_MT_FRAME_SIZE)?;
        let width = (size_attr >> 32) as u32;
        let height = (size_attr & 0xFFFFFFFF) as u32;
        tracing::info!("Webcam configured for {}x{} RGB32", width, height);

        // 6. Capture loop
        while !stop_flag.load(Ordering::Relaxed) {
            let mut actual_stream_index = 0;
            let mut flags = 0;
            let mut timestamp = 0;
            let mut sample_opt = None;

            if let Err(e) = reader.ReadSample(
                stream_index,
                0,
                Some(&mut actual_stream_index),
                Some(&mut flags),
                Some(&mut timestamp),
                Some(&mut sample_opt)
            ) {
                tracing::warn!("IMFSourceReader ReadSample failed: {}", e);
                break;
            }

            if (flags & MF_SOURCE_READERF_ERROR.0 as u32) != 0 {
                tracing::warn!("Webcam read error flag set");
                break;
            }

            if let Some(sample) = sample_opt {
                let buffer = sample.ConvertToContiguousBuffer()?;
                let mut data_ptr = std::ptr::null_mut();
                let mut current_len = 0;
                let mut max_len = 0;
                
                buffer.Lock(&mut data_ptr, Some(&mut max_len), Some(&mut current_len))?;
                
                if !data_ptr.is_null() && current_len > 0 {
                    let expected_size = (width * height * 4) as usize;
                    let size = current_len as usize;
                    
                    if size >= expected_size {
                        let slice = std::slice::from_raw_parts(data_ptr, expected_size);
                        // MFVideoFormat_RGB32 delivers BGRX — alpha byte is 0. Copying into the
                        // pooled buffer and forcing alpha=255 in the same per-pixel pass (rather
                        // than a full `to_vec()` copy followed by a second full loop over every
                        // chunk) avoids a second complete pass over the frame.
                        let mut pixels = crate::buffer_pool::acquire(expected_size);
                        for (src, dst) in slice.chunks_exact(4).zip(pixels.chunks_exact_mut(4)) {
                            dst[0] = src[0];
                            dst[1] = src[1];
                            dst[2] = src[2];
                            dst[3] = 255;
                        }
                        let raw = RawFrame {
                            source_id: source_id.to_string(),
                            width,
                            height,
                            pixels,
                            // MF timestamp is in 100ns units, same scale as WGC.
                            timestamp_100ns: timestamp,
                        };
                        // We do not care if the receiver is dropped
                        let _ = tx.send(Arc::new(raw));
                    }
                }
                
                buffer.Unlock()?;
            }
        }

        tracing::info!("Webcam capture loop terminating");
        Ok(())
    }
}
