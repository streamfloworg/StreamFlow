using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Helpers.Behaviors;

public enum SlotPlacementMode
{
    None,
    Move,
    Resize,
}

public enum SlotResizeDirection
{
    BottomRight,
    Bottom,
    Right,
    TopRight,
    Top,
    TopLeft,
    Left,
    BottomLeft,
}

public enum SlotGuideRole
{
    None,
    Vertical,
    Horizontal,
}

/// <summary>Drag-move/drag-resize with alignment-guide snapping for items on a placement canvas.
/// Reusable across pages: sibling <see cref="SourceSlot"/>s are derived from the enclosing
/// ItemsControl's own ItemsSource (not a hardcoded ViewModel reference), and selection is exposed
/// as a two-way attached property the host page binds to its own SelectedSlot.</summary>
public static class SlotPlacementBehavior
{
    private const double SnapTolerancePixels = 4;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SlotPlacementBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty SelectedSlotProperty = DependencyProperty.RegisterAttached(
        "SelectedSlot", typeof(SourceSlot), typeof(SlotPlacementBehavior),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode", typeof(SlotPlacementMode), typeof(SlotPlacementBehavior),
        new PropertyMetadata(SlotPlacementMode.None, OnModeChanged));

    public static readonly DependencyProperty GuideRoleProperty = DependencyProperty.RegisterAttached(
        "GuideRole", typeof(SlotGuideRole), typeof(SlotPlacementBehavior), new PropertyMetadata(SlotGuideRole.None));

    public static readonly DependencyProperty ResizeDirectionProperty = DependencyProperty.RegisterAttached(
        "ResizeDirection", typeof(SlotResizeDirection), typeof(SlotPlacementBehavior),
        new PropertyMetadata(SlotResizeDirection.BottomRight));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static SourceSlot? GetSelectedSlot(DependencyObject element) => (SourceSlot?)element.GetValue(SelectedSlotProperty);
    public static void SetSelectedSlot(DependencyObject element, SourceSlot? value) => element.SetValue(SelectedSlotProperty, value);

    public static SlotPlacementMode GetMode(DependencyObject element) => (SlotPlacementMode)element.GetValue(ModeProperty);
    public static void SetMode(DependencyObject element, SlotPlacementMode value) => element.SetValue(ModeProperty, value);

    public static SlotGuideRole GetGuideRole(DependencyObject element) => (SlotGuideRole)element.GetValue(GuideRoleProperty);
    public static void SetGuideRole(DependencyObject element, SlotGuideRole value) => element.SetValue(GuideRoleProperty, value);

    public static SlotResizeDirection GetResizeDirection(DependencyObject element) => (SlotResizeDirection)element.GetValue(ResizeDirectionProperty);
    public static void SetResizeDirection(DependencyObject element, SlotResizeDirection value) => element.SetValue(ResizeDirectionProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Canvas canvas) return;

        if ((bool)e.NewValue) canvas.PreviewMouseLeftButtonDown += OnCanvasPreviewMouseLeftButtonDown;
        else canvas.PreviewMouseLeftButtonDown -= OnCanvasPreviewMouseLeftButtonDown;
    }

    /// <summary>Clicking empty canvas space deselects. Only acts when the click didn't land on
    /// a MoveThumb/ResizeThumb — otherwise this fired for every drag-start too (this is a Preview/
    /// tunneling handler, so it runs before the Thumb itself sees the click), which briefly
    /// cleared the selection and collapsed the resize handle mid-drag, aborting the drag since
    /// WPF releases mouse capture when a captured element loses visibility.</summary>
    private static void OnCanvasPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Canvas canvas) return;
        if (e.OriginalSource is DependencyObject source && FindVisualParent<Thumb>(source) is not null) return;

        SetSelectedSlot(canvas, null);
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Thumb thumb) return;

        if (e.OldValue is SlotPlacementMode.Move)
        {
            thumb.DragStarted -= MoveThumb_DragStarted;
            thumb.DragDelta -= MoveThumb_DragDelta;
            thumb.DragCompleted -= Thumb_DragCompleted;
        }
        else if (e.OldValue is SlotPlacementMode.Resize)
        {
            thumb.DragDelta -= ResizeThumb_DragDelta;
            thumb.DragCompleted -= Thumb_DragCompleted;
        }

        if (e.NewValue is SlotPlacementMode.Move)
        {
            thumb.DragStarted += MoveThumb_DragStarted;
            thumb.DragDelta += MoveThumb_DragDelta;
            thumb.DragCompleted += Thumb_DragCompleted;
        }
        else if (e.NewValue is SlotPlacementMode.Resize)
        {
            thumb.DragDelta += ResizeThumb_DragDelta;
            thumb.DragCompleted += Thumb_DragCompleted;
        }
    }

    private static void MoveThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.DataContext is not SourceSlot slot) return;

        var canvas = FindPlacementCanvas(thumb);
        if (canvas is not null) SetSelectedSlot(canvas, slot);
    }

    private static void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.DataContext is not SourceSlot slot) return;

        var canvas = FindPlacementCanvas(thumb);
        if (canvas is null) return;

        var newX = Math.Clamp(slot.XPercent + e.HorizontalChange / slot.CanvasWidth * 100.0, 0, 100 - slot.WPercent);
        var newY = Math.Clamp(slot.YPercent + e.VerticalChange / slot.CanvasHeight * 100.0, 0, 100 - slot.HPercent);

        var xTargets = CollectSnapTargets(thumb, slot, horizontal: true);
        var yTargets = CollectSnapTargets(thumb, slot, horizontal: false);

        slot.XPercent = SnapBoxPosition(newX, slot.WPercent, xTargets, slot.CanvasWidth, out var vGuide);
        slot.YPercent = SnapBoxPosition(newY, slot.HPercent, yTargets, slot.CanvasHeight, out var hGuide);

        SetGuideLine(canvas, SlotGuideRole.Vertical, vGuide);
        SetGuideLine(canvas, SlotGuideRole.Horizontal, hGuide);
    }

    private static void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.DataContext is not SourceSlot slot) return;

        var canvas = FindPlacementCanvas(thumb);
        if (canvas is null) return;

        var direction = GetResizeDirection(thumb);
        slot.ResizeByHandleDelta(direction, e.HorizontalChange, e.VerticalChange);

        var xTargets = CollectSnapTargets(thumb, slot, horizontal: true);
        var yTargets = CollectSnapTargets(thumb, slot, horizontal: false);

        double? vGuide = null, hGuide = null;
        if (direction is SlotResizeDirection.Right or SlotResizeDirection.TopRight or SlotResizeDirection.BottomRight)
        {
            var right = FindNearestTarget(slot.XPercent + slot.WPercent, xTargets, slot.CanvasWidth, out var rightDistPx);
            if (rightDistPx <= SnapTolerancePixels)
            {
                slot.ResizeToWidthPercent(right - slot.XPercent);
                vGuide = right / 100.0 * slot.CanvasWidth;
            }
        }
        else if (direction is SlotResizeDirection.Bottom or SlotResizeDirection.BottomLeft)
        {
            var bottom = FindNearestTarget(slot.YPercent + slot.HPercent, yTargets, slot.CanvasHeight, out var bottomDistPx);
            if (bottomDistPx <= SnapTolerancePixels)
            {
                slot.ResizeToHeightPercent(bottom - slot.YPercent);
                hGuide = bottom / 100.0 * slot.CanvasHeight;
            }
        }

        SetGuideLine(canvas, SlotGuideRole.Vertical, vGuide);
        SetGuideLine(canvas, SlotGuideRole.Horizontal, hGuide);
    }

    private static void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is not Thumb thumb) return;

        var canvas = FindPlacementCanvas(thumb);
        if (canvas is null) return;

        SetGuideLine(canvas, SlotGuideRole.Vertical, null);
        SetGuideLine(canvas, SlotGuideRole.Horizontal, null);
    }

    /// <summary>Candidate snap positions (percent, along one axis): the canvas edges/center,
    /// plus every other slot's leading edge, trailing edge, and center. Siblings are read from
    /// the enclosing ItemsControl's own ItemsSource rather than any specific ViewModel type.</summary>
    private static List<double> CollectSnapTargets(Thumb self, SourceSlot selfSlot, bool horizontal)
    {
        var targets = new List<double> { 0, 50, 100 };

        var itemsControl = FindVisualParent<ItemsControl>(self);
        if (itemsControl is null) return targets;

        foreach (var item in itemsControl.Items)
        {
            if (item is not SourceSlot other || ReferenceEquals(other, selfSlot)) continue;

            var start = horizontal ? other.XPercent : other.YPercent;
            var size = horizontal ? other.WPercent : other.HPercent;
            targets.Add(start);
            targets.Add(start + size);
            targets.Add(start + size / 2);
        }

        return targets;
    }

    /// <summary>For a move: snaps whichever of the box's leading edge/trailing edge/center is
    /// nearest a target (within tolerance), returning the adjusted leading-edge position.</summary>
    private static double SnapBoxPosition(double position, double size, List<double> targets, double canvasSize, out double? guidePixel)
    {
        ReadOnlySpan<double> offsets = [0, size, size / 2];

        double? bestTarget = null;
        var bestOffset = 0.0;
        var bestDistPx = double.MaxValue;

        foreach (var offset in offsets)
        {
            var boxValue = position + offset;
            foreach (var target in targets)
            {
                var distPx = Math.Abs(boxValue - target) / 100.0 * canvasSize;
                if (distPx < bestDistPx)
                {
                    bestDistPx = distPx;
                    bestTarget = target;
                    bestOffset = offset;
                }
            }
        }

        if (bestTarget is double t && bestDistPx <= SnapTolerancePixels)
        {
            guidePixel = t / 100.0 * canvasSize;
            return t - bestOffset;
        }

        guidePixel = null;
        return position;
    }

    /// <summary>For a resize: finds the nearest target to a single edge value, reporting the
    /// distance so the caller can decide whether it's within tolerance.</summary>
    private static double FindNearestTarget(double value, List<double> targets, double canvasSize, out double distancePixels)
    {
        var best = value;
        distancePixels = double.MaxValue;

        foreach (var target in targets)
        {
            var distPx = Math.Abs(value - target) / 100.0 * canvasSize;
            if (distPx < distancePixels)
            {
                distancePixels = distPx;
                best = target;
            }
        }

        return best;
    }

    private static void SetGuideLine(Canvas placementCanvas, SlotGuideRole role, double? positionPixel)
    {
        var line = placementCanvas.Children.OfType<Line>().FirstOrDefault(l => GetGuideRole(l) == role);
        if (line is null) return;

        if (positionPixel is not double p)
        {
            line.Visibility = Visibility.Collapsed;
            return;
        }

        if (role == SlotGuideRole.Vertical) { line.X1 = p; line.X2 = p; }
        else { line.Y1 = p; line.Y2 = p; }
        line.Visibility = Visibility.Visible;
    }

    /// <summary>Walks up from a Thumb inside an item template, past the ItemsControl that owns
    /// the items, to the enclosing Canvas that hosts the guide Lines as siblings of that
    /// ItemsControl.</summary>
    private static Canvas? FindPlacementCanvas(DependencyObject child)
    {
        var itemsControl = FindVisualParent<ItemsControl>(child);
        return itemsControl is null ? null : FindVisualParent<Canvas>(itemsControl);
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject is null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }
}
