use anyhow::{Context, Result};
use windows::core::Interface;
use windows::Win32::Graphics::Direct2D::Common::{
    D2D1_ALPHA_MODE_PREMULTIPLIED, D2D1_PIXEL_FORMAT, D2D_RECT_F, D2D_SIZE_U,
};
use windows::Win32::Graphics::Direct2D::{
    D2D1CreateFactory, ID2D1Bitmap1, ID2D1DeviceContext, ID2D1Factory1,
    D2D1_BITMAP_OPTIONS_CANNOT_DRAW, D2D1_BITMAP_OPTIONS_TARGET, D2D1_BITMAP_PROPERTIES1,
    D2D1_DEVICE_CONTEXT_OPTIONS_NONE, D2D1_FACTORY_TYPE_MULTI_THREADED,
    D2D1_INTERPOLATION_MODE_LINEAR,
};
use windows::Win32::Graphics::Direct3D11::{
    ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D, D3D11_BIND_RENDER_TARGET,
    D3D11_BIND_SHADER_RESOURCE, D3D11_CPU_ACCESS_FLAG, D3D11_RESOURCE_MISC_FLAG,
    D3D11_TEXTURE2D_DESC, D3D11_USAGE_DEFAULT,
};
use windows::Win32::Graphics::Dxgi::Common::{DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_SAMPLE_DESC};
use windows::Win32::Graphics::Dxgi::{IDXGIDevice, IDXGISurface};

pub struct D2DCompositor {
    d2d_context: ID2D1DeviceContext,
}

unsafe impl Send for D2DCompositor {}
unsafe impl Sync for D2DCompositor {}

impl D2DCompositor {
    pub fn new(d3d_device: &ID3D11Device) -> Result<Self> {
        unsafe {
            let factory: ID2D1Factory1 =
                D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED, None)?;
            let dxgi_device: IDXGIDevice = d3d_device.cast()?;
            let d2d_device = factory.CreateDevice(&dxgi_device)?;
            let d2d_context =
                d2d_device.CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE)?;
            Ok(Self { d2d_context })
        }
    }

    /// Upload a BGRA (premultiplied alpha) pixel buffer as a cached D2D bitmap.
    /// Only called when the overlay content changes — cache the result in GpuState.
    pub fn create_bitmap_from_bgra(
        &self,
        bgra: &[u8],
        width: u32,
        height: u32,
    ) -> Result<ID2D1Bitmap1> {
        unsafe {
            let props = D2D1_BITMAP_PROPERTIES1 {
                pixelFormat: D2D1_PIXEL_FORMAT {
                    format: DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode: D2D1_ALPHA_MODE_PREMULTIPLIED,
                },
                dpiX: 96.0,
                dpiY: 96.0,
                ..Default::default()
            };
            self.d2d_context
                .CreateBitmap(
                    D2D_SIZE_U { width, height },
                    Some(bgra.as_ptr() as _),
                    width * 4,
                    &props,
                )
                .context("D2D CreateBitmap from BGRA failed")
        }
    }

    /// Composite `overlay_bmp` over `main_tex`, writing the result into
    /// `render_target` (must have D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE).
    pub fn composite(
        &self,
        d3d_context: &ID3D11DeviceContext,
        main_tex: &ID3D11Texture2D,
        overlay_bmp: &ID2D1Bitmap1,
        render_target: &ID3D11Texture2D,
    ) -> Result<()> {
        unsafe {
            // Blit the main capture into the render target so D2D draws on top.
            d3d_context.CopyResource(render_target, main_tex);

            // Wrap the render target as a D2D bitmap (draw-target only).
            let rt_surface: IDXGISurface = render_target.cast()?;
            let rt_props = D2D1_BITMAP_PROPERTIES1 {
                pixelFormat: D2D1_PIXEL_FORMAT {
                    format: DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode: D2D1_ALPHA_MODE_PREMULTIPLIED,
                },
                bitmapOptions: D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
                ..Default::default()
            };
            let rt_bmp = self
                .d2d_context
                .CreateBitmapFromDxgiSurface(&rt_surface, Some(&rt_props))?;

            // Scale overlay to fill the full render target.
            let mut rt_desc = D3D11_TEXTURE2D_DESC::default();
            render_target.GetDesc(&mut rt_desc);
            let dest_rect = D2D_RECT_F {
                left: 0.0,
                top: 0.0,
                right: rt_desc.Width as f32,
                bottom: rt_desc.Height as f32,
            };

            self.d2d_context.SetTarget(&rt_bmp);
            self.d2d_context.BeginDraw();
            self.d2d_context.DrawBitmap(
                overlay_bmp,
                Some(&dest_rect),
                1.0,
                D2D1_INTERPOLATION_MODE_LINEAR,
                None,
                None,
            );
            self.d2d_context.EndDraw(None, None)?;
            self.d2d_context.SetTarget(None);

            Ok(())
        }
    }
}

pub fn create_render_target(
    d3d_device: &ID3D11Device,
    width: u32,
    height: u32,
) -> Result<ID3D11Texture2D> {
    let desc = D3D11_TEXTURE2D_DESC {
        Width: width,
        Height: height,
        MipLevels: 1,
        ArraySize: 1,
        Format: DXGI_FORMAT_B8G8R8A8_UNORM,
        SampleDesc: DXGI_SAMPLE_DESC { Count: 1, Quality: 0 },
        Usage: D3D11_USAGE_DEFAULT,
        BindFlags: (D3D11_BIND_RENDER_TARGET.0 | D3D11_BIND_SHADER_RESOURCE.0) as u32,
        CPUAccessFlags: D3D11_CPU_ACCESS_FLAG(0).0 as u32,
        MiscFlags: D3D11_RESOURCE_MISC_FLAG(0).0 as u32,
    };
    let mut out = None;
    unsafe {
        d3d_device
            .CreateTexture2D(&desc, None, Some(&mut out))
            .context("Failed to create D2D render target texture")?;
    }
    out.context("Render target was None")
}
