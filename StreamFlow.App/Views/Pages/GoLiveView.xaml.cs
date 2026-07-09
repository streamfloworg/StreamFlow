using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using StreamFlow.App.Rendering;
using StreamFlow.App.Services.Core;
using StreamFlow.App.ViewModels.Pages;

using TextBox = System.Windows.Controls.TextBox;

namespace StreamFlow.App.Views.Pages;
public partial class GoLiveView
{
    public GoLiveViewModel ViewModel
    {
        get;
    }

    private WriteableBitmap? _previewBitmap;
    private volatile bool _frameDispatchPending;

    /// <summary>
    /// Inverse of the preview Viewbox's current zoom, so labels can counter-scale via
    /// LayoutTransform and stay a constant on-screen size regardless of container size.
    /// </summary>
    public static readonly DependencyProperty LabelScaleCorrectionProperty = DependencyProperty.Register(
        nameof(LabelScaleCorrection), typeof(double), typeof(GoLiveView), new PropertyMetadata(1.0));

    public double LabelScaleCorrection
    {
        get => (double)GetValue(LabelScaleCorrectionProperty);
        set => SetValue(LabelScaleCorrectionProperty, value);
    }

    public GoLiveView(GoLiveViewModel viewModel, CoreBridgeService core)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        core.FrameReceived += OnFrameReceived;
    }

    /// <summary>Starts audio monitor sessions for whatever's checked in Audio Sources — see
    /// GoLiveViewModel.OnNavigatedToAsync. Frame.Navigate doesn't drive the ViewModel's
    /// navigation hooks on its own in this app, so Loaded/Unloaded stand in for them (same
    /// pattern AudioView already uses for its own page-active tracking).</summary>
    private async void GoLiveView_Loaded(object sender, RoutedEventArgs e) => await ViewModel.OnNavigatedToAsync();

    private async void GoLiveView_Unloaded(object sender, RoutedEventArgs e) => await ViewModel.OnNavigatedFromAsync();

    // Inline rename for a Layers-list row: double-click the name to enter edit mode (see the
    // TextBlock/TextBox pair in the ListBox.ItemTemplate), Enter or click-away commits, Escape
    // reverts to whatever the name was before this edit started.
    private string? _renameOriginalDisplayName;

    private void LayerName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (((FrameworkElement)sender).DataContext is not SourceSlot slot) return;

        _renameOriginalDisplayName = slot.DisplayName;
        slot.IsRenaming = true;
        e.Handled = true;
    }

    // TextBox stays in the visual tree the whole time (only its Visibility toggles), so Loaded
    // won't refire on re-entering edit mode — IsVisibleChanged is what actually fires each time.
    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true } box) return;
        box.Focus();
        box.SelectAll();
    }

    private void RenameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        if (e.Key == Key.Escape)
        {
            if (box.DataContext is SourceSlot slot) slot.DisplayName = _renameOriginalDisplayName ?? slot.DisplayName;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // LostFocus below does the actual commit (including the blank-name fallback) —
            // this just ends the edit the same way clicking away would.
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: SourceSlot slot }) return;

        if (string.IsNullOrWhiteSpace(slot.DisplayName))
            slot.DisplayName = string.IsNullOrWhiteSpace(_renameOriginalDisplayName) ? "Layer" : _renameOriginalDisplayName;
        slot.IsRenaming = false;
    }

    private void PreviewContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var canvasHeight = ViewModel.SceneEditor.ActiveScene?.CanvasHeight ?? SourceSlot.DefaultCanvasWidth * 9.0 / 16.0;
        var scale = Math.Min(e.NewSize.Width / SourceSlot.DefaultCanvasWidth, e.NewSize.Height / canvasHeight);
        if (scale > 0) LabelScaleCorrection = 1.0 / scale;
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void UnlinkSceneSet_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveProfile is not null)
        {
            ViewModel.ActiveProfile.LinkedSceneSetId = null;
        }
    }

    private void OnFrameReceived(object? sender, VideoFrame frame)
    {
        // Guards against a corrupt/misparsed frame (e.g. a future protocol change) taking
        // down the whole app via WriteableBitmap's allocation overflow.
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > 16384 || frame.Height > 16384) return;
        if (frame.BgraPixels.Length < frame.Width * frame.Height * 4) return;

        // The core pushes frames at full capture fps on a background thread; if the UI
        // thread falls behind, drop newer frames rather than queueing an unbounded backlog.
        // Shared across the primary and every PiP thumbnail — a minor simplification that at
        // worst skips an occasional frame for one of them under load, never a correctness issue.
        if (_frameDispatchPending) return;
        _frameDispatchPending = true;

        // Render priority (not the default Normal, which outranks Render) so a live preview's
        // frequent, potentially large WritePixels calls (a 3440x1440 capture is ~20MB/frame)
        // never preempt the layout/paint passes an interactive window resize depends on to stay
        // responsive — without this, dragging to resize the window while a preview is active
        // felt extremely slow, since every incoming frame kept jumping the queue ahead of the
        // resize's own rendering work on the same UI thread.
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _frameDispatchPending = false;
            ViewModel.RecordPreviewFrameReceived();

            // The compositor's own (primary) output is always tagged "preview" regardless of
            // which source is actually primary — see native/crates/core/src/compositor.rs.
            // Anything else is a PiP's own raw frame, tagged with its real SourceId.
            if (frame.SourceId == "preview")
            {
                UpdateBitmap(ref _previewBitmap, frame, bmp => PreviewImage.Source = bmp);
                return;
            }

            var slot = ViewModel.SceneEditor.Slots.FirstOrDefault(s => s.SourceId == frame.SourceId);
            if (slot is null) return;

            // Gives the local live-thumbnail (video overlays are the only live-thumbnail kind
            // chromakey is exposed for) the same WYSIWYG preview the real composited/stream
            // output already gets — a per-frame CPU pass over this thumbnail's own pixels, not
            // the full canvas, so cost scales with the video overlay's decode size, not the
            // scene as a whole.
            if (slot.Content is IChromaKeyable { ChromaKeyEnabled: true } keyable)
            {
                OverlayContentRenderer.ApplyChromaKey(frame.BgraPixels, keyable.ChromaKeyColor, keyable.ChromaKeySimilarity);
            }

            var bitmap = slot.LiveThumbnail as WriteableBitmap;
            UpdateBitmap(ref bitmap, frame, bmp => slot.LiveThumbnail = bmp);
        }));
    }

    private static void UpdateBitmap(ref WriteableBitmap? bitmap, VideoFrame frame, Action<WriteableBitmap> assign)
    {
        if (bitmap is null || bitmap.PixelWidth != frame.Width || bitmap.PixelHeight != frame.Height)
        {
            bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            assign(bitmap);
        }

        bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.BgraPixels, frame.Width * 4, 0);
    }

}
