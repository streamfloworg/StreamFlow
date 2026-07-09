//! Shared free-list of `Vec<u8>` pixel buffers, reused across capture/composite frames instead
//! of a fresh heap alloc + dealloc every time — at 1080p that's ~8MB per frame, and before the
//! compositor's recompositing was coalesced (see the comment on the compositor's `dirty` flag)
//! this could run at whatever combined rate every capture source produced frames at.
//!
//! A single shared pool (rather than one per call site) is deliberate: capture frames, composited
//! canvases, and PiP-scaled copies are all just `Vec<u8>` of varying sizes, and whichever buffer
//! last released with enough capacity is fair game for the next request regardless of which of
//! those three produced it.

use std::sync::Mutex;
use std::sync::OnceLock;

static POOL: OnceLock<Mutex<Vec<Vec<u8>>>> = OnceLock::new();

/// Cap on how many idle buffers the pool holds onto at once — without this, a burst of frames at
/// many distinct resolutions (e.g. several differently-sized PiP sources) would let the pool grow
/// unboundedly, since every size is a "miss" against every other size already parked here.
const MAX_POOLED_BUFFERS: usize = 16;

/// Takes a zeroed `Vec<u8>` of exactly `len` bytes from the pool if one with enough capacity is
/// free, or allocates a fresh one otherwise. Callers that are about to overwrite every byte
/// anyway (true of every current call site) still get the zero-fill here — safe `resize` can't
/// avoid it — so the saving is strictly the malloc/free churn and resulting memory fragmentation,
/// not the memset itself.
pub fn acquire(len: usize) -> Vec<u8> {
    let mut pool = POOL.get_or_init(|| Mutex::new(Vec::new())).lock().unwrap();
    if let Some(pos) = pool.iter().position(|b| b.capacity() >= len) {
        let mut buf = pool.swap_remove(pos);
        buf.clear();
        buf.resize(len, 0);
        buf
    } else {
        vec![0u8; len]
    }
}

/// Like `acquire`, but skips the zero-fill — the returned buffer's bytes are leftover garbage
/// from whatever it held last. Only sound to use when the caller overwrites every single byte
/// before any read (true of the compositor's base-canvas init, which immediately loops over
/// every 4-byte pixel setting all four channels) — using `acquire` there would zero-fill the
/// whole buffer and then immediately overwrite it all again in that same loop, a fully wasted
/// pass over a multi-MB buffer.
pub fn acquire_uninit(len: usize) -> Vec<u8> {
    let mut pool = POOL.get_or_init(|| Mutex::new(Vec::new())).lock().unwrap();
    if let Some(pos) = pool.iter().position(|b| b.capacity() >= len) {
        let mut buf = pool.swap_remove(pos);
        buf.clear();
        // SAFETY: u8 has no drop glue and no validity invariant beyond "any bit pattern is a
        // valid u8", so extending length without initializing is sound on its own — the actual
        // safety contract this function relies on is documented above: the caller must
        // overwrite every byte before reading any of them.
        unsafe { buf.set_len(len); }
        buf
    } else {
        let mut buf = Vec::with_capacity(len);
        unsafe { buf.set_len(len); }
        buf
    }
}

/// Like `acquire`, but returns an empty (len=0) Vec with at least `min_capacity` already
/// reserved — for call sites that build content up via `push`/`extend_from_slice` rather than
/// writing at fixed indices (e.g. the preview pipe's frame-envelope serialization). Cheaper than
/// `acquire` for that pattern: nothing needs zero-filling since nothing is read before whatever
/// gets pushed/extended onto it.
pub fn acquire_empty(min_capacity: usize) -> Vec<u8> {
    let mut pool = POOL.get_or_init(|| Mutex::new(Vec::new())).lock().unwrap();
    if let Some(pos) = pool.iter().position(|b| b.capacity() >= min_capacity) {
        let mut buf = pool.swap_remove(pos);
        buf.clear();
        buf
    } else {
        Vec::with_capacity(min_capacity)
    }
}

/// Returns a buffer to the pool for reuse — call once nothing still references it (see `impl
/// Drop for RawFrame` in capture.rs, which is the actual trigger point for capture/composited
/// frames since those are shared via `Arc` and may outlive the call site that produced them).
pub fn release(buf: Vec<u8>) {
    let mut pool = POOL.get_or_init(|| Mutex::new(Vec::new())).lock().unwrap();
    if pool.len() < MAX_POOLED_BUFFERS {
        pool.push(buf);
    }
}
