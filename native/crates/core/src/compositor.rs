#![allow(unsafe_code)]

use std::sync::{Arc, Condvar, Mutex};
use std::collections::HashMap;
use std::time::{Duration, Instant};

use streamflow_ipc::{BlurRegionDef, ChromaKeyDef, StreamSourceDef, TransitionKind};
use crate::capture::{RawFrame, ShmOverlay, SharedShmOverlay};
use ffmpeg_sys_next::*;
use windows::Win32::System::Threading::{GetCurrentThread, SetThreadPriority, THREAD_PRIORITY_BELOW_NORMAL};

/// Cached scaler + last scaled output for one layer's source_id — a PiP/overlay, or the primary
/// (which now goes through this same scale-and-place path like any other layer). Static overlays
/// (image/text/color/chat) broadcast the same `Arc<RawFrame>` on every composite tick — see
/// `AddStaticOverlay` in main.rs — so `last_src_ptr` lets us detect "pixels didn't actually
/// change" and skip re-running `sws_scale` on identical input, which otherwise re-scaled a
/// static overlay's full source resolution on every single frame for no reason. Live capture
/// sources get a fresh `Arc::new(..)` per frame, so their pointer always differs and they scale
/// every tick same as before.
struct PipScalerCache {
    ctx: *mut SwsContext,
    src_w: i32,
    src_h: i32,
    dst_w: i32,
    dst_h: i32,
    last_src_ptr: usize,
    /// Part of `scaled`'s cache-invalidation key alongside `last_src_ptr` — a rotation change
    /// must invalidate the cached (already-rotated) buffer even when the source pixels
    /// themselves haven't changed (e.g. a static overlay's rotation is edited live).
    rotation: u16,
    scaled: Vec<u8>,
    // Temporary diagnostics for the still-open CPU-spike investigation: lets us see in the
    // Core Diagnostics panel whether this entry is actually hitting the skip-scale path or
    // still re-scaling every tick (which would mean the cache-key assumption is wrong, or the
    // cost is coming from somewhere else in this per-frame loop entirely).
    total_calls: u64,
    scale_calls: u64,
}

/// One frame's worth of data for the Spout publisher thread to act on — see `SpoutMailbox`'s own
/// doc comment for why this hands off through a mailbox rather than running inline.
struct SpoutPublishMsg {
    enabled: bool,
    primary_frame_known: bool,
    sender_name: String,
    frame: Arc<RawFrame>,
}

/// Single-slot "latest value" handoff from the compositor thread to a dedicated Spout publisher
/// thread. Sending always overwrites whatever's currently sitting in the slot rather than
/// queuing, so the compositor thread's send can never block, and the Spout thread — if it's
/// running behind — only ever skips forward to the newest frame instead of working through a
/// backlog.
///
/// This exists because `SpoutSender::send_bgra` (specifically its `ID3D11DeviceContext::Flush()`
/// call — see spout.rs's own comment on why that's there) is a genuine, synchronous GPU driver
/// call that can stall. It previously ran inline inside `recomposite!()`, on the same single
/// thread that also produces every other consumer's frame (the CPU preview pipe *and* the
/// streaming/recording encoder both subscribe to the same broadcast `tx` this thread sends on) —
/// a stall there didn't just delay Spout, it delayed the *next* recomposite tick entirely,
/// throttling how often fresh frames reached the encoder. Since the encoder's own jitter buffer
/// paces itself by wall-clock time regardless of how often new frames actually arrive (see
/// streaming.rs), the practical effect was the encoder "catching up" through a backlog of
/// composited frames whenever the compositor thread unstuck itself — video content (and anything
/// visibly time-based in it, like a Timer overlay) appearing to play through more real elapsed
/// time than the stream's own declared duration, i.e. looking sped up, with no single dropped
/// frame or PTS discontinuity for any of our own diagnostics to catch. Moving Spout's GPU work to
/// its own thread means a stall there can now only ever affect Spout's own publish cadence.
struct SpoutMailbox {
    slot: Mutex<Option<SpoutPublishMsg>>,
    notify: Condvar,
}

pub struct CompositeFrame {
    pub canvas_width: u32,
    pub canvas_height: u32,
    /// Every source in z-order, primary included — there's no structurally privileged "base"
    /// layer anymore, the primary (if any) is placed/scaled/blended exactly like any other
    /// entry, at whatever position in this list it occupies. `None` pixels means either a blur
    /// layer (see `StreamSourceDef::blur_radius` — acts on the frame composited so far rather
    /// than contributing pixels of its own) or a source that hasn't delivered a frame yet.
    pub layers: Vec<(StreamSourceDef, Option<Arc<RawFrame>>)>,
    /// Timestamp to stamp the composited output with, for the encoder's jitter-buffer temporal-
    /// proximity selection — inherited from the primary's raw frame when one is present in this
    /// composite, else `0` (matching the existing "no meaningful timestamp" convention used by
    /// static/video overlays elsewhere in this crate).
    pub timestamp_100ns: i64,
}

/// Lazily-initialized blur implementation for blur-layer overlays. GL setup can fail (remote
/// desktop, ancient drivers), in which case the SIMD CPU stack blur takes over permanently —
/// same visual intent, no reason to retry a broken GL stack every frame.
pub enum BlurEngine {
    Uninit,
    Gl(crate::gl_blur::GlBlur),
    CpuFallback,
}

impl BlurEngine {
    /// Blurs the `(x, y, w, h)` region of a `fw`×`fh` BGRA frame in place (bounds are clamped
    /// to the frame). The GL path works on its own extracted copy of the region, so edge
    /// pixels clamp at the region border rather than sampling surrounding frame content —
    /// same behavior as the CPU fallback.
    fn blur_region(&mut self, pixels: &mut [u8], fw: i32, fh: i32, x: i32, y: i32, w: i32, h: i32, radius: u32) {
        let x0 = x.clamp(0, fw);
        let y0 = y.clamp(0, fh);
        let x1 = (x + w).clamp(0, fw);
        let y1 = (y + h).clamp(0, fh);
        let (rw, rh) = (x1 - x0, y1 - y0);
        if rw <= 0 || rh <= 0 || radius == 0 {
            return;
        }

        if let BlurEngine::Uninit = self {
            *self = match crate::gl_blur::GlBlur::new() {
                Ok(gl) => BlurEngine::Gl(gl),
                Err(e) => {
                    tracing::warn!("GL blur unavailable, using CPU fallback: {e:#}");
                    BlurEngine::CpuFallback
                }
            };
        }

        match self {
            BlurEngine::Gl(gl) => {
                let mut region_pixels = vec![0u8; (rw * rh * 4) as usize];
                for r in 0..rh {
                    let src = (((y0 + r) * fw + x0) * 4) as usize;
                    let dst = (r * rw * 4) as usize;
                    region_pixels[dst..dst + (rw * 4) as usize]
                        .copy_from_slice(&pixels[src..src + (rw * 4) as usize]);
                }

                match gl.blur_frame(&mut region_pixels, rw as u32, rh as u32, radius) {
                    Ok(()) => {
                        for r in 0..rh {
                            let dst = (((y0 + r) * fw + x0) * 4) as usize;
                            let src = (r * rw * 4) as usize;
                            pixels[dst..dst + (rw * 4) as usize]
                                .copy_from_slice(&region_pixels[src..src + (rw * 4) as usize]);
                        }
                    }
                    Err(e) => {
                        tracing::warn!("GL blur failed, switching to CPU fallback: {e:#}");
                        *self = BlurEngine::CpuFallback;
                        self.blur_region(pixels, fw, fh, x, y, w, h, radius);
                    }
                }
            }
            BlurEngine::CpuFallback => {
                let region = BlurRegionDef { x: x0, y: y0, w: rw, h: rh, radius: radius as i32 };
                apply_blur_region(pixels, fw, fh, &region);
            }
            BlurEngine::Uninit => unreachable!("initialized above"),
        }
    }
}

pub struct CompositorConfig {
    pub sources: Vec<StreamSourceDef>,
    pub blur_regions: Vec<BlurRegionDef>,
    /// Canvas resolution to use when no source is flagged `is_primary`, or the primary hasn't
    /// delivered a frame yet — a live primary frame's own dimensions always take priority over
    /// these once available. See `Command::Config` in the ipc crate.
    pub canvas_width: Option<u32>,
    pub canvas_height: Option<u32>,
    /// Set by the `Config` handler whenever the sent command carries a `transition` — consumed
    /// (taken) by the compositor task the next time it recomposites, which is what actually kicks
    /// off the animation. `None` here just means "no scene-switch transition is pending", not
    /// that one isn't currently playing (see `ActiveTransition` in the compositor task itself).
    pub pending_transition: Option<streamflow_ipc::TransitionDef>,
    /// Set by `Command::SetSpoutOutput` — whether the compositor thread should be maintaining a
    /// live Spout sender at all. See `spout_sender_name` for which name; both are read together
    /// each recomposite, same as `blur_regions`.
    pub spout_enabled: bool,
    /// Sender name to publish under — only meaningful while `spout_enabled`. Changing this while
    /// enabled tears down the old sender and stands up a new one under the new name, rather than
    /// renaming in place (Spout has no rename operation; a name is fixed for a sender's lifetime).
    pub spout_sender_name: String,
}

pub type SharedCompositorConfig = Arc<Mutex<CompositorConfig>>;

/// Coverage (0.0-1.0) for a pixel at (c, r) within a `w`x`h` rect being inside a rounded-rect
/// shape with the given corner `radius` (already clamped to at most half the shorter side by
/// the caller). Pixels away from any corner are always fully covered; within a corner's square,
/// coverage falls off smoothly over roughly the last pixel so the edge isn't hard-aliased.
#[inline]
fn corner_mask(c: i32, r: i32, w: i32, h: i32, radius: i32) -> f32 {
    if radius <= 0 { return 1.0; }

    let (cx, cy) = if c < radius && r < radius {
        (radius, radius)
    } else if c >= w - radius && r < radius {
        (w - radius - 1, radius)
    } else if c < radius && r >= h - radius {
        (radius, h - radius - 1)
    } else if c >= w - radius && r >= h - radius {
        (w - radius - 1, h - radius - 1)
    } else {
        return 1.0; // Not in a corner square at all.
    };

    let dx = (c - cx) as f32;
    let dy = (r - cy) as f32;
    let dist = (dx * dx + dy * dy).sqrt();

    (radius as f32 + 0.5 - dist).clamp(0.0, 1.0)
}

/// Smooth 0.0-1.0 interpolation of `x` between `lo` and `hi` (Hermite ease, not linear) — used
/// below for an anti-aliased chromakey edge instead of a hard on/off cutoff.
#[inline]
fn smoothstep(lo: f32, hi: f32, x: f32) -> f32 {
    if hi <= lo { return if x < lo { 0.0 } else { 1.0 }; }
    let t = ((x - lo) / (hi - lo)).clamp(0.0, 1.0);
    t * t * (3.0 - 2.0 * t)
}

/// Coverage (0.0-1.0) for a pixel's color distance from a chromakey color — 0.0 (fully keyed
/// out) within `key.similarity` of it, ramping up to 1.0 (fully kept) over a fixed softness band
/// above that threshold. Squared RGB distance (no `sqrt`): this runs per-pixel on every keyed
/// layer with no SIMD available in this crate outside of `libblur`'s blur path, so avoiding the
/// sqrt keeps the added per-pixel cost to a couple of multiplies/adds.
#[inline]
fn chroma_mask(sr: u32, sg: u32, sb: u32, key: &ChromaKeyDef) -> f32 {
    let dr = sr as f32 - key.r as f32;
    let dg = sg as f32 - key.g as f32;
    let db = sb as f32 - key.b as f32;
    let dist_sq = (dr * dr + dg * dg + db * db) / (3.0 * 255.0 * 255.0);

    let lo = key.similarity * key.similarity;
    let hi = (key.similarity + 0.12) * (key.similarity + 0.12);
    smoothstep(lo, hi, dist_sq)
}

/// Rotates a BGRA buffer clockwise by an exact multiple of 90 degrees — a lossless pixel
/// permutation, not a resampled transform, since only 0/90/180/270 are ever passed in
/// (`StreamSourceDef::rotation_degrees`). Returns `src` unchanged (cloned) for any other value.
/// 90/270 swap width and height; the caller is expected to have already scaled `src` into
/// whatever footprint produces the correct final `w × h` (or `h × w`) after this call.
fn rotate_pixels(src: &[u8], w: i32, h: i32, degrees: u16) -> Vec<u8> {
    match degrees {
        90 => {
            let mut out = vec![0u8; src.len()];
            let out_w = h;
            for y in 0..h {
                for x in 0..w {
                    let src_idx = ((y * w + x) * 4) as usize;
                    let out_x = h - 1 - y;
                    let out_y = x;
                    let dst_idx = ((out_y * out_w + out_x) * 4) as usize;
                    out[dst_idx..dst_idx + 4].copy_from_slice(&src[src_idx..src_idx + 4]);
                }
            }
            out
        }
        180 => {
            let mut out = vec![0u8; src.len()];
            let count = (w * h) as usize;
            for i in 0..count {
                let src_idx = i * 4;
                let dst_idx = (count - 1 - i) * 4;
                out[dst_idx..dst_idx + 4].copy_from_slice(&src[src_idx..src_idx + 4]);
            }
            out
        }
        270 => {
            let mut out = vec![0u8; src.len()];
            let out_w = h;
            for y in 0..h {
                for x in 0..w {
                    let src_idx = ((y * w + x) * 4) as usize;
                    let out_x = y;
                    let out_y = w - 1 - x;
                    let dst_idx = ((out_y * out_w + out_x) * 4) as usize;
                    out[dst_idx..dst_idx + 4].copy_from_slice(&src[src_idx..src_idx + 4]);
                }
            }
            out
        }
        _ => src.to_vec(),
    }
}

fn bgra_blend(
    src: &[u8], src_w: i32, src_h: i32,
    dst: &mut [u8], dst_w: i32, dst_h: i32,
    offset_x: i32, offset_y: i32,
    corner_radius: i32,
    chromakey: Option<&ChromaKeyDef>,
    opacity: f32,
) {
    let corner_radius = corner_radius.min(src_w / 2).min(src_h / 2);

    for r in 0..src_h {
        let y = offset_y + r;
        if y < 0 || y >= dst_h { continue; }

        for c in 0..src_w {
            let x = offset_x + c;
            if x < 0 || x >= dst_w { continue; }

            let src_idx = ((r * src_w + c) * 4) as usize;
            let dst_idx = ((y * dst_w + x) * 4) as usize;

            // Read up front (not just in the blend branch below) since the chromakey check
            // needs them regardless of which path this pixel takes.
            let sr = src[src_idx + 2] as u32;
            let sg = src[src_idx + 1] as u32;
            let sb = src[src_idx] as u32;

            let mask = corner_mask(c, r, src_w, src_h, corner_radius);
            let key_mask = match chromakey {
                Some(key) => chroma_mask(sr, sg, sb, key),
                None => 1.0,
            };
            let sa = ((src[src_idx + 3] as f32) * mask * key_mask * opacity) as u32;
            if sa == 0 { continue; }

            if sa == 255 {
                dst[dst_idx] = src[src_idx];
                dst[dst_idx + 1] = src[src_idx + 1];
                dst[dst_idx + 2] = src[src_idx + 2];
                dst[dst_idx + 3] = 255;
                continue;
            }

            let dr = dst[dst_idx + 2] as u32;
            let dg = dst[dst_idx + 1] as u32;
            let db = dst[dst_idx] as u32;
            let da = dst[dst_idx + 3] as u32;

            let inv_sa = 255 - sa;

            let out_a = sa + (da * inv_sa) / 255;
            if out_a == 0 { continue; }

            // Standard "over" operator: the source contributes scaled by its OWN alpha (sa), not
            // unconditionally at full strength — using a bare 255 here (as this previously did)
            // means a fractional sa only ever adds a diminishing destination contribution on top
            // of an undiminished source, which for saturated source colors (e.g. solid-white
            // text) clamps straight back to the source's own color regardless of sa. Invisible
            // before now because sa only ever took a narrow fractional range (a ~1px corner/
            // chromakey antialiasing band); Opacity spans the full 0-255 range uniformly across
            // an entire layer, which is what exposed it.
            let out_r = (sr * sa + dr * inv_sa) / 255;
            let out_g = (sg * sa + dg * inv_sa) / 255;
            let out_b = (sb * sa + db * inv_sa) / 255;

            dst[dst_idx] = (out_b.min(255)) as u8;
            dst[dst_idx + 1] = (out_g.min(255)) as u8;
            dst[dst_idx + 2] = (out_r.min(255)) as u8;
            dst[dst_idx + 3] = out_a as u8;
        }
    }
}

// Apply a SIMD-accelerated stack blur (via libblur) to a rectangular region of a
// BGRA frame in-place. Region coordinates are clamped to frame bounds.
fn apply_blur_region(pixels: &mut [u8], fw: i32, fh: i32, region: &BlurRegionDef) {
    use libblur::{AnisotropicRadius, BlurImageMut, BufferStore, FastBlurChannels, ThreadingPolicy};

    let x0 = region.x.max(0);
    let y0 = region.y.max(0);
    let x1 = (region.x + region.w).min(fw);
    let y1 = (region.y + region.h).min(fh);
    let w = x1 - x0;
    let h = y1 - y0;
    if w <= 0 || h <= 0 || region.radius <= 0 { return; }

    // Extract sub-region to a compact flat buffer (row-major BGRA)
    let n = (w * h * 4) as usize;
    let mut sub = vec![0u8; n];
    for y in 0..h {
        for x in 0..w {
            let fi = ((y0 + y) * fw + (x0 + x)) as usize * 4;
            let ri = (y * w + x) as usize * 4;
            sub[ri..ri + 4].copy_from_slice(&pixels[fi..fi + 4]);
        }
    }

    let mut image = BlurImageMut {
        data: BufferStore::Borrowed(&mut sub),
        width: w as u32,
        height: h as u32,
        stride: (w * 4) as u32,
        channels: FastBlurChannels::Channels4,
    };
    let _ = libblur::stack_blur(
        &mut image,
        AnisotropicRadius::new(region.radius as u32),
        ThreadingPolicy::Adaptive,
    );

    // Write blurred sub-region back into the frame
    for y in 0..h {
        for x in 0..w {
            let fi = ((y0 + y) * fw + (x0 + x)) as usize * 4;
            let ri = (y * w + x) as usize * 4;
            pixels[fi..fi + 4].copy_from_slice(&sub[ri..ri + 4]);
        }
    }
}

/// Try to copy a consistent overlay frame from the SHM region via seqlock.
/// Returns `Some((width, height))` if a valid frame was copied into `buf`.
/// `buf` is pre-allocated and reused across frames to avoid per-frame allocation.
unsafe fn read_shm_overlay(shm: &ShmOverlay, buf: &mut Vec<u8>) -> Option<(u32, u32)> {
    use std::sync::atomic::{AtomicU32, Ordering, fence};

    let base = shm.view;
    if base.is_null() || shm.size < 12 {
        return None;
    }

    let gen_ptr = base as *const AtomicU32;

    // Up to 16 attempts: spin through both "write in progress" (odd gen) and
    // torn-copy races (gen changed between read and re-check).
    for _ in 0..16 {
        let gen1 = (*gen_ptr).load(Ordering::Acquire);
        if gen1 == 0 {
            // No frame written yet.
            return None;
        }
        if gen1 & 1 == 1 {
            // Electron write in progress — spin and retry rather than bail.
            // The write window is ~10–50 µs (two koffi memcpy calls); a few
            // spin iterations are enough to outlast it on any modern CPU.
            std::hint::spin_loop();
            continue;
        }

        let ov_w = std::ptr::read_volatile(base.add(4) as *const u32);
        let ov_h = std::ptr::read_volatile(base.add(8) as *const u32);

        if ov_w == 0 || ov_h == 0 {
            return None;
        }

        let px_len = (ov_w as usize).saturating_mul(ov_h as usize).saturating_mul(4);
        if 12 + px_len > shm.size {
            return None;
        }

        // Copy pixels into the reusable buffer.
        buf.resize(px_len, 0);
        std::ptr::copy_nonoverlapping(base.add(12), buf.as_mut_ptr(), px_len);

        // Acquire fence ensures the copy is complete before we re-read gen.
        fence(Ordering::Acquire);
        let gen2 = (*gen_ptr).load(Ordering::Acquire);

        if gen2 == gen1 {
            // Copy was consistent.
            return Some((ov_w, ov_h));
        }
        // gen changed mid-copy (torn read) — retry.
    }

    None
}

pub fn composite_frame(
    raw: &CompositeFrame,
    shm_overlay: &ShmOverlay,
    blur_regions: &[BlurRegionDef],
    pip_scalers: &mut HashMap<String, PipScalerCache>,
    blur_engine: &mut BlurEngine,
    overlay_buf: &mut Vec<u8>,
    good_overlay_buf: &mut Vec<u8>,
    last_overlay_dims: &mut Option<(u32, u32)>,
) -> Arc<RawFrame> {
    let out_w = raw.canvas_width as i32;
    let out_h = raw.canvas_height as i32;

    // Synthesized opaque black base canvas — no longer a clone of any particular source's raw
    // buffer, since there's no structurally privileged "base" layer anymore (see CompositeFrame).
    // acquire_uninit + setting all 4 channels per pixel here is one pass over the buffer, instead
    // of a zero-fill immediately followed by a second full pass just to set alpha.
    let mut out_pixels = crate::buffer_pool::acquire_uninit((out_w as usize) * (out_h as usize) * 4);
    for px in out_pixels.chunks_exact_mut(4) {
        px[0] = 0;
        px[1] = 0;
        px[2] = 0;
        px[3] = 255;
    }

    for (def, pip_pixels) in &raw.layers {
        // A blur layer acts on everything composited before it in z-order, within its own
        // placement rect: the defs already blended into out_pixels get blurred behind it,
        // the ones still to come render sharp on top.
        if def.blur_radius > 0 {
            let bx = (def.x_percent / 100.0 * out_w as f32).round() as i32;
            let by = (def.y_percent / 100.0 * out_h as f32).round() as i32;
            let bw = (def.w_percent / 100.0 * out_w as f32).round() as i32;
            let bh = (def.h_percent / 100.0 * out_h as f32).round() as i32;
            blur_engine.blur_region(&mut out_pixels, out_w, out_h, bx, by, bw, bh, def.blur_radius);
            continue;
        }
        let Some(pip_raw) = pip_pixels else { continue };

        let pw = (def.w_percent / 100.0 * out_w as f32).round() as i32 & !1;
        let ph = (def.h_percent / 100.0 * out_h as f32).round() as i32 & !1;
        let px = (def.x_percent / 100.0 * out_w as f32).round() as i32 & !1;
        let py = (def.y_percent / 100.0 * out_h as f32).round() as i32 & !1;

        if pw <= 0 || ph <= 0 { continue; }

        let src_w = pip_raw.width as i32;
        let src_h = pip_raw.height as i32;
        let src_ptr = Arc::as_ptr(pip_raw) as usize;

        // Scale into the *pre-rotation* footprint — 90/270 swap width and height, since the
        // source is scaled to fill whatever shape it'll have before being rotated the rest of
        // the way into this layer's actual `pw × ph` placement rect (see rotate_pixels below).
        let rotation = def.rotation_degrees;
        let (scale_w, scale_h) = if rotation == 90 || rotation == 270 { (ph, pw) } else { (pw, ph) };

        let cached = pip_scalers.get(&def.source_id);
        let need_new_ctx = match cached {
            Some(c) => c.src_w != src_w || c.src_h != src_h || c.dst_w != scale_w || c.dst_h != scale_h,
            None => true,
        };

        if need_new_ctx {
            if let Some(old) = pip_scalers.remove(&def.source_id) {
                unsafe { sws_freeContext(old.ctx); }
            }
            let ctx = unsafe {
                sws_getContext(
                    src_w, src_h, AVPixelFormat::AV_PIX_FMT_BGRA,
                    scale_w, scale_h, AVPixelFormat::AV_PIX_FMT_BGRA,
                    SwsFlags::SWS_BILINEAR as i32, std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null()
                )
            };
            pip_scalers.insert(def.source_id.clone(), PipScalerCache {
                ctx, src_w, src_h, dst_w: scale_w, dst_h: scale_h,
                last_src_ptr: 0, // forces the fresh-scale path below
                rotation: u16::MAX, // forces the fresh-rotate path below too
                scaled: Vec::new(),
                total_calls: 0,
                scale_calls: 0,
            });
        }

        let entry = pip_scalers.get_mut(&def.source_id).unwrap();
        entry.total_calls += 1;

        // Static overlays (image/text/color/chat) re-broadcast the *same* Arc every tick —
        // their pixels never change between composites, so re-running sws_scale on identical
        // input is pure waste (and was the actual cause of the startup CPU/thermal spikes:
        // cost scales with the overlay's own source resolution, not its on-screen size). Live
        // capture PiPs get a fresh Arc per frame, so this only ever short-circuits for content
        // that's genuinely unchanged. Rotation is folded into the same cache: a rotation-only
        // edit (same pixels, same box) still needs to re-rotate, so it's checked here too rather
        // than re-rotating unconditionally on every tick regardless of whether anything changed.
        if entry.last_src_ptr != src_ptr || entry.rotation != rotation || entry.scaled.len() != (pw * ph * 4) as usize {
            entry.scale_calls += 1;
            let src_data: [*const u8; 4] = [pip_raw.pixels.as_ptr(), std::ptr::null(), std::ptr::null(), std::ptr::null()];
            let src_stride: [i32; 4] = [src_w * 4, 0, 0, 0];
            // sws_scale below writes the full destination buffer, so acquire_uninit is sound —
            // no need to zero-fill something about to be completely overwritten.
            let mut scaled_pip = crate::buffer_pool::acquire_uninit((scale_w * scale_h * 4) as usize);
            let mut dst_data: [*mut u8; 4] = [scaled_pip.as_mut_ptr(), std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null_mut()];
            let dst_stride: [i32; 4] = [scale_w * 4, 0, 0, 0];

            unsafe {
                sws_scale(
                    entry.ctx,
                    src_data.as_ptr(),
                    src_stride.as_ptr(),
                    0, src_h,
                    dst_data.as_mut_ptr(),
                    dst_stride.as_ptr(),
                );
            }

            // Whatever entry.scaled held before (from the last time this cache invalidated) is
            // about to be discarded — release it back to the shared pool instead of just
            // dropping it, same as the freshly-scaled buffer once rotate_pixels has copied out of
            // it (rotate_pixels always returns a new Vec rather than rotating in place).
            let new_scaled = if rotation == 0 {
                scaled_pip
            } else {
                let rotated = rotate_pixels(&scaled_pip, scale_w, scale_h, rotation);
                crate::buffer_pool::release(scaled_pip);
                rotated
            };
            let old_scaled = std::mem::replace(&mut entry.scaled, new_scaled);
            if !old_scaled.is_empty() {
                crate::buffer_pool::release(old_scaled);
            }
            entry.last_src_ptr = src_ptr;
            entry.rotation = rotation;
        }

        // [diag] Periodic hit/miss summary. `tracing::enabled!` short-circuits both this
        // check and the formatting below when nothing's listening at DEBUG (the default is
        // "warn", "info" under --verbose — this never fires unless RUST_LOG=debug is set),
        // so it stays available for a future investigation without costing anything in the
        // render thread's hot path in normal operation (see recomposite!()'s own diag block
        // below for why that mattered: this used to run unconditionally at `warn` level).
        if entry.total_calls % 120 == 0 && tracing::enabled!(tracing::Level::DEBUG) {
            tracing::debug!(
                "[diag] source_id={} composite calls={} sws_scale calls={} ({}x{} -> {}x{})",
                def.source_id, entry.total_calls, entry.scale_calls, src_w, src_h, pw, ph
            );
        }

        let corner_radius_px = (def.corner_radius_percent / 100.0 * (pw.min(ph) as f32 / 2.0)) as i32;
        bgra_blend(&entry.scaled, pw, ph, &mut out_pixels, out_w, out_h, px, py, corner_radius_px, def.chroma_key.as_ref(), def.opacity);
    }

    // Apply blur regions to the composited video frame before overlays are blended in.
    // This blurs the capture (+ PIPs) while keeping overlay elements sharp on top.
    for region in blur_regions {
        apply_blur_region(&mut out_pixels, out_w, out_h, region);
    }

    // Read the overlay from shared memory via seqlock.
    let overlay_dims = unsafe { read_shm_overlay(shm_overlay, overlay_buf) };
    if let Some(dims) = overlay_dims {
        // Consistent read — promote to last-known-good by swapping buffers (O(1), no copy).
        // After swap: good_overlay_buf has fresh pixels; overlay_buf holds stale data
        // that will be overwritten on the next frame's seqlock read.
        std::mem::swap(overlay_buf, good_overlay_buf);
        *last_overlay_dims = Some(dims);
    }

    // Always composite from the last-known-good buffer.
    // On any seqlock-race frame the previous overlay is reused instead of disappearing.
    if let Some((ov_w, ov_h)) = *last_overlay_dims {
        let px_len = ov_w as usize * ov_h as usize * 4;
        if good_overlay_buf.len() >= px_len {
            bgra_blend(&good_overlay_buf[..px_len], ov_w as i32, ov_h as i32, &mut out_pixels, out_w, out_h, 0, 0, 0, None, 1.0);
        }
    }

    Arc::new(RawFrame {
        source_id: "preview".to_string(),
        width: raw.canvas_width,
        height: raw.canvas_height,
        pixels: out_pixels,
        timestamp_100ns: raw.timestamp_100ns,
    })
}

/// A scene-switch transition currently animating. `from` is a snapshot of whatever was actually
/// on screen the instant the switch happened (see `start_compositor`'s use of `last_composited`)
/// — not the outgoing scene's live sources, which have already stopped capturing by this point
/// (see `SceneEditorViewModel.DeactivateSceneAsync` on the C# side).
struct ActiveTransition {
    kind: TransitionKind,
    start: Instant,
    duration: Duration,
    from: Arc<RawFrame>,
}

/// Blends `from` (the outgoing snapshot) toward `to` (the freshly-composited incoming frame) at
/// `progress` (0.0-1.0). Falls back to `to` unchanged if the two frames don't share dimensions —
/// switching to a scene with a differently-sized primary mid-transition isn't worth the
/// complexity of a resizing animation, so it just cuts instead.
fn blend_transition(from: &RawFrame, to: &Arc<RawFrame>, kind: TransitionKind, progress: f32) -> Arc<RawFrame> {
    if from.width != to.width || from.height != to.height {
        return Arc::clone(to);
    }
    let progress = progress.clamp(0.0, 1.0);
    let w = to.width as usize;
    let h = to.height as usize;
    let mut out = vec![0u8; w * h * 4];

    match kind {
        TransitionKind::Cut => return Arc::clone(to),
        TransitionKind::Fade => {
            for i in 0..out.len() {
                let f = from.pixels[i] as f32;
                let t = to.pixels[i] as f32;
                out[i] = (f + (t - f) * progress).round() as u8;
            }
        }
        TransitionKind::SlideLeft | TransitionKind::SlideRight => {
            let offset = (progress * w as f32).round() as usize;
            for y in 0..h {
                let row = y * w * 4;
                let row_out = &mut out[row..row + w * 4];
                match kind {
                    TransitionKind::SlideLeft => {
                        // Old content shifts left off-screen; new content enters from the right.
                        let keep = w - offset;
                        if keep > 0 {
                            row_out[0..keep * 4].copy_from_slice(&from.pixels[row + offset * 4..row + offset * 4 + keep * 4]);
                        }
                        if offset > 0 {
                            row_out[keep * 4..w * 4].copy_from_slice(&to.pixels[row..row + offset * 4]);
                        }
                    }
                    TransitionKind::SlideRight => {
                        // Old content shifts right off-screen; new content enters from the left.
                        if offset > 0 {
                            row_out[0..offset * 4].copy_from_slice(&to.pixels[row + (w - offset) * 4..row + w * 4]);
                        }
                        let keep = w - offset;
                        if keep > 0 {
                            row_out[offset * 4..w * 4].copy_from_slice(&from.pixels[row..row + keep * 4]);
                        }
                    }
                    _ => unreachable!(),
                }
            }
        }
        TransitionKind::SlideUp | TransitionKind::SlideDown => {
            let offset = (progress * h as f32).round() as usize;
            let stride = w * 4;
            match kind {
                TransitionKind::SlideUp => {
                    // Old content shifts up off-screen; new content enters from the bottom.
                    let keep = h - offset;
                    if keep > 0 {
                        out[0..keep * stride].copy_from_slice(&from.pixels[offset * stride..offset * stride + keep * stride]);
                    }
                    if offset > 0 {
                        out[keep * stride..h * stride].copy_from_slice(&to.pixels[0..offset * stride]);
                    }
                }
                TransitionKind::SlideDown => {
                    // Old content shifts down off-screen; new content enters from the top.
                    if offset > 0 {
                        out[0..offset * stride].copy_from_slice(&to.pixels[(h - offset) * stride..h * stride]);
                    }
                    let keep = h - offset;
                    if keep > 0 {
                        out[offset * stride..h * stride].copy_from_slice(&from.pixels[0..keep * stride]);
                    }
                }
                _ => unreachable!(),
            }
        }
    }

    Arc::new(RawFrame {
        source_id: to.source_id.clone(),
        width: to.width,
        height: to.height,
        pixels: out,
        timestamp_100ns: to.timestamp_100ns,
    })
}

pub fn start_compositor(
    mut frame_rx: tokio::sync::broadcast::Receiver<Arc<RawFrame>>,
    shm_overlay: SharedShmOverlay,
    config: SharedCompositorConfig,
    config_notify: Arc<tokio::sync::Notify>,
    spout_evt_tx: tokio::sync::mpsc::UnboundedSender<(u32, u32, u32, i64)>,
) -> tokio::sync::broadcast::Sender<Arc<RawFrame>> {
    let (tx, _rx) = tokio::sync::broadcast::channel(8);
    let out_tx = tx.clone();

    // ── Spout publisher thread ─────────────────────────────────────────────────
    // See SpoutMailbox's own doc comment for why this is a separate thread from the compositor's
    // main recompose loop below, rather than running inline as it used to.
    let spout_mailbox = Arc::new(SpoutMailbox { slot: Mutex::new(None), notify: Condvar::new() });
    {
        let spout_mailbox = Arc::clone(&spout_mailbox);
        std::thread::Builder::new()
            .name("spout-publisher".into())
            .spawn(move || {
                // Deliberately de-prioritized — this thread does real, sustained GPU work every
                // recompose tick (UpdateSubresource + Flush, see SpoutSender::send_bgra's own
                // comment on why Flush is a genuine synchronous GPU call, not just a queued one)
                // purely to feed an *optional* external consumer (OBS, TouchDesigner, etc. via
                // Spout). That's real CPU/GPU load competing with the actual streaming path
                // (encode loop at THREAD_PRIORITY_HIGHEST, rtmp-writer at ABOVE_NORMAL) for the
                // same finite CPU time — under sustained contention, BELOW_NORMAL here means the
                // scheduler favors the stream actually reaching viewers over the local preview
                // feed, rather than treating both as equally important.
                unsafe { let _ = SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_BELOW_NORMAL); }
                // Thread-affinity note (same reasoning as blur_engine/GL in the compositor thread
                // below): the D3D11 device is created and used exclusively from this one thread.
                let mut spout_sender: Option<crate::spout::SpoutSender> = None;
                let mut spout_active_name: Option<String> = None;
                // Last (share_handle, width, height) reported to the C# host via
                // Event::SpoutTextureReady — compared against SpoutSender::texture_info() each
                // publish so the event only fires on an actual create/resize.
                let mut spout_notified_info: Option<(u32, u32, u32, i64)> = None;

                loop {
                    let msg = {
                        let mut slot = spout_mailbox.slot.lock().unwrap();
                        loop {
                            if let Some(msg) = slot.take() { break msg; }
                            slot = spout_mailbox.notify.wait(slot).unwrap();
                        }
                    };

                    if msg.enabled && msg.primary_frame_known {
                        if spout_active_name.as_deref() != Some(msg.sender_name.as_str()) {
                            // First enable, or the configured name changed — Spout has no rename
                            // operation, so drop (deregisters) and stand up fresh.
                            spout_sender = None;
                            match crate::spout::SpoutSender::new(&msg.sender_name) {
                                Ok(sender) => {
                                    spout_sender = Some(sender);
                                    spout_active_name = Some(msg.sender_name.clone());
                                }
                                Err(e) => {
                                    tracing::warn!("Failed to start Spout sender '{}': {e:#}", msg.sender_name);
                                    spout_active_name = None;
                                }
                            }
                        }
                        if let Some(sender) = spout_sender.as_mut() {
                            if let Err(e) = sender.send_bgra(&msg.frame.pixels, msg.frame.width, msg.frame.height) {
                                tracing::warn!("Spout send_bgra failed: {e:#}");
                            }
                            let info = sender.texture_info();
                            if info.is_some() && info != spout_notified_info {
                                if let Some((share_handle, width, height, adapter_luid)) = info {
                                    tracing::info!(
                                        "[diag] Spout texture ready: share_handle=0x{share_handle:08X} width={width} height={height} adapter_luid={adapter_luid}"
                                    );
                                    let _ = spout_evt_tx.send((share_handle, width, height, adapter_luid));
                                }
                                spout_notified_info = info;
                            }
                        }
                    } else if spout_sender.is_some() {
                        spout_sender = None; // Drop deregisters the sender name.
                        spout_active_name = None;
                        spout_notified_info = None;
                    }
                }
            })
            .expect("failed to spawn spout-publisher thread");
    }

    std::thread::spawn(move || {
        let mut latest_pips: HashMap<String, Arc<RawFrame>> = HashMap::new();
        let mut latest_primary: Option<Arc<RawFrame>> = None;
        let mut pip_scalers: HashMap<String, PipScalerCache> = HashMap::new();
        // Reusable buffers for seqlock overlay copies — allocated once, avoids per-frame 8 MB allocs.
        // overlay_buf: staging target for each seqlock read attempt.
        // good_overlay_buf: last confirmed-consistent frame; used as fallback on race failures.
        let mut overlay_buf: Vec<u8> = Vec::with_capacity(1920 * 1080 * 4);
        let mut good_overlay_buf: Vec<u8> = Vec::with_capacity(1920 * 1080 * 4);
        let mut last_overlay_dims: Option<(u32, u32)> = None;
        // Created lazily on first use — this thread is the only one that ever touches the GL
        // context, which is exactly what GL's thread-affinity rules require.
        let mut blur_engine = BlurEngine::Uninit;

        // [diag] Temporary — how often the whole compositor is actually re-running per
        // second. If this is pegged near the display refresh rate even with an idle desktop,
        // something (the app's own preview window on the captured monitor? a config-notify
        // storm?) is forcing continuous recomposites rather than WGC's normal
        // only-on-visible-change behavior.
        let mut recomposite_count: u64 = 0;
        let mut recomposite_last_log = std::time::Instant::now();
        let mut process_stats_sampler = crate::process_stats::ProcessStatsSampler::new();

        let mut rt = tokio::runtime::Runtime::new().unwrap();
        rt.block_on(async move {
            // The last frame actually sent downstream (whether a plain composite or a blended
            // transition frame) — doubles as the "from" snapshot for the *next* scene switch, so
            // a rapid second switch mid-transition crossfades from what's currently visible
            // rather than a stale pre-blend frame. `None` only until the very first recomposite.
            let mut last_composited: Option<Arc<RawFrame>> = None;
            let mut transition: Option<ActiveTransition> = None;
            // Coalescing clock: frame arrivals and config changes just mark `dirty` and return
            // immediately; this tick is the only thing that actually calls `recomposite!()`.
            // Caps recompositing at ~62.5Hz regardless of how many events land in a given
            // window (WGC + N PiPs + config churn can otherwise easily exceed that combined),
            // comfortably above any realistic stream/preview target fps. Also drives transitions
            // forward between real frame arrivals — a static overlay-only scene wouldn't
            // otherwise recomposite on a timer. reset() whenever a new transition starts so its
            // first tick lands a full interval later rather than firing immediately on stale
            // elapsed time.
            let mut dirty = false;
            let mut recompute_interval = tokio::time::interval(Duration::from_millis(16));
            recompute_interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);

            // WGC only delivers a new primary frame when the captured surface visibly changes,
            // so a Config change (removing a PiP/overlay, or registering/updating one) or a
            // freshly-arrived PiP/overlay frame otherwise wouldn't show up in the composited
            // output until the primary source happened to change next — which, for an idle
            // capture target, might be a long time or never. Recompositing here from whatever
            // primary frame is already cached makes those changes visible immediately.
            //
            // This used to run inline on every single frame_rx arrival and config_notify —
            // meaning a full canvas alloc + per-layer scale/blend/chromakey pass ran once per
            // event, at whatever combined rate all capture sources produced frames (WGC fires
            // on any visible change, potentially at full display refresh; each PiP/webcam adds
            // its own independent rate on top). Nothing downstream ever consumed faster than
            // ~15fps (preview pipe) or the stream's own target fps (paced separately by
            // streaming.rs's own jitter buffer), so most of that work was thrown away before it
            // left this thread. It's now only invoked from the coalescing tick below.
            macro_rules! recomposite {
                () => {
                    // Locked once per call and held through every field this pass needs (canvas
                    // fallback dims, sources, blur_regions) rather than re-acquiring it two or
                    // three separate times over the course of one recomposite.
                    let cfg = config.lock().unwrap();

                    // A live primary frame's own dimensions are always authoritative once one
                    // exists; otherwise fall back to whatever canvas resolution was explicitly
                    // configured (manual or pre-selected-device) for a primary-less scene. If
                    // neither is known yet, there's genuinely nothing to render at any size.
                    let canvas_dims: Option<(u32, u32)> = match &latest_primary {
                        Some(p) => Some((p.width, p.height)),
                        None => match (cfg.canvas_width, cfg.canvas_height) {
                            (Some(w), Some(h)) => Some((w, h)),
                            _ => None,
                        },
                    };

                    if let Some((canvas_w, canvas_h)) = canvas_dims {
                        recomposite_count += 1;
                        if recomposite_last_log.elapsed().as_secs() >= 2 {
                            // This used to run unconditionally at `warn` level (so it showed up
                            // without --verbose) — a synchronous process-stats sample plus a
                            // formatted stderr write, both on the render thread, every ~2s of
                            // wall time regardless of whether anyone was watching. That's a
                            // plausible source of a periodic stutter that wouldn't show up as a
                            // dropped frame (the recomposite still completes, just late). Gated
                            // behind `enabled!` now so it costs nothing unless RUST_LOG=debug.
                            if tracing::enabled!(tracing::Level::DEBUG) {
                                let stats = process_stats_sampler.sample();
                                tracing::debug!(
                                    "[diag] compositor recomposite rate: {:.1}/sec ({} total) — \
                                     process cpu={:.1}% working_set={:.1}MB \
                                     pip_scalers={} latest_pips={}",
                                    recomposite_count as f64 / recomposite_last_log.elapsed().as_secs_f64(),
                                    recomposite_count,
                                    stats.cpu_percent,
                                    stats.working_set_mb,
                                    pip_scalers.len(),
                                    latest_pips.len(),
                                );
                            }
                            recomposite_count = 0;
                            recomposite_last_log = std::time::Instant::now();
                        }
                        let all_defs: Vec<StreamSourceDef> = cfg.sources.clone();

                        // Prune cached state for sources no longer in the config. Every
                        // overlay/PiP gets a unique GUID-based source_id, so without this,
                        // every one ever created — even long since removed from the scene —
                        // stays permanently cached here: its last frame buffer (several MB for
                        // a 1080p+ source) and, worse, its native FFmpeg SwsContext, which is
                        // unmanaged memory that leaks outright unless explicitly freed. Over a
                        // session with any real amount of add/remove testing this is an
                        // unbounded leak, not a slow one. Covers every non-blur source_id,
                        // primary included (its own frame lives in latest_primary, not
                        // latest_pips, so retaining its id against latest_pips is a harmless
                        // no-op — this only ever actually prunes non-primary ids there).
                        let live_ids: std::collections::HashSet<&str> = all_defs.iter()
                            .filter(|d| d.blur_radius == 0)
                            .map(|d| d.source_id.as_str())
                            .collect();
                        latest_pips.retain(|sid, _| live_ids.contains(sid.as_str()));
                        pip_scalers.retain(|sid, entry| {
                            let keep = live_ids.contains(sid.as_str());
                            if !keep {
                                unsafe { sws_freeContext(entry.ctx); }
                            }
                            keep
                        });

                        let mut layers = Vec::new();
                        for def in &all_defs {
                            if def.blur_radius > 0 {
                                // Blur layers carry no pixels of their own; their position in
                                // this list is what matters (blur everything before, spare
                                // everything after).
                                layers.push((def.clone(), None));
                            } else if def.is_primary {
                                if let Some(primary_frame) = &latest_primary {
                                    layers.push((def.clone(), Some(Arc::clone(primary_frame))));
                                }
                                // Else: configured as primary but hasn't delivered a frame yet —
                                // skip, same convention as an as-yet-frameless PiP below.
                            } else if let Some(pip_frame) = latest_pips.get(&def.source_id) {
                                layers.push((def.clone(), Some(Arc::clone(pip_frame))));
                            }
                            // Else: registered but no frame has arrived yet for this source_id —
                            // same as an as-yet-frameless PiP above, not an error.
                        }

                        let timestamp_100ns = latest_primary.as_ref().map(|p| p.timestamp_100ns).unwrap_or(0);
                        let composite = CompositeFrame {
                            canvas_width: canvas_w,
                            canvas_height: canvas_h,
                            layers,
                            timestamp_100ns,
                        };

                        let blur = cfg.blur_regions.clone();
                        let spout_enabled = cfg.spout_enabled;
                        let spout_sender_name = cfg.spout_sender_name.clone();
                        // Whether this scene has a primary source configured at all, regardless
                        // of whether it's delivered its first frame yet — see the Spout-publish
                        // gate below for why this matters.
                        let has_configured_primary = cfg.sources.iter().any(|s| s.is_primary);
                        drop(cfg);
                        let out_frame = composite_frame(&composite, &shm_overlay, &blur, &mut pip_scalers, &mut blur_engine, &mut overlay_buf, &mut good_overlay_buf, &mut last_overlay_dims);

                        let final_frame = match &transition {
                            Some(t) => {
                                let elapsed = t.start.elapsed();
                                if elapsed >= t.duration {
                                    transition = None;
                                    out_frame
                                } else {
                                    let progress = elapsed.as_secs_f32() / t.duration.as_secs_f32();
                                    blend_transition(&t.from, &out_frame, t.kind, progress)
                                }
                            }
                            None => out_frame,
                        };
                        last_composited = Some(Arc::clone(&final_frame));
                        // Preview/streaming get the frame first, unconditionally. Spout's actual
                        // GPU publish now happens entirely on its own thread (see SpoutMailbox's
                        // doc comment) — handing it off here is just an Arc clone + a mutex swap,
                        // never a blocking GPU call, so it can never delay this thread reaching
                        // the next recomposite tick the way the old inline call could.
                        let _ = tx.send(Arc::clone(&final_frame));

                        // Skip Spout entirely while a *configured* primary hasn't delivered its
                        // first frame yet — canvas_dims is a transient fallback in that window
                        // (see its own comment above), and creating/publishing a texture at that
                        // throwaway size just to immediately replace it once the real frame
                        // arrives creates a race: a receiver (including our own C# "Show
                        // Preview" path) that's slow to open the first handle can end up trying
                        // to open a texture the Rust side has already destroyed and recreated at
                        // the real size, which fails with a generic, hard-to-diagnose
                        // E_INVALIDARG rather than anything pointing at what actually happened.
                        // A scene with no primary configured at all isn't affected — canvas_dims
                        // there is the real, stable answer from the start, not a placeholder.
                        let primary_frame_known = latest_primary.is_some() || !has_configured_primary;
                        {
                            let mut slot = spout_mailbox.slot.lock().unwrap();
                            *slot = Some(SpoutPublishMsg {
                                enabled: spout_enabled,
                                primary_frame_known,
                                sender_name: spout_sender_name,
                                frame: Arc::clone(&final_frame),
                            });
                        }
                        spout_mailbox.notify.notify_one();
                    }
                };
            }

            loop {
                tokio::select! {
                    result = frame_rx.recv() => {
                        match result {
                            Ok(frame) => {
                                let primary_source_id = config.lock().unwrap().sources.iter().find(|s| s.is_primary).map(|s| s.source_id.clone());
                                let sid = frame.source_id.clone();
                                if Some(sid.clone()) == primary_source_id {
                                    latest_primary = Some(frame);
                                } else {
                                    latest_pips.insert(sid, frame);
                                }
                                dirty = true;
                            }
                            Err(tokio::sync::broadcast::error::RecvError::Lagged(n)) => {
                                tracing::trace!("compositor missed {n} broadcast frames (lagged)");
                            }
                            Err(tokio::sync::broadcast::error::RecvError::Closed) => {
                                break;
                            }
                        }
                    }
                    _ = config_notify.notified() => {
                        let pending = config.lock().unwrap().pending_transition.take();
                        if let Some(t) = pending {
                            if t.kind != streamflow_ipc::TransitionKind::Cut {
                                if let Some(from) = last_composited.clone() {
                                    transition = Some(ActiveTransition {
                                        kind: t.kind,
                                        start: Instant::now(),
                                        duration: Duration::from_millis(t.duration_ms as u64),
                                        from,
                                    });
                                    recompute_interval.reset();
                                }
                                // Else: nothing composited yet to transition from (first-ever
                                // Config) — falls through to the plain instant-cut recomposite.
                            }
                        }
                        // warn! so this shows in the C# Core Diagnostics panel by default (see
                        // the matching Command::Config log in main.rs) — confirms the compositor
                        // thread actually woke up and will recomposite on the next 16ms tick,
                        // as opposed to the notify getting lost or this thread being stuck.
                        tracing::warn!("[diag] Compositor woke on config change, will recomposite next tick");
                        dirty = true;
                    }
                    _ = recompute_interval.tick(), if dirty || transition.is_some() => {
                        recomposite!();
                        dirty = false;
                    }
                }
            }
        });
    });

    out_tx
}
