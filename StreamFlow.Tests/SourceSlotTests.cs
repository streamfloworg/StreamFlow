using StreamFlow.App.Helpers.Behaviors;
using StreamFlow.App.ViewModels.Pages;
using Xunit;

namespace StreamFlow.Tests;

public class SourceSlotTests
{
    [Theory]
    [InlineData(SlotResizeDirection.TopLeft)]
    [InlineData(SlotResizeDirection.Top)]
    [InlineData(SlotResizeDirection.TopRight)]
    [InlineData(SlotResizeDirection.Right)]
    [InlineData(SlotResizeDirection.BottomRight)]
    [InlineData(SlotResizeDirection.Bottom)]
    [InlineData(SlotResizeDirection.BottomLeft)]
    [InlineData(SlotResizeDirection.Left)]
    public void ResizeByHandleDelta_AllDirections_ResizesSlotWithinBounds(SlotResizeDirection direction)
    {
        var slot = new SourceSlot(isPrimary: false, x: 20, y: 20, w: 30, h: 30, isOverlay: true)
        {
            CanvasWidth = 1000,
            CanvasHeight = 1000,
            IsAspectLocked = false
        };

        slot.ResizeByHandleDelta(direction, 10, 10);

        Assert.True(slot.WPercent >= 5.0 && slot.WPercent <= 100.0, $"WPercent expected >= 5 and <= 100, got {slot.WPercent}");
        Assert.True(slot.HPercent >= 5.0 && slot.HPercent <= 100.0, $"HPercent expected >= 5 and <= 100, got {slot.HPercent}");
        Assert.True(slot.XPercent >= 0.0 && slot.XPercent + slot.WPercent <= 100.0, $"XPercent expected within bounds, got {slot.XPercent}");
        Assert.True(slot.YPercent >= 0.0 && slot.YPercent + slot.HPercent <= 100.0, $"YPercent expected within bounds, got {slot.YPercent}");
    }
}
