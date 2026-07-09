#![allow(unsafe_code)]

//! GPU Gaussian blur for blur-layer overlays, rendered with glium on an offscreen WGL context.
//!
//! The compositor pipeline is CPU-based (`Vec<u8>` BGRA buffers), so each blur is a full
//! upload → two-pass separable Gaussian → readback round trip. The context is anchored to a
//! 1×1 hidden window (WGL needs a real HDC; pbuffers are flakier across drivers), created
//! lazily on the compositor thread the first time a blur layer is actually composited — a
//! GL context must only ever be used from the thread that created it, which the compositor's
//! dedicated thread guarantees.
//!
//! Channel order note: the buffers are BGRA but are uploaded as if RGBA. A Gaussian blur
//! never mixes channels, so the mislabeling round-trips losslessly — same for the vertical
//! flip between our top-down rows and GL's bottom-up textures (the kernel is symmetric).

use std::ffi::{c_void, CString};
use std::num::{NonZeroIsize, NonZeroU32};
use std::rc::Rc;

use anyhow::{anyhow, Context as AnyhowContext, Result};
use glium::backend::{Backend, Context as GliumContext, Facade};
use glium::framebuffer::SimpleFrameBuffer;
use glium::implement_vertex;
use glium::index::{NoIndices, PrimitiveType};
use glium::texture::{ClientFormat, MipmapsOption, RawImage2d, Texture2d, UncompressedFloatFormat};
use glium::uniforms::{MagnifySamplerFilter, MinifySamplerFilter, SamplerWrapFunction};
use glium::{uniform, Program, Surface as GliumSurface, VertexBuffer};
use glutin::config::ConfigTemplateBuilder;
use glutin::context::{ContextAttributesBuilder, NotCurrentGlContext, PossiblyCurrentContext, PossiblyCurrentGlContext};
use glutin::display::{Display, DisplayApiPreference, GlDisplay};
use glutin::surface::{GlSurface, Surface, SurfaceAttributesBuilder, SwapInterval, WindowSurface};
use raw_window_handle::{RawDisplayHandle, RawWindowHandle, Win32WindowHandle, WindowsDisplayHandle};
use windows::core::w;
use windows::Win32::Foundation::HWND;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, RegisterClassW, HMENU, WNDCLASSW, WS_EX_TOOLWINDOW,
    WS_OVERLAPPED,
};

/// Hard cap on the per-pass kernel radius — 2×128+1 taps per pixel per pass is already a lot
/// of texture fetches at 1080p; anything beyond is visually indistinguishable anyway.
const MAX_RADIUS: u32 = 128;

#[derive(Copy, Clone)]
struct QuadVertex {
    position: [f32; 2],
}
implement_vertex!(QuadVertex, position);

const VERTEX_SHADER: &str = r#"
    #version 140
    in vec2 position;
    out vec2 v_uv;
    void main() {
        v_uv = position * 0.5 + 0.5;
        gl_Position = vec4(position, 0.0, 1.0);
    }
"#;

// One shader for both passes; `texel` is (1/w, 0) horizontally and (0, 1/h) vertically.
const BLUR_FRAGMENT_SHADER: &str = r#"
    #version 140
    uniform sampler2D tex;
    uniform vec2 texel;
    uniform int radius;
    in vec2 v_uv;
    out vec4 color;
    void main() {
        float sigma = max(float(radius) * 0.5, 0.5);
        float two_sigma_sq = 2.0 * sigma * sigma;
        vec4 sum = vec4(0.0);
        float weight_sum = 0.0;
        for (int i = -radius; i <= radius; i++) {
            float w = exp(-float(i * i) / two_sigma_sq);
            sum += texture(tex, v_uv + texel * float(i)) * w;
            weight_sum += w;
        }
        color = sum / weight_sum;
    }
"#;

/// glium's `Backend` over the glutin context/surface pair. We never present to the hidden
/// window — all rendering targets offscreen textures — so `swap_buffers` is a no-op.
struct OffscreenBackend {
    display: Display,
    surface: Surface<WindowSurface>,
    context: PossiblyCurrentContext,
}

unsafe impl Backend for OffscreenBackend {
    fn swap_buffers(&self) -> std::result::Result<(), glium::SwapBuffersError> {
        Ok(())
    }

    unsafe fn get_proc_address(&self, symbol: &str) -> *const c_void {
        let symbol = CString::new(symbol).unwrap();
        self.display.get_proc_address(&symbol)
    }

    fn get_framebuffer_dimensions(&self) -> (u32, u32) {
        (1, 1) // The hidden window's surface; never rendered to.
    }

    fn resize(&self, _new_size: (u32, u32)) {}

    fn is_current(&self) -> bool {
        self.context.is_current()
    }

    unsafe fn make_current(&self) {
        let _ = self.context.make_current(&self.surface);
    }
}

struct HeadlessFacade {
    context: Rc<GliumContext>,
}

impl Facade for HeadlessFacade {
    fn get_context(&self) -> &Rc<GliumContext> {
        &self.context
    }
}

pub struct GlBlur {
    facade: HeadlessFacade,
    program: Program,
    quad: VertexBuffer<QuadVertex>,
    /// (source/final, intermediate, width, height) — recreated when the frame size changes.
    textures: Option<(Texture2d, Texture2d, u32, u32)>,
}

impl GlBlur {
    pub fn new() -> Result<Self> {
        let (raw_window, raw_display) = create_hidden_window()?;

        // SAFETY: the handles come from a window we just created and never destroy (it lives
        // for the process's lifetime, like the compositor thread itself).
        let display = unsafe { Display::new(raw_display, DisplayApiPreference::Wgl(Some(raw_window))) }
            .context("WGL display creation failed")?;

        let template = ConfigTemplateBuilder::new().with_alpha_size(8).build();
        let config = unsafe { display.find_configs(template) }
            .context("No WGL configs found")?
            .next()
            .ok_or_else(|| anyhow!("WGL config list was empty"))?;

        let context_attrs = ContextAttributesBuilder::new().build(Some(raw_window));
        let not_current = unsafe { display.create_context(&config, &context_attrs) }
            .context("GL context creation failed")?;

        let one = NonZeroU32::new(1).unwrap();
        let surface_attrs = SurfaceAttributesBuilder::<WindowSurface>::new().build(raw_window, one, one);
        let surface = unsafe { display.create_window_surface(&config, &surface_attrs) }
            .context("GL surface creation failed")?;

        let context = not_current
            .make_current(&surface)
            .context("Failed to make GL context current")?;
        // Don't let the (never-presented) surface throttle anything.
        let _ = surface.set_swap_interval(&context, SwapInterval::DontWait);

        let backend = OffscreenBackend { display, surface, context };
        // SAFETY: the backend's context outlives the glium context (both owned here).
        let glium_context = unsafe {
            GliumContext::new(backend, false, glium::debug::DebugCallbackBehavior::Ignore)
        }
        .context("glium context init failed")?;

        let facade = HeadlessFacade { context: glium_context };

        let program = Program::from_source(&facade, VERTEX_SHADER, BLUR_FRAGMENT_SHADER, None)
            .context("Blur shader compilation failed")?;

        let quad = VertexBuffer::new(
            &facade,
            &[
                QuadVertex { position: [-1.0, -1.0] },
                QuadVertex { position: [1.0, -1.0] },
                QuadVertex { position: [-1.0, 1.0] },
                QuadVertex { position: [1.0, 1.0] },
            ],
        )
        .context("Quad vertex buffer creation failed")?;

        tracing::info!("GL blur pipeline initialized (offscreen WGL context)");

        Ok(Self { facade, program, quad, textures: None })
    }

    /// Blurs the whole BGRA frame in place with a two-pass separable Gaussian.
    pub fn blur_frame(&mut self, pixels: &mut [u8], width: u32, height: u32, radius: u32) -> Result<()> {
        let radius = radius.min(MAX_RADIUS);
        if radius == 0 || width == 0 || height == 0 {
            return Ok(());
        }
        let expected_len = width as usize * height as usize * 4;
        if pixels.len() < expected_len {
            return Err(anyhow!("Frame buffer smaller than {width}x{height}x4"));
        }

        let needs_new = !matches!(&self.textures, Some((_, _, w, h)) if *w == width && *h == height);
        if needs_new {
            let make = || {
                Texture2d::empty_with_format(
                    &self.facade,
                    UncompressedFloatFormat::U8U8U8U8,
                    MipmapsOption::NoMipmap,
                    width,
                    height,
                )
            };
            self.textures = Some((
                make().context("Blur texture A creation failed")?,
                make().context("Blur texture B creation failed")?,
                width,
                height,
            ));
        }
        let (tex_a, tex_b, ..) = self.textures.as_ref().unwrap();

        tex_a.write(
            glium::Rect { left: 0, bottom: 0, width, height },
            RawImage2d {
                data: std::borrow::Cow::Borrowed(&pixels[..expected_len]),
                width,
                height,
                format: ClientFormat::U8U8U8U8,
            },
        );

        let indices = NoIndices(PrimitiveType::TriangleStrip);
        fn sampler(tex: &Texture2d) -> glium::uniforms::Sampler<'_, Texture2d> {
            tex.sampled()
                .wrap_function(SamplerWrapFunction::Clamp)
                .minify_filter(MinifySamplerFilter::Linear)
                .magnify_filter(MagnifySamplerFilter::Linear)
        }

        // Horizontal pass: A → B.
        {
            let mut fb = SimpleFrameBuffer::new(&self.facade, tex_b)
                .context("Blur framebuffer (B) creation failed")?;
            fb.draw(
                &self.quad,
                indices,
                &self.program,
                &uniform! {
                    tex: sampler(tex_a),
                    texel: [1.0f32 / width as f32, 0.0f32],
                    radius: radius as i32,
                },
                &Default::default(),
            )
            .context("Horizontal blur pass failed")?;
        }

        // Vertical pass: B → A.
        {
            let mut fb = SimpleFrameBuffer::new(&self.facade, tex_a)
                .context("Blur framebuffer (A) creation failed")?;
            fb.draw(
                &self.quad,
                indices,
                &self.program,
                &uniform! {
                    tex: sampler(tex_b),
                    texel: [0.0f32, 1.0f32 / height as f32],
                    radius: radius as i32,
                },
                &Default::default(),
            )
            .context("Vertical blur pass failed")?;
        }

        let result: RawImage2d<u8> = tex_a.read();
        pixels[..expected_len].copy_from_slice(&result.data);

        Ok(())
    }
}

// windows 0.61 declares DefWindowProcW as a plain unsafe fn, but WNDCLASSW's callback slot
// wants an extern "system" fn pointer — bridge the calling convention explicitly.
unsafe extern "system" fn def_window_proc(
    hwnd: HWND,
    msg: u32,
    wparam: windows::Win32::Foundation::WPARAM,
    lparam: windows::Win32::Foundation::LPARAM,
) -> windows::Win32::Foundation::LRESULT {
    DefWindowProcW(hwnd, msg, wparam, lparam)
}

/// Creates the invisible 1×1 window whose HDC anchors the WGL context. Registered once;
/// subsequent registrations of the same class name fail harmlessly (we ignore the result).
fn create_hidden_window() -> Result<(RawWindowHandle, RawDisplayHandle)> {
    unsafe {
        let hinstance = GetModuleHandleW(None).context("GetModuleHandleW failed")?;
        let class_name = w!("StreamFlowGlBlurWindow");

        let wc = WNDCLASSW {
            lpfnWndProc: Some(def_window_proc),
            hInstance: hinstance.into(),
            lpszClassName: class_name,
            ..Default::default()
        };
        let _ = RegisterClassW(&wc);

        let hwnd: HWND = CreateWindowExW(
            WS_EX_TOOLWINDOW,
            class_name,
            w!("StreamFlow GL"),
            WS_OVERLAPPED,
            0,
            0,
            1,
            1,
            None,
            Some(HMENU::default()),
            Some(hinstance.into()),
            None,
        )
        .context("CreateWindowExW for GL anchor window failed")?;

        let mut window_handle = Win32WindowHandle::new(
            NonZeroIsize::new(hwnd.0 as isize).ok_or_else(|| anyhow!("HWND was null"))?,
        );
        window_handle.hinstance = NonZeroIsize::new(hinstance.0 as isize);

        Ok((
            RawWindowHandle::Win32(window_handle),
            RawDisplayHandle::Windows(WindowsDisplayHandle::new()),
        ))
    }
}
