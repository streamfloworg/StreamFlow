using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using StreamFlow.App.Rendering;
using StreamFlow.App.Services;
using StreamFlow.App.Services.Core;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.AudioProperties;

using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace StreamFlow.App.Views.Pages;
public partial class GoLiveView
{
    private readonly HotkeyConflictService _hotkeyConflicts;
    private readonly IDialogService _dialogs;

    public GoLiveViewModel ViewModel
    {
        get;
    }

    private WriteableBitmap? _previewBitmap;
    private volatile bool _frameDispatchPending;

    // "Show Preview" (Option B of the Spout2 Integration Plan): while ViewModel.
    // IsSpoutOutputEnabled is on, the primary/composited preview comes from the Rust core's
    // GPU-shared Spout texture via D3DImage instead of the CPU pipe+WriteableBitmap path — see
    // SpoutPreviewRenderer's own doc comment for why. PiP thumbnails are unaffected either way
    // (OnFrameReceived below still handles those regardless of this toggle).
    private readonly SpoutPreviewRenderer _spoutRenderer = new();

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

    public GoLiveView(GoLiveViewModel viewModel, CoreBridgeService core, HotkeyConflictService hotkeyConflicts, IDialogService dialogs)
    {
        ViewModel = viewModel;
        _hotkeyConflicts = hotkeyConflicts;
        _dialogs = dialogs;
        DataContext = this;

        InitializeComponent();

        core.FrameReceived += OnFrameReceived;
        core.EventReceived += OnCoreEventReceived;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _spoutRenderer.Failed += OnSpoutPreviewFailed;

        // GoLiveViewModel.ApplySettings (already run by now — DI finishes constructing the
        // ViewModel before this View's constructor runs) sets the loaded IsSpoutOutputEnabled
        // value via its backing field directly rather than the generated property setter, so no
        // PropertyChanged fires for that initial value and the subscription above alone would
        // never see it. Sync once against whatever the value already is so a persisted "Show
        // Preview: on" is honored from app start, not just from a future manual toggle.
        SyncSpoutPreviewRenderingHook();
    }

    /// <summary>Starts audio monitor sessions for whatever's checked in Audio Sources — see
    /// GoLiveViewModel.OnNavigatedToAsync. Frame.Navigate doesn't drive the ViewModel's
    /// navigation hooks on its own in this app, so Loaded/Unloaded stand in for them (same
    /// pattern AudioView already uses for its own page-active tracking).</summary>
    private async void GoLiveView_Loaded(object sender, RoutedEventArgs e) => await ViewModel.OnNavigatedToAsync();

    private async void GoLiveView_Unloaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.OnNavigatedFromAsync();

        // Deliberately NOT unhooking CompositionTarget.Rendering or disposing _spoutRenderer here
        // anymore — GoLiveView is a DI singleton (App.xaml.cs), so Unloaded only means "navigated
        // to a different page," not app shutdown. Tearing the D3D device/texture down here (with
        // nothing in GoLiveView_Loaded to rebuild it) permanently killed the Spout preview the
        // moment the user visited any other page and came back: the Rust core only re-sends
        // SpoutTextureReadyEvent on a genuine (re)create or resolution change, not on request, so
        // there was nothing to trigger recovery — the preview just stayed frozen on whatever was
        // last rendered before the navigate-away, for the rest of the session, regardless of how
        // correctly scenes kept switching underneath it. The GPU cost of keeping one small D3D9Ex
        // device+texture alive for the app's lifetime is negligible; the OS reclaims it at process
        // exit regardless.
    }

    /// <summary>Swaps the primary preview's rendering path when "Show Preview" is toggled — see
    /// the field doc comment on <see cref="_spoutRenderer"/>. Runs on the UI thread already
    /// (ObservableObject's PropertyChanged is raised synchronously from whatever thread set the
    /// property, and IsSpoutOutputEnabled is only ever set from XAML/UI-thread code).</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GoLiveViewModel.IsSpoutOutputEnabled)) return;

        SyncSpoutPreviewRenderingHook();
    }

    private void SyncSpoutPreviewRenderingHook()
    {
        if (ViewModel.IsSpoutOutputEnabled)
        {
            CompositionTarget.Rendering += OnSpoutPreviewRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnSpoutPreviewRendering;
            SpoutPreviewImage.Source = null;
        }
    }

    /// <summary>Mirrors ScenesView.SceneHotkeyTextBoxPreviewKeyDown exactly (this page has its
    /// own copy of the Scene Name/properties section, so the hotkey field is duplicated here too)
    /// — see that method's own doc comment and PropertiesEditor.HotkeyTextBoxPreviewKeyDown for
    /// the shared capture-logic/conflict-check pattern this follows.</summary>
    private async void SceneHotkeyTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var scene = ViewModel.SceneEditor.ActiveScene;
        if (scene is null || e.Key == Key.Tab) return;

        e.Handled = true;

        var modifiers = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (modifiers == ModifierKeys.None && (key == Key.Delete || key == Key.Back || key == Key.Escape))
        {
            scene.SwitchHotkey = null;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.Clear or Key.OemClear or Key.Apps)
        {
            return;
        }

        var candidate = new Hotkey(key, modifiers);
        var conflict = _hotkeyConflicts.FindConflict(candidate, excludingOwner: scene);
        if (conflict is not null)
        {
            var proceed = await _dialogs.ConfirmAsync("Hotkey Conflict",
                $"{candidate} is already assigned to {conflict}. Assign it here too?\n\nBoth will trigger whenever this combo is pressed.",
                primaryText: "Assign Anyway", secondaryText: "Cancel");
            if (!proceed) return;
        }

        scene.SwitchHotkey = candidate;
    }

    /// <summary>Keeps a chat overlay's local preview pinned to its newest messages, matching
    /// OverlayContentRenderer.RenderChatToBgra's own explicit fit calculation — a ScrollViewer
    /// scrolled to its end has simple, unambiguous "show the tail, clip the rest" behavior, unlike
    /// relying on ItemsControl/Border alignment-based overflow clipping (which didn't reliably
    /// discard the *oldest* message first). Fires whenever the content's rendered height changes
    /// (a message added/trimmed, or a Style edit changing font size) — one handler instance shared
    /// by every chat overlay's own ScrollViewer, via the `sender` parameter.</summary>
    private void ChatScrollViewer_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 && sender is System.Windows.Controls.ScrollViewer scrollViewer)
            scrollViewer.ScrollToEnd();
    }

    // Polls the shared texture's latest content into the D3DImage once per composition frame —
    // see SpoutPreviewRenderer.MarkDirty's own doc comment for why this has to be a poll.
    // Also what feeds the PreviewFps stat while Show Preview is active: OnFrameReceived (the CPU
    // path's own counter) never fires for the primary preview in this mode since the Rust core
    // stops sending those frames over the pipe entirely (see run_data_pipe's spout_enabled
    // check) — without this call, PreviewFps just decays to ~0. This reports display refresh
    // rate (CompositionTarget.Rendering's own cadence) rather than the compositor's actual
    // publish rate, which isn't quite the same thing, but it's the closest available signal for
    // "is the preview actually updating" without plumbing Spout's own frame-ready semaphore all
    // the way into WPF.
    private void OnSpoutPreviewRendering(object? sender, EventArgs e)
    {
        _spoutRenderer.MarkDirty();
        ViewModel.RecordPreviewFrameReceived();
    }

    /// <summary>Opens/re-opens the Spout shared texture whenever the core (re)creates it — first
    /// enable, or a resolution change. <see cref="CoreBridgeService.EventReceived"/> fires on a
    /// thread-pool thread, so the actual D3D/D3DImage work is marshaled to the UI thread.</summary>
    private void OnCoreEventReceived(object? sender, CoreEvent evt)
    {
        if (evt is not SpoutTextureReadyEvent tex) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            var hwnd = new WindowInteropHelper(Window.GetWindow(this)).Handle;
            if (hwnd == IntPtr.Zero) return;

            _spoutRenderer.UpdateTexture(hwnd, tex.ShareHandle, tex.Width, tex.Height, tex.AdapterLuid);
            if (ViewModel.IsSpoutOutputEnabled)
            {
                SpoutPreviewImage.Source = _spoutRenderer.Image;
            }
        }));
    }

    /// <summary>Surfaces a D3D/D3DImage failure via the existing error banner instead of the
    /// silent no-op it was previously (see SpoutPreviewRenderer.Failed's own doc comment) — this
    /// is a background/D3D-callback-adjacent path, so marshal to the UI thread before touching
    /// ViewModel state.</summary>
    private void OnSpoutPreviewFailed(object? sender, Exception ex)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            ViewModel.ErrorMessage = $"Show Preview (Spout) failed: {ex.Message}";
        }));
    }

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

    private void NewGroupFromSelected_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Parent is System.Windows.Controls.ContextMenu contextMenu)
        {
            if (contextMenu.PlacementTarget is System.Windows.Controls.TreeView treeView)
            {
                var selected = ViewModel.SceneEditor.SelectedSlot;
                if (selected is not null && selected.OverlayKind != OverlayKind.Group && selected.OverlayKind != OverlayKind.Alert && !selected.IsPrimary)
                {
                    var others = ViewModel.SceneEditor.FlattenedSlots
                        .Where(s => s.IsInSelectedGroup && s != selected)
                        .ToList();
                    if (others.Count > 0)
                    {
                        var list = new List<SourceSlot> { selected };
                        list.AddRange(others);
                        ViewModel.SceneEditor.GroupSlots(list);
                    }
                }
            }
            else if (contextMenu.PlacementTarget is System.Windows.Controls.ListBox listBox)
            {
                var selectedSlots = listBox.SelectedItems.Cast<SourceSlot>()
                    .Where(s => s.OverlayKind != OverlayKind.Group && !s.IsPrimary)
                    .ToList();
                if (selectedSlots.Count > 1)
                {
                    ViewModel.SceneEditor.GroupSlots(selectedSlots);
                }
            }
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
                // While Show Preview is on, PreviewImage.Source belongs to the D3DImage-backed
                // Spout texture (see OnCoreEventReceived/_spoutRenderer) — the Rust side already
                // stops sending composited frames over the pipe in that case (see
                // run_data_pipe's spout_enabled check), but this guard makes the two paths
                // independently correct rather than relying on that timing: even a frame that
                // slipped through mid-toggle can't stomp the GPU-driven preview back to the CPU
                // bitmap.
                if (!ViewModel.IsSpoutOutputEnabled)
                {
                    UpdateBitmap(ref _previewBitmap, frame, bmp => PreviewImage.Source = bmp);
                }
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

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SourceSlot slot)
        {
            ViewModel.SceneEditor.SelectedSlot = slot;
        }
        else
        {
            ViewModel.SceneEditor.SelectedSlot = null;
        }
    }

}
