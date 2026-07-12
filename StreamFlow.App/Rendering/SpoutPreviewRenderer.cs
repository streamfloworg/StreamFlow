using System.Windows;
using System.Windows.Interop;

using Vortice.Direct3D9;

namespace StreamFlow.App.Rendering;

/// <summary>
/// Opens the Rust core's Spout output texture directly into a <see cref="D3DImage"/> for the
/// "Show Preview" GPU-backed preview path (Option B of the Spout2 Integration Plan) — bypasses
/// the CPU pipe+WriteableBitmap path entirely for the primary/composited preview while active.
/// <see cref="D3DImage"/> only accepts D3D9Ex surfaces, so this owns a minimal D3D9Ex device
/// purely for opening the D3D11-created shared handle (legacy DXGI shared handles bridge
/// D3D9Ex/D3D11 directly — no DuplicateHandle or cross-API pixel copy needed, see the Rust-side
/// doc comment on Event::SpoutTextureReady). Nothing is ever actually presented through this
/// device; it exists solely to hold the opened texture/surface.
/// </summary>
public sealed class SpoutPreviewRenderer : IDisposable
{
    private IDirect3D9Ex? _d3d;
    private IDirect3DDevice9Ex? _device;
    private IDirect3DTexture9? _texture;
    private IDirect3DSurface9? _surface;
    private uint _width;
    private uint _height;

    public D3DImage Image { get; } = new();

    /// <summary>Raised whenever a D3D/D3DImage call throws — see <see cref="UpdateTexture"/> and
    /// <see cref="MarkDirty"/>'s own catch blocks, which previously swallowed these silently
    /// (the actual root cause of Option B appearing to do nothing at all instead of visibly
    /// failing). The caller decides how to surface it (e.g. the existing error banner).</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>(Re)opens the shared texture at the given handle/dimensions — call whenever
    /// Event::SpoutTextureReady arrives (first enable, or a resolution change). Safe to call
    /// repeatedly; a device-lost/driver failure just tears everything down and waits for the
    /// next call to rebuild from scratch rather than propagating the exception.</summary>
    public void UpdateTexture(nint windowHandle, uint shareHandle, uint width, uint height, long adapterLuid)
    {
        // Each step wrapped separately (rather than one broad try/catch) so a failure's message
        // says which specific D3D call rejected it — the previous single-try version only ever
        // reported the same generic HRESULT regardless of whether device creation, opening the
        // shared handle, or the D3DImage backbuffer assignment was the actual failure point.
        string step = "EnsureDevice";
        try
        {
            EnsureDevice(windowHandle, adapterLuid);
            if (_device is null)
            {
                Failed?.Invoke(this, new InvalidOperationException("D3D9Ex device creation returned no device (no exception thrown)"));
                return;
            }

            ReleaseTexture();

            step = "CreateTexture(open shared handle)";
            var handle = (nint)shareHandle;
            _texture = _device.CreateTexture(width, height, 1, Usage.RenderTarget,
                Format.A8R8G8B8, Pool.Default, ref handle);

            step = "GetSurfaceLevel";
            _surface = _texture.GetSurfaceLevel(0);
            _width = width;
            _height = height;

            step = "D3DImage.SetBackBuffer";
            Image.Lock();
            Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface.NativePointer);
            Image.Unlock();
        }
        catch (Exception ex)
        {
            // Device-lost / driver-level failure — tear down; the next SpoutTextureReady
            // (which the Rust core keeps sending on every resolution change) rebuilds everything.
            ReleaseDevice();
            Failed?.Invoke(this, new InvalidOperationException(
                $"[{step}] shareHandle=0x{shareHandle:X8} width={width} height={height} adapterLuid={adapterLuid}: {ex.Message}", ex));
        }
    }

    /// <summary>Marks the current backbuffer dirty so WPF re-reads the shared surface's latest
    /// content — call every <c>CompositionTarget.Rendering</c> tick while active. The underlying
    /// texture's pixels are updated asynchronously by the Rust core (a plain GPU write into the
    /// same shared memory), so this is a poll at display refresh rate, not a push notification —
    /// matching how <see cref="D3DImage"/> is designed to be driven for externally-updated D3D
    /// content.</summary>
    public void MarkDirty()
    {
        if (_surface is null) return;
        try
        {
            Image.Lock();
            Image.AddDirtyRect(new Int32Rect(0, 0, (int)_width, (int)_height));
            Image.Unlock();
        }
        catch (Exception ex)
        {
            ReleaseDevice();
            Failed?.Invoke(this, ex);
        }
    }

    private void EnsureDevice(nint windowHandle, long adapterLuid)
    {
        if (_device is not null) return;

        _d3d = D3D9.Direct3DCreate9Ex();

        // D3D9's and DXGI's adapter enumeration don't have to agree on which one is "default" —
        // a real, documented failure mode on hybrid-graphics/multi-GPU machines where opening a
        // shared handle from a mismatched adapter fails with E_INVALIDARG. Find the D3D9 adapter
        // whose LUID matches the Rust core's D3D11 device instead of assuming adapter 0 is the
        // same physical GPU; falls back to 0 if no match is found (e.g. LUID resolution failed
        // core-side) or adapterLuid is 0.
        var adapter = 0u;
        if (adapterLuid != 0)
        {
            var count = _d3d.AdapterCount;
            for (var i = 0u; i < count; i++)
            {
                if (_d3d.GetAdapterLuid(i) == adapterLuid)
                {
                    adapter = i;
                    break;
                }
            }
        }

        // Minimal 1x1 dummy swapchain — this device never actually presents anything, it exists
        // solely to open the shared texture handle and hand its surface to D3DImage.
        var pp = new PresentParameters
        {
            Windowed = true,
            SwapEffect = SwapEffect.Discard,
            BackBufferFormat = Format.Unknown,
            BackBufferCount = 1,
            BackBufferWidth = 1,
            BackBufferHeight = 1,
            DeviceWindowHandle = windowHandle,
            PresentationInterval = PresentInterval.Default,
        };

        // Deliberately the `params PresentParameters[]` overload, not the one taking an explicit
        // DisplayModeEx — D3D9Ex requires that fullscreen-display-mode argument to be a true null
        // whenever Windowed=true, and a by-value `default(DisplayModeEx)` struct isn't the same
        // thing at the native marshaling level (it's a valid, non-null, all-zero struct, which
        // D3D9Ex rejects with D3DERR_INVALIDCALL). This overload never takes that parameter at
        // all, so there's nothing to get wrong.
        _device = _d3d.CreateDeviceEx(adapter, DeviceType.Hardware, windowHandle,
            CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
            pp);
    }

    private void ReleaseTexture()
    {
        _surface?.Dispose();
        _surface = null;
        _texture?.Dispose();
        _texture = null;
    }

    private void ReleaseDevice()
    {
        ReleaseTexture();
        _device?.Dispose();
        _device = null;
        _d3d?.Dispose();
        _d3d = null;
    }

    public void Dispose() => ReleaseDevice();
}
