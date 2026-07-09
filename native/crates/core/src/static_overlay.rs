use std::sync::Arc;

use anyhow::{Context, Result};
use base64::Engine;

use crate::capture::RawFrame;

/// Turns an already-rendered static overlay (BGRA pixels, base64-encoded over the wire) into a
/// [`RawFrame`] the compositor can treat exactly like a PiP capture source: positioned and
/// scaled via the same `source_id` in [`streamflow_ipc::StreamSourceDef`], alpha-blended the
/// same way. Unlike a live capture, this only needs to be produced once — the compositor caches
/// the last frame seen per source_id and keeps reusing it every tick.
///
/// Decoding (image files, text rasterization, solid-color fills) happens on the UI side, which
/// already has rich imaging/text APIs — Core just caches whatever pixels arrive.
pub fn decode_as_raw_frame(source_id: &str, width: u32, height: u32, pixels_base64: &str) -> Result<Arc<RawFrame>> {
    let pixels = base64::engine::general_purpose::STANDARD
        .decode(pixels_base64)
        .context("Failed to base64-decode overlay pixels")?;

    let expected_len = (width as usize) * (height as usize) * 4;
    if pixels.len() != expected_len {
        anyhow::bail!(
            "Overlay pixel buffer is {} bytes, expected {width}x{height}x4 = {expected_len}",
            pixels.len()
        );
    }

    Ok(Arc::new(RawFrame {
        source_id: source_id.to_string(),
        width,
        height,
        pixels,
        // Not read by the compositor for PiP-style sources (only the primary's timestamp is
        // used for encoder frame selection) — a static overlay has no meaningful capture time.
        timestamp_100ns: 0,
    }))
}
