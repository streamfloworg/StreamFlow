using System.Collections.ObjectModel;
using System.Windows.Media;
using StreamFlow.App.Helpers.Behaviors;
using StreamFlow.Core.Data;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>
/// One capture source's placement within the composited output frame.
/// Coordinates are percentages (0-100) of the frame, matching the Rust core's
/// StreamSourceDef wire format so this maps 1:1 onto the IPC command.
/// </summary>
public partial class SourceSlot : ObservableObject
{
    public bool IsPrimary { get; }

    /// <summary>True for any overlay (fixed content, not picked from AvailableSources) as
    /// opposed to a live monitor/window/webcam capture source. Controls UI: no source
    /// dropdown, shows a content summary instead. See <see cref="IsStaticOverlay"/> for
    /// whether it also needs Start/StopCapture — video overlays are overlays that do.</summary>
    public bool IsOverlay { get; }

    /// <summary>Kind-specific payload — null for capture-source slots (primary/PiP). See
    /// <see cref="IOverlayContent"/>'s own doc comment for why this replaced a flat set of
    /// per-kind nullable properties directly on this class.</summary>
    [ObservableProperty]
    private IOverlayContent? _content;

    /// <summary>Which kind of overlay this is; null for capture-source slots. Derived from
    /// Content's own concrete type rather than tracked separately, so the two can never drift
    /// out of sync.</summary>
    public OverlayKind? OverlayKind => Content?.Kind;

    public bool IsImageOverlay => Content is ImageOverlayContent;
    public bool IsTextOverlay => Content is TextOverlayContent;
    public bool IsColorOverlay => Content is ColorOverlayContent;
    public bool IsVideoOverlay => Content is VideoOverlayContent;
    public bool IsChatOverlay => Content is ChatOverlayContent;
    public bool IsBlurOverlay => Content is BlurOverlayContent;
    public bool IsTimerOverlay => Content is TimerOverlayContent;
    public bool IsAlertOverlay => Content is AlertOverlayContent;
    public bool IsAdvancedOverlay => Content is IAdvancedOverlayContent;
    public bool IsGroupOverlay => Content is GroupOverlayContent;

    public bool IsTextBasedOverlay => Content is IHasTextStyle && Content is not ChatOverlayContent;
    public TextStyle? TextOverlayStyle => (Content as IHasTextStyle)?.Style;
    public string TextOverlayDisplayText => Content switch
    {
        TextOverlayContent text => text.OverlayText ?? "",
        TimerOverlayContent timer => timer.TimerDisplayText ?? "",
        _ => (Content as IHasTextStyle)?.Style is not null ? Content?.ToString() ?? "" : ""
    };

    public void NotifyTextContentChanged()
    {
        OnPropertyChanged(nameof(TextOverlayStyle));
        OnPropertyChanged(nameof(TextOverlayDisplayText));
    }

    /// <summary>True for overlay kinds registered once via AddStaticOverlay (image/text/color)
    /// — no ongoing session, so Start/StopCapture are skipped for these. False for video
    /// overlays, which decode/loop continuously in the core exactly like a live capture.</summary>
    public bool IsStaticOverlay => IsOverlay && !IsVideoOverlay && !IsGroupOverlay && !IsAdvancedOverlay;

    /// <summary>Whether GoLiveView should render a live-updating thumbnail for this slot — every
    /// capture source and video overlay gets its own frames forwarded over the data pipe,
    /// primary included (it's just another positioned layer now, no longer shown via a separate
    /// dedicated preview element); static overlays render directly from their own content
    /// instead.</summary>
    public bool HasLiveThumbnail => !IsStaticOverlay && !IsGroupOverlay && !IsAdvancedOverlay;

    /// <summary>Live video thumbnail for a PiP capture source, updated by GoLiveView as raw
    /// frames arrive over the data pipe tagged with this slot's SourceId. Null until the core
    /// starts forwarding frames for it.</summary>
    [ObservableProperty]
    private ImageSource? _liveThumbnail;

    /// <summary>Whether the properties panel should show the Chroma Key controls for this
    /// slot — only Image/Video content supports it (see <see cref="IChromaKeyable"/>); the
    /// compositor itself doesn't care what kind of layer it's keying.</summary>
    public bool SupportsChromaKey => Content is IChromaKeyable;

    partial void OnContentChanged(IOverlayContent? value)
    {
        OnPropertyChanged(nameof(OverlayKind));
        OnPropertyChanged(nameof(IsImageOverlay));
        OnPropertyChanged(nameof(IsTextOverlay));
        OnPropertyChanged(nameof(IsColorOverlay));
        OnPropertyChanged(nameof(IsVideoOverlay));
        OnPropertyChanged(nameof(IsChatOverlay));
        OnPropertyChanged(nameof(IsBlurOverlay));
        OnPropertyChanged(nameof(IsTimerOverlay));
        OnPropertyChanged(nameof(IsAlertOverlay));
        OnPropertyChanged(nameof(IsAdvancedOverlay));
        OnPropertyChanged(nameof(IsGroupOverlay));
        OnPropertyChanged(nameof(IsTextBasedOverlay));
        OnPropertyChanged(nameof(TextOverlayStyle));
        OnPropertyChanged(nameof(TextOverlayDisplayText));
        OnPropertyChanged(nameof(IsStaticOverlay));
        OnPropertyChanged(nameof(HasLiveThumbnail));
        OnPropertyChanged(nameof(SupportsChromaKey));
        OnPropertyChanged(nameof(Children));
    }

    public System.Collections.IList? Children
    {
        get
        {
            if (Content is GroupOverlayContent group) return group.Children;
            return null;
        }
    }

    public bool CanBeAddedToGroup => !IsPrimary && OverlayKind != StreamFlow.Core.Data.OverlayKind.Group && OverlayKind != StreamFlow.Core.Data.OverlayKind.Alert;

    private bool _lockChildren = true;
    public bool LockChildren
    {
        get => (Content as GroupOverlayContent)?.LockChildren ?? (Content as AlertOverlayContent)?.LockChildren ?? _lockChildren;
        set
        {
            if (Content is GroupOverlayContent g) g.LockChildren = value;
            if (Content is AlertOverlayContent a) a.LockChildren = value;
            SetProperty(ref _lockChildren, value);
        }
    }

    [ObservableProperty]
    private string? _sourceId;

    [ObservableProperty]
    private string _displayName = "(no source selected)";

    /// <summary>Transient UI state: true while this slot's name is being edited inline in the
    /// Layers list (double-click the name to enter, Enter/click-away to commit, Escape to
    /// cancel — see GoLiveView.xaml's rename TextBox/TextBlock swap and the handlers in
    /// GoLiveView.xaml.cs). Not persisted; a freshly loaded/added slot always starts false.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSelectedOrChildSelected));
        ParentGroup?.NotifyChildSelectionChanged();
    }

    public void NotifyChildSelectionChanged()
    {
        OnPropertyChanged(nameof(IsSelectedOrChildSelected));
        ParentGroup?.NotifyChildSelectionChanged();
    }

    public bool IsSelectedOrChildSelected
    {
        get
        {
            if (IsSelected) return true;
            if (Content is AlertOverlayContent alert && alert.Children.Any(c => c.IsSelected))
                return true;
            if (Content is GroupOverlayContent group && group.Children.Cast<SourceSlot>().Any(c => c.IsSelected))
                return true;
            return false;
        }
    }

    [ObservableProperty]
    private double _xPercent;

    [ObservableProperty]
    private double _yPercent;

    [ObservableProperty]
    private double _wPercent;

    [ObservableProperty]
    private double _hPercent;

    /// <summary>Corner rounding as a percentage of half this slot's shorter side (0 = square,
    /// 100 = a full pill/circle). Meaningless for the primary (rounding the whole output
    /// frame's corners isn't a supported look), so the UI only exposes this for PiP/overlays.</summary>
    [ObservableProperty]
    private double _cornerRadiusPercent;

    /// <summary>0-100 UI scale for this layer's own opacity, independent of its pixel content's
    /// alpha channel — converted to the compositor's 0.0-1.0 factor in
    /// SceneEditorViewModel.BuildStreamSources. Meaningless for a blur layer (no pixel content
    /// of its own to fade — its Strength slider already covers its one knob), so the UI hides it
    /// for that kind (see <see cref="IsBlurOverlay"/>).</summary>
    [ObservableProperty]
    private double _opacityPercent = 100;

    /// <summary>Discrete clockwise rotation in degrees — 0, 90, 180, or 270 only (no free
    /// rotation, which would need resampling instead of an exact pixel remap). 90/270 rotate the
    /// content to fill this slot's *current* box exactly, whatever its native aspect ratio —
    /// resize the box afterward to preserve proportions if that matters. Meaningless for a blur
    /// layer, same reasoning as <see cref="OpacityPercent"/>.</summary>
    [ObservableProperty]
    private int _rotationDegrees;

    /// <summary>Native width/height ratio of this slot's source, detected from incoming
    /// preview frames. Null until a frame for this source has been observed.</summary>
    [ObservableProperty]
    private double? _aspectRatio = 16.0 / 9.0;

    /// <summary>Rendered content width (pixels, whatever native resolution the renderer produced
    /// it at) from the previous OverlayContentRenderer.ApplyRenderedAspectRatio call — lets that
    /// method tell "the aspect ratio changed because the content got bigger/smaller" apart from
    /// "the aspect ratio changed but the content is roughly the same size" for Text/Timer
    /// overlays specifically, whose FontSize slider should visibly grow/shrink the box, not just
    /// avoid distorting it. Internal bookkeeping only, not user-facing.</summary>
    internal double? LastRenderedContentWidth { get; set; }

    private bool _isSettingDimensions;

    /// <summary>When true, resizing this slot preserves <see cref="AspectRatio"/> instead
    /// of allowing free-form width/height. Always on for now; a manual-unlock toggle can
    /// be added later if a user wants to distort a source.</summary>
    [ObservableProperty]
    private bool _isAspectLocked = true;

    partial void OnIsAspectLockedChanged(bool value)
    {
        if (value && WPercent > 0 && HPercent > 0 && CanvasWidth > 0 && CanvasHeight > 0)
        {
            var pixelW = WPercent / 100.0 * CanvasWidth;
            var pixelH = HPercent / 100.0 * CanvasHeight;
            if (pixelH > 0)
            {
                AspectRatio = pixelW / pixelH;
            }
        }
    }

    [RelayCommand]
    public void SetSlotAspectRatioPreset(string ratioPreset)
    {
        double? newRatio = ratioPreset.ToLowerInvariant() switch
        {
            "16:9" or "169" => 16.0 / 9.0,
            "9:16" or "916" or "vertical" => 9.0 / 16.0,
            "4:3" or "43" => 4.0 / 3.0,
            "1:1" or "11" or "square" => 1.0,
            "21:9" or "219" or "ultrawide" => 21.0 / 9.0,
            _ => null
        };

        if (newRatio.HasValue)
        {
            AspectRatio = newRatio.Value;
            IsAspectLocked = true;
            if (WPercent > 0)
            {
                ResizeToWidthPercent(WPercent);
            }
        }
    }

    public SourceSlot(
        bool isPrimary, double x, double y, double w, double h,
        bool isOverlay = false, IOverlayContent? content = null)
    {
        IsPrimary = isPrimary;
        IsOverlay = isOverlay;
        _content = content;
        _xPercent = x;
        _yPercent = y;
        _wPercent = w;
        _hPercent = h;
    }

    // The placement canvas's reference width is always 640 (an arbitrary normalization
    // constant — only the ratio to CanvasHeight matters). CanvasHeight, on the other hand,
    // is NOT fixed: it's kept in sync (by GoLiveViewModel.UpdateCanvasReference) with the
    // scene's primary's real aspect ratio, so the canvas's own shape always matches the real
    // composited frame exactly — no letterboxing, ever. That matters because the Rust core
    // has no letterbox concept at all: x_percent/w_percent map 1:1 onto the real output frame
    // (see compositor.rs), so if the canvas's shape doesn't match the real AR, whatever
    // "outside the visible picture" area is left over in the editor is still fully valid,
    // draggable overlay space from the server's point of view — just invisible/misleading in
    // the local preview. Matching the canvas's shape to the real AR removes that mismatch
    // instead of just visually hiding it.
    internal const double DefaultCanvasWidth = 640;

    [ObservableProperty]
    private double _canvasWidth = DefaultCanvasWidth;

    [ObservableProperty]
    private double _canvasHeight = DefaultCanvasWidth * 9.0 / 16.0;

    /// <summary>
    /// Sets this slot's width, deriving height from <see cref="AspectRatio"/> when locked
    /// (falling back to free-form resize if the ratio isn't known yet), clamped so the box
    /// stays within the canvas.
    /// </summary>
    public void ResizeToWidthPercent(double desiredWPercent)
    {
        desiredWPercent = Math.Clamp(desiredWPercent, 5, 100 - XPercent);

        if (!IsAspectLocked || AspectRatio is not double ar || ar <= 0)
        {
            WPercent = desiredWPercent;
            return;
        }

        var pixelW = desiredWPercent / 100.0 * CanvasWidth;
        var desiredHPercent = pixelW / ar / CanvasHeight * 100.0;

        var maxHPercent = 100 - YPercent;
        if (desiredHPercent > maxHPercent)
        {
            desiredHPercent = maxHPercent;
            var fittedPixelW = desiredHPercent / 100.0 * CanvasHeight * ar;
            desiredWPercent = Math.Clamp(fittedPixelW / CanvasWidth * 100.0, 5, 100 - XPercent);
        }

        WPercent = desiredWPercent;
        HPercent = Math.Max(5, desiredHPercent);
    }

    /// <summary>Mirror of <see cref="ResizeToWidthPercent"/> for snapping the bottom edge:
    /// sets height, deriving width from <see cref="AspectRatio"/> when locked.</summary>
    public void ResizeToHeightPercent(double desiredHPercent)
    {
        desiredHPercent = Math.Clamp(desiredHPercent, 5, 100 - YPercent);

        if (!IsAspectLocked || AspectRatio is not double ar || ar <= 0)
        {
            HPercent = desiredHPercent;
            return;
        }

        var pixelH = desiredHPercent / 100.0 * CanvasHeight;
        var desiredWPercent = pixelH * ar / CanvasWidth * 100.0;

        var maxWPercent = 100 - XPercent;
        if (desiredWPercent > maxWPercent)
        {
            desiredWPercent = maxWPercent;
            var fittedPixelH = desiredWPercent / 100.0 * CanvasWidth / ar;
            desiredHPercent = Math.Clamp(fittedPixelH / CanvasHeight * 100.0, 5, 100 - YPercent);
        }

        HPercent = desiredHPercent;
        WPercent = Math.Max(5, desiredWPercent);
    }

    /// <summary>
    /// Applies a handle drag for any corner or side (8-direction resize) while respecting <see cref="AspectRatio"/>
    /// and canvas boundaries.
    /// </summary>
    public void ResizeByHandleDelta(SlotResizeDirection direction, double horizontalPixelDelta, double verticalPixelDelta)
    {
        double minWPercent = 5.0;
        double minHPercent = 5.0;

        double dxPercent = horizontalPixelDelta / CanvasWidth * 100.0;
        double dyPercent = verticalPixelDelta / CanvasHeight * 100.0;

        // Unlocked / Freeform Resize
        if (!IsAspectLocked || AspectRatio is not double ar || ar <= 0)
        {
            if (direction is SlotResizeDirection.Left or SlotResizeDirection.TopLeft or SlotResizeDirection.BottomLeft)
            {
                double oldRight = XPercent + WPercent;
                double newX = Math.Clamp(XPercent + dxPercent, 0, oldRight - minWPercent);
                XPercent = newX;
                WPercent = oldRight - newX;
            }
            else if (direction is SlotResizeDirection.Right or SlotResizeDirection.TopRight or SlotResizeDirection.BottomRight)
            {
                WPercent = Math.Clamp(WPercent + dxPercent, minWPercent, 100 - XPercent);
            }

            if (direction is SlotResizeDirection.Top or SlotResizeDirection.TopLeft or SlotResizeDirection.TopRight)
            {
                double oldBottom = YPercent + HPercent;
                double newY = Math.Clamp(YPercent + dyPercent, 0, oldBottom - minHPercent);
                YPercent = newY;
                HPercent = oldBottom - newY;
            }
            else if (direction is SlotResizeDirection.Bottom or SlotResizeDirection.BottomLeft or SlotResizeDirection.BottomRight)
            {
                HPercent = Math.Clamp(HPercent + dyPercent, minHPercent, 100 - YPercent);
            }
            return;
        }

        // Aspect-Locked Resize
        double oldR = XPercent + WPercent;
        double oldB = YPercent + HPercent;

        double currentPxW = WPercent / 100.0 * CanvasWidth;
        double currentPxH = HPercent / 100.0 * CanvasHeight;

        double targetPxW = currentPxW;
        double targetPxH = currentPxH;

        if (direction is SlotResizeDirection.Left or SlotResizeDirection.TopLeft or SlotResizeDirection.BottomLeft)
        {
            targetPxW = Math.Max(minWPercent / 100.0 * CanvasWidth, currentPxW - horizontalPixelDelta);
        }
        else if (direction is SlotResizeDirection.Right or SlotResizeDirection.TopRight or SlotResizeDirection.BottomRight)
        {
            targetPxW = Math.Max(minWPercent / 100.0 * CanvasWidth, currentPxW + horizontalPixelDelta);
        }

        if (direction is SlotResizeDirection.Top or SlotResizeDirection.TopLeft or SlotResizeDirection.TopRight)
        {
            targetPxH = Math.Max(minHPercent / 100.0 * CanvasHeight, currentPxH - verticalPixelDelta);
        }
        else if (direction is SlotResizeDirection.Bottom or SlotResizeDirection.BottomLeft or SlotResizeDirection.BottomRight)
        {
            targetPxH = Math.Max(minHPercent / 100.0 * CanvasHeight, currentPxH + verticalPixelDelta);
        }

        double newPxW, newPxH;
        if (direction is SlotResizeDirection.TopLeft or SlotResizeDirection.TopRight or SlotResizeDirection.BottomLeft or SlotResizeDirection.BottomRight)
        {
            var t = (targetPxW * ar + targetPxH) / (ar * ar + 1);
            newPxW = t * ar;
            newPxH = t;
        }
        else if (direction is SlotResizeDirection.Left or SlotResizeDirection.Right)
        {
            newPxW = targetPxW;
            newPxH = newPxW / ar;
        }
        else
        {
            newPxH = targetPxH;
            newPxW = newPxH * ar;
        }

        double newWPercent = Math.Clamp(newPxW / CanvasWidth * 100.0, minWPercent, 100);
        double newHPercent = Math.Clamp(newPxH / CanvasHeight * 100.0, minHPercent, 100);

        if (direction is SlotResizeDirection.Left or SlotResizeDirection.TopLeft or SlotResizeDirection.BottomLeft)
        {
            XPercent = Math.Clamp(oldR - newWPercent, 0, oldR - minWPercent);
        }
        if (direction is SlotResizeDirection.Top or SlotResizeDirection.TopLeft or SlotResizeDirection.TopRight)
        {
            YPercent = Math.Clamp(oldB - newHPercent, 0, oldB - minHPercent);
        }

        WPercent = newWPercent;
        HPercent = newHPercent;
    }

    /// <summary>
    /// Applies a corner-handle drag while preserving <see cref="AspectRatio"/>. The raw
    /// horizontal/vertical deltas (in canvas pixels) are combined via vector projection onto
    /// the aspect-ratio line, so width and height are never independently changeable when
    /// locked — dragging purely vertically, purely horizontally, or diagonally all resize the
    /// box consistently, instead of one axis being ignored or fighting the other.
    /// Falls back to independent free-form resize when unlocked or the ratio isn't known yet.
    /// </summary>
    public void ResizeByDragDelta(double horizontalPixelDelta, double verticalPixelDelta)
    {
        ResizeByHandleDelta(SlotResizeDirection.BottomRight, horizontalPixelDelta, verticalPixelDelta);
    }

    partial void OnWPercentChanged(double value)
    {
        NotifyRenderBoundsChanged();
        if (_isSettingDimensions) return;
        if (IsAspectLocked && AspectRatio is double ar && ar > 0)
        {
            _isSettingDimensions = true;
            try
            {
                var pixelW = value / 100.0 * CanvasWidth;
                var desiredHPercent = pixelW / ar / CanvasHeight * 100.0;
                HPercent = Math.Clamp(desiredHPercent, 5, 100 - YPercent);
            }
            finally
            {
                _isSettingDimensions = false;
            }
        }
    }

    partial void OnHPercentChanged(double value)
    {
        NotifyRenderBoundsChanged();
        if (_isSettingDimensions) return;

        if (IsAspectLocked && AspectRatio is double ar && ar > 0)
        {
            _isSettingDimensions = true;
            try
            {
                var pixelH = value / 100.0 * CanvasHeight;
                var desiredWPercent = pixelH * ar / CanvasWidth * 100.0;
                WPercent = Math.Clamp(desiredWPercent, 5, 100 - XPercent);
            }
            finally
            {
                _isSettingDimensions = false;
            }
        }
    }

    [ObservableProperty]
    private bool _isInSelectedGroup;

    private System.Collections.Generic.IEnumerable<SourceSlot>? GetChildrenToUpdate()
    {
        if (!LockChildren) return null;
        if (Content is GroupOverlayContent group) return group.Children;
        return null;
    }

    private bool _isUpdatingGroupChildren;

    partial void OnXPercentChanging(double value)
    {
        if (_isUpdatingGroupChildren) return;
        var oldX = XPercent;
        var newX = value;
        var deltaX = newX - oldX;

        var children = GetChildrenToUpdate();
        if (deltaX != 0 && children is not null)
        {
            _isUpdatingGroupChildren = true;
            try
            {
                foreach (var child in children)
                {
                    child.XPercent += deltaX;
                }
            }
            finally
            {
                _isUpdatingGroupChildren = false;
            }
        }
    }

    partial void OnYPercentChanging(double value)
    {
        if (_isUpdatingGroupChildren) return;
        var oldY = YPercent;
        var newY = value;
        var deltaY = newY - oldY;

        var children = GetChildrenToUpdate();
        if (deltaY != 0 && children is not null)
        {
            _isUpdatingGroupChildren = true;
            try
            {
                foreach (var child in children)
                {
                    child.YPercent += deltaY;
                }
            }
            finally
            {
                _isUpdatingGroupChildren = false;
            }
        }
    }

    partial void OnWPercentChanging(double value)
    {
        if (_isUpdatingGroupChildren) return;
        var oldW = WPercent;
        var newW = value;
        var children = GetChildrenToUpdate();
        if (oldW > 0 && newW > 0 && children is not null)
        {
            var scaleX = newW / oldW;
            if (scaleX != 1.0)
            {
                _isUpdatingGroupChildren = true;
                try
                {
                    var groupX = XPercent;
                    foreach (var child in children)
                    {
                        var relativeX = child.XPercent - groupX;
                        child.WPercent *= scaleX;
                        child.XPercent = groupX + relativeX * scaleX;
                    }
                }
                finally
                {
                    _isUpdatingGroupChildren = false;
                }
            }
        }
    }

    partial void OnHPercentChanging(double value)
    {
        if (_isUpdatingGroupChildren) return;
        var oldH = HPercent;
        var newH = value;
        var children = GetChildrenToUpdate();
        if (oldH > 0 && newH > 0 && children is not null)
        {
            var scaleY = newH / oldH;
            if (scaleY != 1.0)
            {
                _isUpdatingGroupChildren = true;
                try
                {
                    var groupY = YPercent;
                    foreach (var child in children)
                    {
                        var relativeY = child.YPercent - groupY;
                        child.HPercent *= scaleY;
                        child.YPercent = groupY + relativeY * scaleY;
                    }
                }
                finally
                {
                    _isUpdatingGroupChildren = false;
                }
            }
        }
    }

    [ObservableProperty]
    private SourceSlot? _parentGroup;

    partial void OnParentGroupChanged(SourceSlot? value)
    {
        OnPropertyChanged(nameof(VisualLeftMargin));
        NotifyRenderBoundsChanged();
    }

    public System.Windows.Thickness VisualLeftMargin =>
        ParentGroup is not null ? new System.Windows.Thickness(24, 2, 4, 2) : new System.Windows.Thickness(4, 2, 4, 2);

    public double RenderXPercent => ParentGroup != null ? ParentGroup.RenderXPercent + (XPercent / 100.0) * ParentGroup.RenderWPercent : XPercent;
    public double RenderYPercent => ParentGroup != null ? ParentGroup.RenderYPercent + (YPercent / 100.0) * ParentGroup.RenderHPercent : YPercent;
    public double RenderWPercent => ParentGroup != null ? (WPercent / 100.0) * ParentGroup.RenderWPercent : WPercent;
    public double RenderHPercent => ParentGroup != null ? (HPercent / 100.0) * ParentGroup.RenderHPercent : HPercent;

    public void NotifyRenderBoundsChanged()
    {
        OnPropertyChanged(nameof(RenderXPercent));
        OnPropertyChanged(nameof(RenderYPercent));
        OnPropertyChanged(nameof(RenderWPercent));
        OnPropertyChanged(nameof(RenderHPercent));

        if (Content is GroupOverlayContent group)
        {
            foreach (var child in group.Children)
                child.NotifyRenderBoundsChanged();
        }
        else if (Content is AlertOverlayContent alert)
        {
            foreach (var child in alert.Children)
                child.NotifyRenderBoundsChanged();
        }
    }

    partial void OnXPercentChanged(double value) => NotifyRenderBoundsChanged();
    partial void OnYPercentChanged(double value) => NotifyRenderBoundsChanged();
}
