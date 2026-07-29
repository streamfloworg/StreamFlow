using StreamFlow.App.Rendering;
using StreamFlow.App.ViewModels.Pages;
using Xunit;

namespace StreamFlow.Tests;

/// <summary>Regression coverage for the preview/stream text-size mismatch on nested overlays
/// (Stream Alert sub-layers being the common case): OverlayContentRenderer.RenderTextToBgra used
/// to size its render target from the slot's raw WPercent/HPercent, which for a slot nested inside
/// a Group/Alert is a percentage of the *parent's* box, not the full canvas (see
/// SourceSlot.RenderWPercent's doc comment). Multiplying that local percent against the full
/// canvas width inflated the render target far past the slot's true absolute on-screen size, so
/// the compositor's later downscale to the slot's actual (small) box shrank the glyphs along with
/// it — text streamed noticeably smaller than the WPF editor's own native TextBlock preview.</summary>
public class OverlayContentRendererTests
{
    [Fact]
    public void RenderTextToBgra_NestedSlot_SizesTargetByResolvedAbsolutePercent()
    {
        var parent = new SourceSlot(isPrimary: false, x: 10, y: 10, w: 30, h: 20, isOverlay: true)
        {
            CanvasWidth = 640,
            CanvasHeight = 360,
        };
        var child = new SourceSlot(isPrimary: false, x: 10, y: 10, w: 80, h: 80, isOverlay: true, content: new TextOverlayContent())
        {
            CanvasWidth = 640,
            CanvasHeight = 360,
            ParentGroup = parent,
        };

        // Small font + short text keeps the natural glyph width well under the target width, so
        // the render target's own width governs the bitmap size (what this test is checking).
        var style = new TextStyle { FontSize = 8 };
        var rendered = OverlayContentRenderer.RenderTextToBgra("Hi", style, child);
        Assert.NotNull(rendered);
        var (width, _, _) = rendered!.Value;

        const double textSupersampleFactor = 4.0; // mirrors OverlayContentRenderer's private constant

        // Correct: derived from RenderWPercent (24 = 80% of the parent's 30%, i.e. the slot's
        // true absolute on-screen size), not WPercent (80, local to the parent).
        var expectedWidth = (int)Math.Ceiling(child.RenderWPercent / 100.0 * child.CanvasWidth * textSupersampleFactor);
        var buggyWidth = (int)Math.Ceiling(child.WPercent / 100.0 * child.CanvasWidth * textSupersampleFactor);

        Assert.NotEqual(buggyWidth, width);
        Assert.Equal(expectedWidth, width);
    }

    [Fact]
    public void RenderTextToBgra_TopLevelSlot_LocalAndAbsolutePercentAreTheSame()
    {
        // A top-level slot has no ParentGroup, so RenderWPercent/RenderHPercent already equal
        // WPercent/HPercent — this just documents that the nested-slot fix is a no-op here.
        var slot = new SourceSlot(isPrimary: false, x: 10, y: 10, w: 24, h: 16, isOverlay: true, content: new TextOverlayContent())
        {
            CanvasWidth = 640,
            CanvasHeight = 360,
        };

        Assert.Equal(slot.WPercent, slot.RenderWPercent);
        Assert.Equal(slot.HPercent, slot.RenderHPercent);
    }
}
