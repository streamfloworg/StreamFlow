using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging;

using StreamFlow.App.Controls;
using StreamFlow.App.Rendering;
using StreamFlow.App.Services;
using StreamFlow.App.Services.Core;
using StreamFlow.Core.AudioProperties;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>How a scene switch animates in the actual composited/streamed output. Converted to a
/// plain lowercase wire string (not serialized as this enum directly) when sent to Core — see
/// SceneEditorViewModel.TransitionKindToWire.</summary>
public enum SceneTransitionKind { Cut, Fade, SlideLeft, SlideRight, SlideUp, SlideDown }

/// <summary>Scene/layer editing shared between the Go Live and Scenes pages: the Scenes
/// collection, each scene's Slots (capture sources + overlays), Add Layer commands, layer
/// property edits, aspect-ratio/canvas-size sync, and the capture-session lifecycle tied to
/// which scene is currently active. Registered as a DI singleton so both pages observe and edit
/// the exact same live state.
///
/// Deliberately has no persistence, streaming, or chat logic of its own — those remain
/// GoLiveViewModel-only concerns (this class has no concept of "unsaved changes" or "is
/// streaming"). Instead it exposes three narrow events so GoLiveViewModel can react without this
/// class needing to know anything about GoLiveViewModel's other partials:
/// <see cref="Changed"/> (something that used to call ScheduleSaveSettings happened),
/// <see cref="SlotAvailabilityChanged"/> (Slots/ActiveScene changed in a way Start-Stream's
/// CanExecute should re-check), and <see cref="ChatOverlayStateChanged"/> (a chat overlay was
/// added/removed or the active scene changed, so the chat connection should be re-evaluated —
/// kept separate from Changed since that fires on every drag tick and reconnecting chat that
/// often would cause visible churn).</summary>
public partial class SceneEditorViewModel : ObservableObject
{
    private readonly CoreBridgeService _core;
    private readonly IDialogService _dialogs;
    private readonly SceneSetService _sceneSetService;
    private readonly EventBus _eventBus;
    private readonly ILogger<SceneEditorViewModel> _logger;

    /// <summary>Tracked purely for chat-overlay placeholder content (see
    /// ChatOverlayContent.PlaceholderMessages) — this class otherwise deliberately has no concept
    /// of "is streaming" (see this class's own top doc comment), but needs to know whether it's
    /// safe to show fake chat activity without risking a viewer ever seeing it, which the
    /// GoLiveStartedEvent/GoLiveStoppedEvent subscriptions below exist specifically to answer
    /// without actually depending on GoLiveViewModel.IsStreaming directly.</summary>
    private bool _isLive;

    public SceneEditorViewModel(CoreBridgeService core, IDialogService dialogs, SceneSetService sceneSetService, EventBus eventBus, ILogger<SceneEditorViewModel> logger)
    {
        _core = core;
        _dialogs = dialogs;
        _sceneSetService = sceneSetService;
        _eventBus = eventBus;
        _logger = logger;

        Scenes.CollectionChanged += (_, _) => RemoveActiveSceneCommand.NotifyCanExecuteChanged();

        // Event System/Triggers: any timer overlay slot (in any scene, not just the active one —
        // starting it doesn't depend on that scene being on screen, only rendering does) marked
        // AutoStartOnGoLive begins counting the moment the core confirms the stream is actually
        // live. See EventBus's own doc comment for why this goes through the bus rather than
        // GoLiveViewModel calling a method on this class directly — other future subscribers
        // (Stream Deck, later triggers) can hook GoLiveStartedEvent without this class or
        // GoLiveViewModel needing to know about each other's existence.
        _eventBus.Subscribe<GoLiveStartedEvent>(_ =>
        {
            _isLive = true;
            foreach (var scene in Scenes)
            foreach (var slot in scene.Slots)
                if (slot.Content is TimerOverlayContent { AutoStartOnGoLive: true, IsTimerRunning: false } && StartTimerCommand.CanExecute(slot))
                    StartTimerCommand.ExecuteAsync(slot);

            // Wipe any placeholder chat content the instant we actually go live — never let a
            // viewer see fake chat activity, even for the brief window before real messages (if
            // any) start arriving. Real content already in progress (IsShowingPlaceholder false)
            // is untouched.
            foreach (var scene in Scenes)
            foreach (var slot in scene.Slots)
                if (slot.Content is ChatOverlayContent { IsShowingPlaceholder: true } chat)
                {
                    chat.ChatMessages.Clear();
                    chat.IsShowingPlaceholder = false;
                    if (slot.SourceId is not null) ScheduleOverlayContentUpdate(slot);
                }
        });

        _eventBus.Subscribe<GoLiveStoppedEvent>(_ =>
        {
            _isLive = false;
            foreach (var scene in Scenes)
            foreach (var slot in scene.Slots)
                if (slot.Content is ChatOverlayContent { ChatMessages.Count: 0 } chat)
                    PopulateChatPlaceholder(slot, chat);
        });
    }

    /// <summary>Fills an empty, idle chat overlay with sample messages so its text-style controls
    /// and the placement canvas have something to actually preview — see
    /// ChatOverlayContent.PlaceholderMessages. No-op while live (never risk a viewer seeing fake
    /// chat activity) or if real messages are already present.</summary>
    private void PopulateChatPlaceholder(SourceSlot slot, ChatOverlayContent chat)
    {
        if (_isLive || chat.ChatMessages.Count > 0) return;

        foreach (var message in ChatOverlayContent.PlaceholderMessages)
            chat.ChatMessages.Add(message);
        chat.IsShowingPlaceholder = true;
        if (slot.SourceId is not null) ScheduleOverlayContentUpdate(slot);
    }

    public event Action? Changed;
    private void NotifyChanged()
    {
        HasUnsavedChanges = true;
        Changed?.Invoke();
    }

    public event Action? SlotAvailabilityChanged;
    private void NotifySlotAvailabilityChanged() => SlotAvailabilityChanged?.Invoke();

    public event Action? ChatOverlayStateChanged;
    private void NotifyChatOverlayStateChanged() => ChatOverlayStateChanged?.Invoke();

    public ObservableCollection<NativeCaptureSource> AvailableSources { get; } = [];

    public ObservableCollection<GoLiveSceneViewModel> Scenes { get; } = [];

    /// <summary>Every Scene Set the app currently knows about (imported .sfset archives) —
    /// shared so the Scenes page can pick one to load independently of whatever Go Live's
    /// active streaming profile happens to be linked to.</summary>
    public ObservableCollection<SceneSetRegistration> RegisteredSceneSets { get; } = [];

    /// <summary>Which registered Scene Set's content currently populates <see cref="Scenes"/>,
    /// if any — null means the content is either Go Live's local/unlinked default scenes or
    /// nothing has been loaded yet. Set by Go Live's own profile-linked loading (Streaming.cs)
    /// and by this class's own <see cref="LoadSceneSetForEditingAsync"/>.</summary>
    [ObservableProperty]
    private SceneSetRegistration? _activeSceneSet;

    /// <summary>Editable Scene Set-level metadata — always present (unlike ActiveSceneSet, which
    /// is null for a freshly created/unregistered layout), so the Scenes page's Name/Author
    /// fields have something to bind to regardless of whether the current layout has ever been
    /// saved/registered yet. Kept in sync with ActiveSceneSet.Name/Author (when one exists) via
    /// the OnChanged hooks below, so later reads of ActiveSceneSet.Name/Author (dialog messages,
    /// suggested export filenames, SaveActiveSceneSet) see the latest edited value.</summary>
    [ObservableProperty]
    private string _sceneSetName = "New Scene Set";

    [ObservableProperty]
    private string _sceneSetAuthor = "";

    /// <summary>Suppresses the dirty-marking side effect below while SceneSetName/Author are
    /// being set programmatically to reflect a freshly loaded registration (see
    /// SetSceneSetMetadataFromRegistration) — without this, loading a Scene Set would itself
    /// flip HasUnsavedChanges to true, spuriously enabling the Scenes page's own Save button
    /// before the user has actually edited anything.</summary>
    private bool _isLoadingSceneSetMetadata;

    partial void OnSceneSetNameChanged(string value)
    {
        if (ActiveSceneSet is not null) ActiveSceneSet.Name = value;
        if (!_isLoadingSceneSetMetadata) NotifyChanged();
    }

    partial void OnSceneSetAuthorChanged(string value)
    {
        if (ActiveSceneSet is not null) ActiveSceneSet.Author = value;
        if (!_isLoadingSceneSetMetadata) NotifyChanged();
    }

    /// <summary>Sets SceneSetName/Author to reflect a freshly loaded (or absent) registration,
    /// without marking HasUnsavedChanges — shared by this class's own
    /// <see cref="LoadSceneSetForEditingAsync"/> and Go Live's separate scene-set loading path
    /// (GoLiveViewModel.Streaming.cs's LoadSceneSetAsync), since both populate this same shared
    /// Scenes/ActiveScene state through different flows.</summary>
    public void SetSceneSetMetadataFromRegistration(SceneSetRegistration? reg)
    {
        _isLoadingSceneSetMetadata = true;
        try
        {
            SceneSetName = reg?.Name ?? "New Scene Set";
            SceneSetAuthor = reg?.Author ?? "";
        }
        finally
        {
            _isLoadingSceneSetMetadata = false;
        }
    }

    /// <summary>How switching <see cref="ActiveScene"/> animates in the actual composited/
    /// streamed output (not just the local preview) — session-wide, not per-scene or per-layer,
    /// applied by <see cref="ActivateSceneAsync"/> to every scene switch. Persisted globally in
    /// GoLiveSettings (see GoLiveViewModel.ApplySettings/BuildSettingsSnapshot).</summary>
    [ObservableProperty]
    private SceneTransitionKind _transitionKind = SceneTransitionKind.Cut;

    [ObservableProperty]
    private int _transitionDurationMs = 400;

    /// <summary>Wire-format string for a <see cref="SceneTransitionKind"/>, matching the Rust
    /// side's `#[serde(rename_all = "snake_case")]` `TransitionKind` enum exactly. Public (and its
    /// inverse below) so GoLiveViewModel's settings load/save can convert at the persistence
    /// boundary without this ViewModel needing to know anything about GoLiveSettings.</summary>
    public static string TransitionKindToWire(SceneTransitionKind kind) => kind switch
    {
        SceneTransitionKind.Fade => "fade",
        SceneTransitionKind.SlideLeft => "slide_left",
        SceneTransitionKind.SlideRight => "slide_right",
        SceneTransitionKind.SlideUp => "slide_up",
        SceneTransitionKind.SlideDown => "slide_down",
        _ => "cut",
    };

    /// <summary>Inverse of <see cref="TransitionKindToWire"/> — used when restoring
    /// GoLiveSettings.TransitionKind (a plain persisted string) back into this enum. Unrecognized
    /// values fall back to Cut rather than throwing, same defensive convention as the rest of
    /// GoLiveSettingsService's loading.</summary>
    public static SceneTransitionKind TransitionKindFromWire(string? wire) => wire switch
    {
        "fade" => SceneTransitionKind.Fade,
        "slide_left" => SceneTransitionKind.SlideLeft,
        "slide_right" => SceneTransitionKind.SlideRight,
        "slide_up" => SceneTransitionKind.SlideUp,
        "slide_down" => SceneTransitionKind.SlideDown,
        _ => SceneTransitionKind.Cut,
    };

    /// <summary>Pending edits to <see cref="Scenes"/> not yet written to the currently loaded
    /// Scene Set's files via <see cref="SaveActiveSceneSet"/> — distinct from Go Live's own
    /// HasUnsavedChanges (which covers the whole settings file, profiles included, and is only
    /// flushed via its "Save Layout" button); this one is scoped to just the Scene Set content
    /// itself, for the Scenes page's own Save action.</summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private GoLiveSceneViewModel? _activeScene;

    [ObservableProperty]
    private GoLiveSceneViewModel? _defaultScene;

    public bool IsActiveSceneDefault => ActiveScene is not null && ReferenceEquals(ActiveScene, DefaultScene);

    public ObservableCollection<SourceSlot> Slots => ActiveScene?.Slots ?? _emptySlots;

    private static readonly ObservableCollection<SourceSlot> _emptySlots = [];

    /// <summary>Whichever slot in the active scene is flagged primary, if any — decoupled from
    /// position (primary is no longer forced to occupy index 0, see BringLayerForward/
    /// SendLayerBackward). Null for a primary-less scene. Raised alongside Slots wherever slots
    /// are added/removed/the active scene changes.</summary>
    public SourceSlot? PrimarySlot => Slots.FirstOrDefault(s => s.IsPrimary);

    public IEnumerable<SourceSlot> AvailableGroupCandidates =>
        ActiveScene?.Slots.Where(s => s != SelectedSlot && s.OverlayKind != OverlayKind.Group && !s.IsPrimary) ?? [];

    [ObservableProperty]
    private SourceSlot? _selectedSlot;

    [RelayCommand]
    private void SelectSlot(SourceSlot slot) => SelectedSlot = slot;

    private bool _isUpdatingSelection;

    partial void OnSelectedSlotChanged(SourceSlot? oldValue, SourceSlot? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;

        _isUpdatingSelection = true;
        try
        {
            if (ActiveScene is not null)
            {
                var selectedGroup = newValue?.Content as GroupOverlayContent;
                foreach (var slot in ActiveScene.Slots)
                {
                    slot.IsInSelectedGroup = selectedGroup?.Children.Contains(slot) ?? false;
                }
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }

        OnPropertyChanged(nameof(AvailableGroupCandidates));
    }

    public void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_core.State == CoreState.Running)
            _ = _core.SendCommandAsync(new GetSourcesCommand());
    }

    public GoLiveSceneViewModel CreateBlankScene(string name)
    {
        var scene = new GoLiveSceneViewModel(Guid.NewGuid().ToString("N"), name);
        scene.PropertyChanged += OnScenePropertyChanged;
        return scene;
    }

    public SourceSlot BuildSlotFromSettings(SlotSettings savedSlot)
    {
        OverlayKind? overlayKind = savedSlot.OverlayKind
            ?? (savedSlot.ImagePath is not null ? OverlayKind.Image
                : savedSlot.OverlayText is not null ? OverlayKind.Text
                : savedSlot.OverlayColorHex is not null ? OverlayKind.Color
                : null);

        var overlayColor = savedSlot.OverlayColorHex is not null
            ? System.Windows.Media.ColorConverter.ConvertFromString(savedSlot.OverlayColorHex) as System.Windows.Media.Color?
            : null;
        var chromaKeyColor = savedSlot.ChromaKeyColorHex is not null
            && System.Windows.Media.ColorConverter.ConvertFromString(savedSlot.ChromaKeyColorHex) is System.Windows.Media.Color parsedChromaColor
            ? parsedChromaColor
            : (System.Windows.Media.Color?)null;

        IOverlayContent? content = overlayKind switch
        {
            OverlayKind.Image => new ImageOverlayContent
            {
                ImagePath = savedSlot.ImagePath,
                ChromaKeyEnabled = savedSlot.ChromaKeyEnabled,
                ChromaKeySimilarity = savedSlot.ChromaKeySimilarity,
                ChromaKeyColor = chromaKeyColor ?? System.Windows.Media.Color.FromRgb(0x00, 0xB1, 0x40),
            },
            OverlayKind.Text => new TextOverlayContent { OverlayText = savedSlot.OverlayText },
            OverlayKind.Color => new ColorOverlayContent { OverlayColor = overlayColor },
            OverlayKind.Video => new VideoOverlayContent
            {
                VideoPath = savedSlot.VideoPath,
                LoopVideo = savedSlot.LoopVideo,
                ChromaKeyEnabled = savedSlot.ChromaKeyEnabled,
                ChromaKeySimilarity = savedSlot.ChromaKeySimilarity,
                ChromaKeyColor = chromaKeyColor ?? System.Windows.Media.Color.FromRgb(0x00, 0xB1, 0x40),
            },
            OverlayKind.Chat => new ChatOverlayContent(),
            OverlayKind.Blur => new BlurOverlayContent { BlurRadius = savedSlot.BlurRadius },
            OverlayKind.Timer => new TimerOverlayContent
            {
                TimerMode = savedSlot.TimerMode,
                TimerDurationSeconds = savedSlot.TimerDurationSeconds,
                AutoStartOnGoLive = savedSlot.TimerAutoStartOnGoLive,
            },
            OverlayKind.Group => new GroupOverlayContent(),
            _ => null,
        };
        if (content is IHasTextStyle hasStyle)
            ApplyTextStyleFromSettings(hasStyle.Style, savedSlot);

        var slot = new SourceSlot(
            savedSlot.IsPrimary, savedSlot.XPercent, savedSlot.YPercent, savedSlot.WPercent, savedSlot.HPercent,
            savedSlot.IsOverlay, content)
        {
            CornerRadiusPercent = savedSlot.CornerRadiusPercent,
            OpacityPercent = savedSlot.OpacityPercent,
            RotationDegrees = savedSlot.RotationDegrees,
        };
        if (slot.IsChatOverlay || slot.IsBlurOverlay || slot.OverlayKind == OverlayKind.Group)
        {
            slot.IsAspectLocked = false;
        }
        slot.PropertyChanged += OnSlotPropertyChanged;
        HookContentPropertyChanged(slot);

        if (slot.IsStaticOverlay && savedSlot.SourceId is not null)
        {
            slot.SourceId = savedSlot.SourceId;
            slot.DisplayName = savedSlot.DisplayName ?? (slot.IsImageOverlay ? "Image Overlay"
                : slot.IsTextOverlay ? "Text Overlay"
                : slot.IsChatOverlay ? "Chat Overlay"
                : slot.IsBlurOverlay ? "Blur Layer"
                : slot.IsTimerOverlay ? "Timer Overlay"
                : slot.IsColorOverlay ? "Color Overlay"
                : slot.OverlayKind == OverlayKind.Group ? "Overlay Group"
                : overlayColor?.ToString(CultureInfo.InvariantCulture) ?? "");
            if (content is ChatOverlayContent chatContentToRestore)
                PopulateChatPlaceholder(slot, chatContentToRestore);

            _ = RestoreStaticOverlayAsync(slot, savedSlot.SourceId);
            ScheduleChromaKeyPreviewUpdate(slot);
        }
        else
        {
            if (slot.IsVideoOverlay)
                slot.DisplayName = savedSlot.DisplayName ?? "Video Overlay";
            else if (slot.OverlayKind == OverlayKind.Group)
                slot.DisplayName = savedSlot.DisplayName ?? "Overlay Group";

            slot.SourceId = savedSlot.SourceId;
        }

        return slot;
    }

    public GoLiveSceneViewModel BuildSceneFromSettings(SceneSettings saved)
    {
        var scene = new GoLiveSceneViewModel(saved.Id, saved.Name)
        {
            CanvasResolutionWidth = saved.CanvasResolutionWidth,
            CanvasResolutionHeight = saved.CanvasResolutionHeight,
            SwitchHotkey = saved.SwitchHotkeyKey is not null && saved.SwitchHotkeyModifiers is not null
                && Enum.TryParse<System.Windows.Input.Key>(saved.SwitchHotkeyKey, out var savedKey)
                && Enum.TryParse<System.Windows.Input.ModifierKeys>(saved.SwitchHotkeyModifiers, out var savedModifiers)
                ? new Hotkey(savedKey, savedModifiers)
                : null,
        };
        scene.PropertyChanged += OnScenePropertyChanged;

        var slotsList = new List<SourceSlot>();
        foreach (var savedSlot in saved.Slots)
        {
            var slot = BuildSlotFromSettings(savedSlot);
            slotsList.Add(slot);
        }

        // Resolve group children
        foreach (var slot in slotsList)
        {
            if (slot.Content is GroupOverlayContent group)
            {
                var savedSlot = saved.Slots.FirstOrDefault(s => s.SourceId == slot.SourceId || (s.DisplayName == slot.DisplayName && s.OverlayKind == OverlayKind.Group));
                if (savedSlot?.GroupChildIds is not null)
                {
                    foreach (var childId in savedSlot.GroupChildIds)
                    {
                        var childSlot = slotsList.FirstOrDefault(s => s.SourceId == childId);
                        if (childSlot is not null)
                        {
                            group.Children.Add(childSlot);
                            childSlot.ParentGroup = slot;
                        }
                    }
                }
            }
        }

        foreach (var slot in slotsList)
        {
            scene.Slots.Add(slot);
        }

        return scene;
    }

    /// <summary>Subscribes to a freshly-constructed slot's Content so edits to its kind-specific
    /// payload (ImagePath, OverlayText, ChromaKeyEnabled, etc.) route through
    /// <see cref="OnSlotContentPropertyChanged"/> the same way slot-level property edits route
    /// through <see cref="OnSlotPropertyChanged"/>. Content is set once at construction and never
    /// reassigned afterward in practice (an overlay's kind doesn't change post-creation — you'd
    /// remove and re-add instead), so a single one-time subscription here (alongside wherever
    /// slot.PropertyChanged itself gets wired) is sufficient; no unsubscribe/re-subscribe
    /// machinery is needed the way OnContentChanged would otherwise require.</summary>
    private void HookContentPropertyChanged(SourceSlot slot)
    {
        if (slot.Content is System.ComponentModel.INotifyPropertyChanged notifying)
            notifying.PropertyChanged += (_, e) => OnSlotContentPropertyChanged(slot, e);

        // TextStyle is a separate nested ObservableObject (see IHasTextStyle), so edits to it
        // (FontSize, FontColor, etc.) raise PropertyChanged on Style itself, not on Content —
        // without this second subscription they'd never reach OnSlotContentPropertyChanged at
        // all, silently breaking re-render/re-save for every text-formatting edit.
        if (slot.Content is IHasTextStyle hasStyle)
            hasStyle.Style.PropertyChanged += (_, e) => OnSlotContentPropertyChanged(slot, e);
    }

    /// <summary>Applies the persisted shared text-formatting fields (see SlotSettings' Text*
    /// fields) onto a freshly-constructed TextStyle — used for any of the three IHasTextStyle
    /// kinds (Text/Chat/Timer) when restoring a saved scene. TextFontSize/TextFontColorHex/
    /// TextIsBold predate TextStyle and were Text-overlay-only; kept under their original names
    /// for backward compatibility with already-saved scene files, now just applied more broadly.</summary>
    private static void ApplyTextStyleFromSettings(TextStyle style, SlotSettings s)
    {
        style.FontFamily = string.IsNullOrWhiteSpace(s.TextFontFamily) ? "Segoe UI" : s.TextFontFamily;
        style.FontSize = s.TextFontSize ?? 48;
        style.FontColor = s.TextFontColorHex is not null
            && System.Windows.Media.ColorConverter.ConvertFromString(s.TextFontColorHex) is System.Windows.Media.Color parsedFontColor
            ? parsedFontColor
            : System.Windows.Media.Colors.White;
        style.IsBold = s.TextIsBold ?? true;
        style.IsItalic = s.TextIsItalic ?? false;
        style.Alignment = Enum.TryParse<TextHorizontalAlignment>(s.TextAlignment, out var alignment) ? alignment : TextHorizontalAlignment.Left;
        style.OutlineEnabled = s.TextOutlineEnabled ?? false;
        style.OutlineColor = s.TextOutlineColorHex is not null
            && System.Windows.Media.ColorConverter.ConvertFromString(s.TextOutlineColorHex) is System.Windows.Media.Color parsedOutlineColor
            ? parsedOutlineColor
            : System.Windows.Media.Colors.Black;
        style.OutlineThickness = s.TextOutlineThickness ?? 2;
    }

    /// <summary>Deep-copies a slot's Content for DuplicateActiveScene — sharing the same
    /// instance between the original and duplicate slots would mean editing one's ImagePath (or
    /// any other content property) silently mutates the other's too.</summary>
    private static IOverlayContent? CloneContent(IOverlayContent? content)
    {
        IOverlayContent? cloned = content switch
        {
            ImageOverlayContent img => new ImageOverlayContent
            {
                ImagePath = img.ImagePath,
                ChromaKeyEnabled = img.ChromaKeyEnabled,
                ChromaKeyColor = img.ChromaKeyColor,
                ChromaKeySimilarity = img.ChromaKeySimilarity,
            },
            TextOverlayContent text => new TextOverlayContent { OverlayText = text.OverlayText },
            ColorOverlayContent color => new ColorOverlayContent { OverlayColor = color.OverlayColor },
            VideoOverlayContent video => new VideoOverlayContent
            {
                VideoPath = video.VideoPath,
                LoopVideo = video.LoopVideo,
                ChromaKeyEnabled = video.ChromaKeyEnabled,
                ChromaKeyColor = video.ChromaKeyColor,
                ChromaKeySimilarity = video.ChromaKeySimilarity,
            },
            ChatOverlayContent => new ChatOverlayContent(),
            BlurOverlayContent blur => new BlurOverlayContent { BlurRadius = blur.BlurRadius },
            TimerOverlayContent timer => new TimerOverlayContent
            {
                TimerMode = timer.TimerMode,
                TimerDurationSeconds = timer.TimerDurationSeconds,
                AutoStartOnGoLive = timer.AutoStartOnGoLive,
            },
            _ => null,
        };

        // Style lives on a separate nested object now (see IHasTextStyle) — object-initializer
        // syntax above can't reach it (Style has no setter, only a getter), so copy its fields
        // across explicitly instead, same as every other content property above.
        if (content is IHasTextStyle sourceStyle && cloned is IHasTextStyle clonedStyle)
        {
            clonedStyle.Style.FontFamily = sourceStyle.Style.FontFamily;
            clonedStyle.Style.FontSize = sourceStyle.Style.FontSize;
            clonedStyle.Style.FontColor = sourceStyle.Style.FontColor;
            clonedStyle.Style.IsBold = sourceStyle.Style.IsBold;
            clonedStyle.Style.IsItalic = sourceStyle.Style.IsItalic;
            clonedStyle.Style.Alignment = sourceStyle.Style.Alignment;
            clonedStyle.Style.OutlineEnabled = sourceStyle.Style.OutlineEnabled;
            clonedStyle.Style.OutlineColor = sourceStyle.Style.OutlineColor;
            clonedStyle.Style.OutlineThickness = sourceStyle.Style.OutlineThickness;
        }

        return cloned;
    }

    private async Task RestoreStaticOverlayAsync(SourceSlot slot, string sourceId)
    {
        var rendered = slot.Content is ImageOverlayContent { ImagePath: not null } image ? OverlayContentRenderer.DecodeImageToBgra(image.ImagePath, GetImageOverlayCapSize(slot))
            : slot.Content is TextOverlayContent { OverlayText: not null } text ? OverlayContentRenderer.RenderTextToBgra(text.OverlayText, text.Style)
            : slot.Content is ColorOverlayContent { OverlayColor: System.Windows.Media.Color color } ? OverlayContentRenderer.RenderColorToBgra(color)
            : slot.IsChatOverlay ? OverlayContentRenderer.RenderChatToBgra(slot)
            : slot.Content is TimerOverlayContent timerStyle && slot.IsTimerOverlay ? OverlayContentRenderer.RenderTextToBgra(FormatTimerDisplay(slot), timerStyle.Style)
            : ((int Width, int Height, byte[] Pixels)?)null;
        if (rendered is not var (width, height, pixels)) return;

        // Timer included now, same as Text — its rendered digits get the same aspect-ratio
        // tracking (see the AddTimerOverlayAsync/BuildSceneFromSettings comments on
        // IsAspectLocked). This does mean a digit-count change (9:59→10:00, 59:59→1:00:00) can
        // trigger a small resize on that tick, same mechanism Text already has for any content
        // edit — accepted as consistent with "match Text's behavior" rather than special-cased
        // to only run once at creation. trackNaturalSize=true specifically for IHasTextStyle
        // content (Text/Timer) — see ApplyRenderedAspectRatio's own doc comment for why Image
        // deliberately doesn't get the same treatment.
        if (!slot.IsColorOverlay && !slot.IsChatOverlay)
            OverlayContentRenderer.ApplyRenderedAspectRatio(slot, width, height, trackNaturalSize: slot.Content is IHasTextStyle);
        await RegisterStaticOverlayAsync(sourceId, width, height, pixels);
    }

    /// <summary>Computes a Timer overlay slot's current display string from its elapsed/remaining
    /// seconds — shared by RestoreStaticOverlayAsync (both the tick-driven re-render and the
    /// scene-activation restore) so there's a single source of truth for the formatting. `mm:ss`
    /// normally, `hh:mm:ss` once the value passes an hour.</summary>
    private static string FormatTimerDisplay(SourceSlot slot)
    {
        if (slot.Content is not TimerOverlayContent timer) return "";

        var running = timer.IsTimerRunning && timer.TimerStartedAtUtc is DateTime started
            ? (DateTime.UtcNow - started).TotalSeconds
            : 0;
        var elapsed = timer.TimerElapsedBaseSeconds + running;

        var totalSeconds = timer.TimerMode == TimerMode.CountDown
            ? Math.Max(0, timer.TimerDurationSeconds - elapsed)
            : Math.Max(0, elapsed);

        var ts = TimeSpan.FromSeconds(totalSeconds);
        var text = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
        timer.TimerDisplayText = text; // Drives the local canvas's own live WYSIWYG preview.
        return text;
    }

    /// <summary>Resolves the primary slot's actual capture resolution — the same value the
    /// core uses as its composited output size — from whatever AvailableSources currently
    /// reports for it. Null until the source list has resolved at least once.</summary>
    public (uint Width, uint Height)? GetPrimaryResolution()
    {
        var primary = Slots.FirstOrDefault(s => s.IsPrimary);
        if (primary?.SourceId is not string sourceId) return null;

        var source = AvailableSources.FirstOrDefault(s => s.Id == sourceId);
        return source is { Width: > 0, Height: > 0 } ? (source.Width, source.Height) : null;
    }

    /// <summary>Max decode size (with headroom) for an image overlay slot, derived from its own
    /// configured percentage box against the primary's current actual resolution — the same
    /// math the core uses for the destination rect, so a static image's source buffer stays in
    /// a sane range relative to where it'll actually render instead of being whatever size the
    /// source file happens to be.</summary>
    private (int Width, int Height)? GetImageOverlayCapSize(SourceSlot slot)
    {
        if (GetPrimaryResolution() is not var (primaryW, primaryH)) return null;

        var maxW = (int)(slot.WPercent / 100.0 * primaryW * OverlayContentRenderer.ImageOverlayCapHeadroom);
        var maxH = (int)(slot.HPercent / 100.0 * primaryH * OverlayContentRenderer.ImageOverlayCapHeadroom);
        return maxW > 0 && maxH > 0 ? (maxW, maxH) : null;
    }

    /// <summary>Re-renders and re-registers every static overlay in every scene against the
    /// primary's now-current resolution — see GoLiveViewModel's SourcesEvent handler, which
    /// calls this only when that resolution actually changed since last observed.</summary>
    public async Task RefreshStaticOverlaySizesAsync()
    {
        foreach (var slot in Scenes.SelectMany(sc => sc.Slots).Where(s => s.IsStaticOverlay && s.SourceId is not null))
        {
            await RestoreStaticOverlayAsync(slot, slot.SourceId!);
        }
    }

    private void OnScenePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GoLiveSceneViewModel.Name) or nameof(GoLiveSceneViewModel.SwitchHotkey))
            NotifyChanged();
    }

    [RelayCommand]
    private void BringLayerForward(SourceSlot slot)
    {
        var index = Slots.IndexOf(slot);
        if (index >= 0 && index < Slots.Count - 1)
        {
            Slots.Move(index, index + 1);
            ScheduleLiveConfigPush();
            NotifyChanged();
        }
    }

    [RelayCommand]
    private void SendLayerBackward(SourceSlot slot)
    {
        var index = Slots.IndexOf(slot);
        if (index > 0)
        {
            Slots.Move(index, index - 1);
            ScheduleLiveConfigPush();
            NotifyChanged();
        }
    }

    /// <summary>Called after a drag-reorder in the Layers list (see SlotReorderBehavior) — the
    /// Move itself already happened, this just applies the same side effects BringLayerForward/
    /// SendLayerBackward get from the toolbar, so dragging to reorder actually pushes the new
    /// order live instead of only updating the local editor.</summary>
    [RelayCommand]
    private void NotifySlotsReordered()
    {
        ScheduleLiveConfigPush();
        NotifyChanged();
    }

    [RelayCommand]
    private void AddCaptureSource()
    {
        var hasPrimary = Slots.Any(s => s.IsPrimary);
        if (!hasPrimary)
        {
            AttachSlot(new SourceSlot(isPrimary: true, x: 0, y: 0, w: 100, h: 100));
        }
        else
        {
            AttachSlot(new SourceSlot(isPrimary: false, x: 65, y: 65, w: 30, h: 30));
        }
        NotifyChanged();
    }

    [RelayCommand]
    private async Task AddImageOverlayAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Overlay Image",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        var rendered = OverlayContentRenderer.DecodeImageToBgra(dialog.FileName);
        if (rendered is null)
        {
            await _dialogs.WarningAsync("Add Image Overlay", "Couldn't read that image file.");
            return;
        }
        var (width, height, pixels) = rendered.Value;

        var sourceId = $"overlay:{Guid.NewGuid():N}";
        var slot = new SourceSlot(
            isPrimary: false, x: 30, y: 30, w: 30, h: 30,
            isOverlay: true, content: new ImageOverlayContent { ImagePath = dialog.FileName });
        AttachSlot(slot);

        slot.DisplayName = "Image Overlay";
        OverlayContentRenderer.ApplyRenderedAspectRatio(slot, width, height);
        slot.SourceId = sourceId;

        // Config must land before the overlay's own pixels: the compositor prunes any
        // source_id not in the *current* Config every time it recomposites (including in
        // reaction to AddStaticOverlay's own frame arriving), so sending pixels first races
        // Config's own arrival — the freshly registered frame could get pruned right back out
        // before Config ever listed this new source_id as live, leaving the overlay silently
        // missing from the composited output (and the stream) until something else happens to
        // re-register it. Sent directly (not the debounced ScheduleLiveConfigPush) so there's no
        // window for that race.
        await _core.SendCommandAsync(BuildConfigCommand());
        await RegisterStaticOverlayAsync(sourceId, width, height, pixels);
        NotifyChanged();
    }

    /// <summary>Prompts for a short text string (see the Sources panel's inline text box) and
    /// adds it as a static overlay, rendered client-side so the aspect ratio is known
    /// immediately, exactly like an image overlay.</summary>
    [RelayCommand]
    private async Task AddTextOverlayAsync()
    {
        var text = string.IsNullOrWhiteSpace(NewTextOverlayInput) ? "Text Overlay" : NewTextOverlayInput.Trim();

        var rendered = OverlayContentRenderer.RenderTextToBgra(text);
        if (rendered is null) return;
        var (width, height, pixels) = rendered.Value;

        var sourceId = $"overlay:{Guid.NewGuid():N}";
        var slot = new SourceSlot(
            isPrimary: false, x: 30, y: 30, w: 30, h: 30,
            isOverlay: true, content: new TextOverlayContent { OverlayText = text });
        AttachSlot(slot);

        slot.DisplayName = "Text Overlay";
        OverlayContentRenderer.ApplyRenderedAspectRatio(slot, width, height, trackNaturalSize: true);
        slot.SourceId = sourceId;

        // See AddImageOverlayAsync's identical comment — Config must land before the overlay's
        // own pixels, or the compositor's pruning can drop the freshly registered frame right
        // back out before Config ever lists this source_id as live.
        await _core.SendCommandAsync(BuildConfigCommand());
        await RegisterStaticOverlayAsync(sourceId, width, height, pixels);
        NewTextOverlayInput = "";
        NotifyChanged();
    }

    /// <summary>Adds a solid-color fill overlay. Unlike image/text, a color has no intrinsic
    /// size — it's rendered once at a small fixed resolution and scaled to whatever box the
    /// user sets, since scaling a uniform fill loses nothing regardless of scale factor.</summary>
    [RelayCommand]
    private async Task AddColorOverlayAsync()
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var color = System.Windows.Media.Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
        var (width, height, pixels) = OverlayContentRenderer.RenderColorToBgra(color);

        var sourceId = $"overlay:{Guid.NewGuid():N}";
        var slot = new SourceSlot(
            isPrimary: false, x: 30, y: 30, w: 30, h: 30,
            isOverlay: true, content: new ColorOverlayContent { OverlayColor = color });
        AttachSlot(slot);

        slot.DisplayName = "Color Overlay";
        slot.SourceId = sourceId;

        // See AddImageOverlayAsync's identical comment — Config must land before the overlay's
        // own pixels, or the compositor's pruning can drop the freshly registered frame right
        // back out before Config ever lists this source_id as live.
        await _core.SendCommandAsync(BuildConfigCommand());
        await RegisterStaticOverlayAsync(sourceId, width, height, pixels);
        NotifyChanged();
    }

    /// <summary>Adds a looping video overlay. Unlike the static kinds, this needs an ongoing
    /// decode session — the core plays and loops the file itself, so this goes through the
    /// same Start/StopCapture lifecycle as a live source rather than AddStaticOverlay. The file
    /// path is encoded straight into the source id (matching how webcam ids embed a symlink),
    /// so no separate registration command is needed.</summary>
    [RelayCommand]
    private void AddVideoOverlay()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Overlay Video",
            Filter = "Video Files|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.m4v|All Files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        var sourceId = BuildVideoSourceId(dialog.FileName, loopVideo: true);
        var slot = new SourceSlot(
            isPrimary: false, x: 30, y: 30, w: 30, h: 30,
            isOverlay: true, content: new VideoOverlayContent { VideoPath = dialog.FileName });
        AttachSlot(slot);

        slot.DisplayName = "Video Overlay";
        slot.SourceId = sourceId; // Triggers UpdateSlotCaptureAsync, starting the decode session.

        NotifyChanged();
    }

    /// <summary>Adds a blur layer. Unlike every other overlay kind it carries no pixel content
    /// at all — the def itself (blur_radius in the next Config push) is the whole effect: the
    /// core's compositor blurs everything below it in z-order within the layer's rect, so its
    /// box is moved/resized like any overlay (drag to full frame to blur everything behind it)
    /// and its position in the layer list decides what gets blurred vs. stays sharp.</summary>
    [RelayCommand]
    private void AddBlurOverlay()
    {
        var slot = new SourceSlot(
            isPrimary: false, x: 30, y: 30, w: 40, h: 40,
            isOverlay: true, content: new BlurOverlayContent())
        {
            IsAspectLocked = false,
        };
        AttachSlot(slot);

        slot.DisplayName = "Blur Layer";
        slot.SourceId = $"blur:{Guid.NewGuid():N}";

        // Make it visible immediately rather than waiting for the next drag/scene switch to
        // happen to push a Config.
        ScheduleLiveConfigPush();
        NotifyChanged();
    }

    [RelayCommand]
    private async Task AddChatOverlayAsync()
    {
        var sourceId = $"overlay:{Guid.NewGuid():N}";
        var chatContent = new ChatOverlayContent();
        var slot = new SourceSlot(
            isPrimary: false, x: 30, y: 30, w: 40, h: 40,
            isOverlay: true, content: chatContent)
        {
            IsAspectLocked = false
        };
        AttachSlot(slot);

        slot.DisplayName = "Chat Overlay";
        slot.SourceId = sourceId;

        // Sample content so the properties panel's text-style controls and the placement canvas
        // have something to preview immediately, rather than an empty box — see
        // ChatOverlayContent.PlaceholderMessages. No-op if already live.
        PopulateChatPlaceholder(slot, chatContent);

        // Config must land before the overlay's own pixels — see AddImageOverlayAsync's
        // identical comment for why.
        await _core.SendCommandAsync(BuildConfigCommand());
        var rendered = OverlayContentRenderer.RenderChatToBgra(slot);
        if (rendered is var (width, height, pixels))
        {
            await RegisterStaticOverlayAsync(sourceId, width, height, pixels);
        }
        NotifyChanged();
        NotifyChatOverlayStateChanged();
    }

    [RelayCommand]
    private async Task AddTimerOverlayAsync()
    {
        var sourceId = $"overlay:{Guid.NewGuid():N}";
        var content = new TimerOverlayContent();
        var slot = new SourceSlot(
            isPrimary: false, x: 30, y: 30, w: 30, h: 15,
            isOverlay: true, content: content);
        AttachSlot(slot);

        slot.DisplayName = "Timer Overlay";
        // Aspect-locked to the rendered timer text's own natural size now, same as a Text
        // overlay (AddTextOverlayAsync) — previously explicitly IsAspectLocked=false with no
        // ApplyRenderedAspectRatio call at all, so resizing was free-form regardless of what the
        // digits actually needed.
        var (width, height, pixels) = OverlayContentRenderer.RenderTextToBgra(FormatTimerDisplay(slot), content.Style)!.Value;
        OverlayContentRenderer.ApplyRenderedAspectRatio(slot, width, height, trackNaturalSize: true);
        slot.SourceId = sourceId;

        // Config must land before the overlay's own pixels — see AddImageOverlayAsync's
        // identical comment for why.
        await _core.SendCommandAsync(BuildConfigCommand());
        await RegisterStaticOverlayAsync(sourceId, width, height, pixels);
        NotifyChanged();
    }

    [RelayCommand]
    private void AddGroupOverlay()
    {
        var sourceId = $"overlay:{Guid.NewGuid():N}";
        var slot = new SourceSlot(
            isPrimary: false, x: 20, y: 20, w: 20, h: 20,
            isOverlay: true, content: new GroupOverlayContent())
        {
            SourceId = sourceId,
            DisplayName = "Overlay Group",
            IsAspectLocked = false
        };
        AttachSlot(slot);
        NotifyChanged();
    }

    public void GroupSlots(List<SourceSlot> slotsToGroup)
    {
        if (slotsToGroup == null || slotsToGroup.Count <= 1) return;

        double minX = slotsToGroup.Min(s => s.XPercent);
        double minY = slotsToGroup.Min(s => s.YPercent);
        double maxX = slotsToGroup.Max(s => s.XPercent + s.WPercent);
        double maxY = slotsToGroup.Max(s => s.YPercent + s.HPercent);

        var newGroupContent = new GroupOverlayContent();
        var newGroupSourceId = $"overlay:{Guid.NewGuid():N}";
        var newGroupSlot = new SourceSlot(
            isPrimary: false, x: minX, y: minY, w: maxX - minX, h: maxY - minY,
            isOverlay: true, content: newGroupContent)
        {
            SourceId = newGroupSourceId,
            DisplayName = "Overlay Group",
            IsAspectLocked = false
        };

        newGroupSlot.PropertyChanged += OnSlotPropertyChanged;
        HookContentPropertyChanged(newGroupSlot);

        foreach (var slot in slotsToGroup)
        {
            if (slot.ParentGroup is not null)
            {
                var oldGroup = slot.ParentGroup.Content as GroupOverlayContent;
                oldGroup?.Children.Remove(slot);
            }
            newGroupContent.Children.Add(slot);
            slot.ParentGroup = newGroupSlot;
            slot.IsInSelectedGroup = false;
        }

        var firstIndex = Slots.IndexOf(slotsToGroup[0]);
        if (firstIndex >= 0)
        {
            Slots.Insert(firstIndex, newGroupSlot);
        }
        else
        {
            Slots.Add(newGroupSlot);
        }

        int offset = 1;
        foreach (var slot in slotsToGroup)
        {
            Slots.Remove(slot);
            var groupIndex = Slots.IndexOf(newGroupSlot);
            Slots.Insert(groupIndex + offset, slot);
            offset++;
        }

        NotifyChanged();
    }

    public void GroupTwoSlots(SourceSlot sourceSlot, SourceSlot targetSlot)
    {
        GroupSlots([targetSlot, sourceSlot]);
    }

    public void AddSlotToGroup(SourceSlot sourceSlot, SourceSlot targetSlotOrGroup)
    {
        var groupSlot = targetSlotOrGroup.OverlayKind == OverlayKind.Group ? targetSlotOrGroup : targetSlotOrGroup.ParentGroup;
        if (groupSlot is null) return;
        
        var group = groupSlot.Content as GroupOverlayContent;
        if (group is not null && !group.Children.Contains(sourceSlot))
        {
            if (sourceSlot.ParentGroup is not null)
            {
                var oldGroup = sourceSlot.ParentGroup.Content as GroupOverlayContent;
                oldGroup?.Children.Remove(sourceSlot);
            }
            group.Children.Add(sourceSlot);
            sourceSlot.ParentGroup = groupSlot;

            Slots.Remove(sourceSlot);
            var targetIdx = Slots.IndexOf(targetSlotOrGroup);
            Slots.Insert(targetIdx + 1, sourceSlot);
            NotifyChanged();
        }
    }

    public void RemoveSlotFromGroup(SourceSlot sourceSlot)
    {
        if (sourceSlot.ParentGroup is not null)
        {
            var oldGroup = sourceSlot.ParentGroup.Content as GroupOverlayContent;
            oldGroup?.Children.Remove(sourceSlot);
            sourceSlot.ParentGroup = null;
            NotifyChanged();
        }
    }

    // Nullable despite the command body itself treating slot as always-present: WPF's Button
    // probes CanExecute(null) while the CommandParameter binding is still activating, before the
    // real SourceSlot value has resolved — an un-guarded slot.Content access here is a guaranteed
    // NullReferenceException on every app startup.
    private bool CanStartTimer(SourceSlot? slot) => slot?.Content is TimerOverlayContent { IsTimerRunning: false };

    [RelayCommand(CanExecute = nameof(CanStartTimer))]
    private async Task StartTimer(SourceSlot slot)
    {
        if (slot.Content is not TimerOverlayContent timer || timer.IsTimerRunning || slot.SourceId is not string sourceId) return;
        timer.TimerStartedAtUtc = DateTime.UtcNow;
        timer.IsTimerRunning = true;
        StartTimerCommand.NotifyCanExecuteChanged();
        PauseTimerCommand.NotifyCanExecuteChanged();
        await RestoreStaticOverlayAsync(slot, sourceId);
    }

    private bool CanPauseTimer(SourceSlot? slot) => slot?.Content is TimerOverlayContent { IsTimerRunning: true };

    [RelayCommand(CanExecute = nameof(CanPauseTimer))]
    private async Task PauseTimer(SourceSlot slot)
    {
        if (slot.Content is not TimerOverlayContent timer || !timer.IsTimerRunning || slot.SourceId is not string sourceId) return;
        if (timer.TimerStartedAtUtc is DateTime started)
            timer.TimerElapsedBaseSeconds += (DateTime.UtcNow - started).TotalSeconds;
        timer.TimerStartedAtUtc = null;
        timer.IsTimerRunning = false;
        StartTimerCommand.NotifyCanExecuteChanged();
        PauseTimerCommand.NotifyCanExecuteChanged();
        await RestoreStaticOverlayAsync(slot, sourceId);
    }

    [RelayCommand]
    private async Task ResetTimer(SourceSlot slot)
    {
        if (slot.Content is not TimerOverlayContent timer || slot.SourceId is not string sourceId) return;
        timer.IsTimerRunning = false;
        timer.TimerStartedAtUtc = null;
        timer.TimerElapsedBaseSeconds = 0;
        StartTimerCommand.NotifyCanExecuteChanged();
        PauseTimerCommand.NotifyCanExecuteChanged();
        await RestoreStaticOverlayAsync(slot, sourceId);
    }

    /// <summary>Advances every running Timer overlay slot in the active scene by one tick —
    /// called once a second by GoLiveViewModel's own tick driver (see GoLiveViewModel.Timer.cs).
    /// A no-op scan when nothing's running, so that driver never needs its own start/stop
    /// lifecycle. Auto-stops a countdown that's reached zero rather than going negative.</summary>
    public async Task TickTimerOverlaysAsync()
    {
        if (ActiveScene is null) return;
        foreach (var slot in ActiveScene.Slots.Where(s => s.Content is TimerOverlayContent { IsTimerRunning: true }).ToList())
        {
            var timer = (TimerOverlayContent)slot.Content!;
            if (slot.SourceId is not string sourceId) continue;

            if (timer.TimerMode == TimerMode.CountDown)
            {
                var elapsed = timer.TimerElapsedBaseSeconds + (timer.TimerStartedAtUtc is DateTime started ? (DateTime.UtcNow - started).TotalSeconds : 0);
                if (elapsed >= timer.TimerDurationSeconds)
                {
                    timer.IsTimerRunning = false;
                    timer.TimerStartedAtUtc = null;
                    timer.TimerElapsedBaseSeconds = timer.TimerDurationSeconds;
                    StartTimerCommand.NotifyCanExecuteChanged();
                    PauseTimerCommand.NotifyCanExecuteChanged();
                }
            }

            await RestoreStaticOverlayAsync(slot, sourceId);
        }
    }

    /// <summary>URL-safe, unpadded base64 — matches the core's decoder
    /// (base64::engine::general_purpose::URL_SAFE_NO_PAD).</summary>
    private static string Base64UrlEncode(string text) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Builds a video overlay's source id, the sole channel the core learns both the file
    /// path and the loop mode through (see main.rs's StartCapture handling for the "video:"
    /// prefix) — no separate IPC command exists for either. A non-looping overlay carries an
    /// extra "once:" marker ahead of the base64 path; looping (the historical default) keeps the
    /// original "video:{base64}" shape unchanged for compatibility with already-saved scenes.</summary>
    private static string BuildVideoSourceId(string path, bool loopVideo) =>
        loopVideo ? $"video:{Base64UrlEncode(path)}" : $"video:once:{Base64UrlEncode(path)}";

    // ── Properties-panel edits to an existing overlay's content ────────────────────

    [RelayCommand]
    private void BrowseOverlayImage(SourceSlot slot)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Overlay Image",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;
        if (slot.Content is not ImageOverlayContent image) return;

        image.ImagePath = dialog.FileName; // Triggers ScheduleOverlayContentUpdate.
    }

    [RelayCommand]
    private void ChangeOverlayColor(SourceSlot slot)
    {
        if (slot.Content is not ColorOverlayContent color) return;

        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (color.OverlayColor is System.Windows.Media.Color current)
            dialog.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        color.OverlayColor = System.Windows.Media.Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
    }

    /// <summary>Shared by every IHasTextStyle kind (Text/Chat/Timer), not just Text overlays —
    /// widened from the pre-TextStyle version, which only ever checked for TextOverlayContent.</summary>
    [RelayCommand]
    private void ChangeTextColor(SourceSlot slot)
    {
        if (slot.Content is not IHasTextStyle hasStyle) return;

        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        var current = hasStyle.Style.FontColor;
        dialog.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        hasStyle.Style.FontColor = System.Windows.Media.Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
    }

    [RelayCommand]
    private void ChangeTextOutlineColor(SourceSlot slot)
    {
        if (slot.Content is not IHasTextStyle hasStyle) return;

        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        var current = hasStyle.Style.OutlineColor;
        dialog.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        hasStyle.Style.OutlineColor = System.Windows.Media.Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
    }

    [RelayCommand]
    private void ChangeChromaKeyColor(SourceSlot slot)
    {
        if (slot.Content is not IChromaKeyable keyable) return;

        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        dialog.Color = System.Drawing.Color.FromArgb(
            keyable.ChromaKeyColor.A, keyable.ChromaKeyColor.R, keyable.ChromaKeyColor.G, keyable.ChromaKeyColor.B);
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        keyable.ChromaKeyColor = System.Windows.Media.Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
    }

    /// <summary>Samples a chromakey color directly from whatever's on screen — the live preview
    /// included — instead of picking one blind from the standard color dialog's swatches/sliders.
    /// See EyedropperWindow's own doc comment for how the capture/loupe/sampling works.</summary>
    [RelayCommand]
    private void PickChromaKeyColorFromScreen(SourceSlot slot)
    {
        if (slot.Content is not IChromaKeyable keyable) return;
        if (EyedropperWindow.PickColor() is System.Windows.Media.Color color)
            keyable.ChromaKeyColor = color;
    }

    [RelayCommand]
    private void BrowseOverlayVideo(SourceSlot slot)
    {
        if (slot.Content is not VideoOverlayContent video) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Overlay Video",
            Filter = "Video Files|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.m4v|All Files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        video.VideoPath = dialog.FileName; // Triggers the SourceId rebuild above.
        slot.DisplayName = Path.GetFileName(dialog.FileName);
    }

    [ObservableProperty]
    private string _newTextOverlayInput = "";

    private Task RegisterStaticOverlayAsync(string sourceId, int width, int height, byte[] bgraPixels) =>
        _core.SendCommandAsync(new AddStaticOverlayCommand(sourceId, (uint)width, (uint)height, Convert.ToBase64String(bgraPixels)));

    public void AttachSlot(SourceSlot slot)
    {
        // SourceSlot's own constructor defaults CanvasWidth/CanvasHeight to a hardcoded 16:9
        // assumption — only UpdateCanvasReference (which runs on scene *switch*, not on adding a
        // slot to the scene that's already active) corrects it to the scene's real aspect ratio.
        // Without this, a freshly-added overlay on any non-16:9 scene keeps the wrong
        // CanvasHeight until the user happens to switch away and back, which throws off anything
        // that derives real proportions from it (e.g. OverlayContentRenderer.RenderChatToBgra's
        // bitmap dimensions vs. what the compositor — which always knows the true resolution —
        // actually stretches it into).
        if (ActiveScene is { } activeScene)
        {
            slot.CanvasWidth = activeScene.CanvasWidth;
            slot.CanvasHeight = activeScene.CanvasHeight;
        }

        slot.PropertyChanged += OnSlotPropertyChanged;
        HookContentPropertyChanged(slot);
        Slots.Add(slot);
        OnPropertyChanged(nameof(PrimarySlot));
    }

    /// <summary>Last source id each slot's capture session was (re)started for — lets
    /// UpdateSlotCaptureAsync know what to stop when a slot's SourceId changes.</summary>
    private readonly Dictionary<SourceSlot, string?> _activeCaptureBySlot = [];

    /// <summary>Reference count per underlying capture source_id, driving the actual Start/
    /// StopCaptureCommand IPC traffic — <see cref="_activeCaptureBySlot"/> only tracks what each
    /// *slot* last requested, which isn't enough on its own: two different scenes commonly share
    /// the same primary source (e.g. the same monitor), and switching between them creates two
    /// different SourceSlot instances for the "same" capture. Without a shared refcount, a scene
    /// switch would always stop-then-restart that source's native capture session (WGC teardown/
    /// recreate) even though nothing about it actually changed — see AcquireCaptureAsync/
    /// ReleaseCaptureAsync, and the reordered Activate-before-Deactivate in
    /// SwitchSceneCoreStateAsync that lets the refcount ever see a shared source stay above zero
    /// across the switch. Confirmed via diagnostic log as the root cause of a rapid stop/start
    /// thrash on every scene switch that could eventually race the underlying WGC session into a
    /// permanently-stopped state with no frames ever arriving again (preview looked "frozen").</summary>
    private readonly Dictionary<string, int> _activeCaptureRefCounts = [];

    /// <summary>Registers one more slot wanting <paramref name="sourceId"/> — only sends
    /// StartCaptureCommand the moment the refcount actually leaves zero, so a source already kept
    /// alive by another slot (typically the outgoing scene's slot for the same source, mid-switch)
    /// is left running untouched.</summary>
    private async Task AcquireCaptureAsync(SourceSlot slot, string sourceId)
    {
        _activeCaptureBySlot[slot] = sourceId;
        _activeCaptureRefCounts.TryGetValue(sourceId, out var count);
        _activeCaptureRefCounts[sourceId] = count + 1;
        if (count == 0)
            await _core.SendCommandAsync(new StartCaptureCommand(sourceId));
    }

    /// <summary>Releases the given slot's claim on whatever source it last acquired — only sends
    /// StopCaptureCommand once the refcount actually reaches zero, i.e. no other slot (typically
    /// the incoming scene's slot for the same source, mid-switch) still needs it. No-op for a
    /// slot that never acquired a capture (static overlays, or a slot with no source selected).</summary>
    private async Task ReleaseCaptureAsync(SourceSlot slot)
    {
        if (!_activeCaptureBySlot.Remove(slot, out var sourceId) || sourceId is null) return;
        if (!_activeCaptureRefCounts.TryGetValue(sourceId, out var count)) return;
        if (count <= 1)
        {
            _activeCaptureRefCounts.Remove(sourceId);
            await _core.SendCommandAsync(new StopCaptureCommand(sourceId));
        }
        else
        {
            _activeCaptureRefCounts[sourceId] = count - 1;
        }
    }

    private async void OnSlotPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not SourceSlot slot) return;

        if (e.PropertyName == nameof(SourceSlot.IsInSelectedGroup))
        {
            if (!_isUpdatingSelection)
            {
                var group = SelectedSlot?.Content as GroupOverlayContent;
                if (group is not null)
                {
                    if (slot.IsInSelectedGroup)
                    {
                        if (!group.Children.Contains(slot))
                            group.Children.Add(slot);
                    }
                    else
                    {
                        group.Children.Remove(slot);
                    }
                    NotifyChanged();
                }
            }
        }

        if (e.PropertyName == nameof(SourceSlot.SourceId))
        {
            // Static overlay slots' SourceId is a synthetic id assigned once at creation (not
            // a capture source picked from AvailableSources) — its DisplayName/AspectRatio are
            // set explicitly by AddImageOverlayAsync/ApplySettings, so skip the capture-source
            // resolution below entirely; running it here would immediately clobber both back to
            // "no source selected" / null the moment the overlay's SourceId is first assigned.
            // Video overlays are also fixed/synthetic content, but — unlike the static kinds —
            // still need an ongoing capture session (the core decodes/loops the file), so they
            // go through UpdateSlotCaptureAsync same as a live source, just skipping the
            // AvailableSources-based name/ratio resolution that only applies to those.
            if (!slot.IsStaticOverlay)
            {
                if (!slot.IsOverlay)
                {
                    slot.DisplayName = AvailableSources.FirstOrDefault(s => s.Id == slot.SourceId)?.Name
                        ?? "(no source selected)";
                    slot.AspectRatio = null; // Re-detected below (or once sources are (re)loaded) for the new source.
                    TryApplyAspectRatioFromSource(slot);
                }

                slot.LiveThumbnail = null; // Stale frame from whatever this slot used to show.
                await UpdateSlotCaptureAsync(slot);
            }
        }

        if (e.PropertyName is nameof(SourceSlot.SourceId) or nameof(SourceSlot.XPercent) or nameof(SourceSlot.YPercent)
            or nameof(SourceSlot.WPercent) or nameof(SourceSlot.HPercent) or nameof(SourceSlot.CornerRadiusPercent)
            or nameof(SourceSlot.DisplayName) or nameof(SourceSlot.OpacityPercent) or nameof(SourceSlot.RotationDegrees))
        {
            NotifyChanged();
        }

        if (e.PropertyName == nameof(SourceSlot.SourceId))
        {
            NotifySlotAvailabilityChanged();
        }

        if (e.PropertyName is nameof(SourceSlot.XPercent) or nameof(SourceSlot.YPercent)
            or nameof(SourceSlot.WPercent) or nameof(SourceSlot.HPercent) or nameof(SourceSlot.CornerRadiusPercent)
            or nameof(SourceSlot.OpacityPercent) or nameof(SourceSlot.RotationDegrees))
        {
            ScheduleLiveConfigPush();
        }

        // Chat is the one static-overlay kind whose *rendered bitmap* dimensions are derived
        // directly from the box's own WPercent/HPercent (see OverlayContentRenderer.
        // RenderChatToBgra) rather than being intrinsic/independent content that just gets
        // uniformly stretched into whatever box size (Text/Image/Timer — their AspectRatio is
        // locked to match, so a stale bitmap stretched into a resized box stays proportionally
        // correct; Chat isn't aspect-locked at all). Without this, resizing a chat overlay only
        // ever told the compositor the box's new size via Config — the actual registered bitmap
        // was never re-rendered at the new dimensions, so the compositor kept stretching the
        // stale one into the resized box, non-uniformly, worse the more it diverged from
        // whatever size it was first rendered at.
        if (e.PropertyName is nameof(SourceSlot.WPercent) or nameof(SourceSlot.HPercent)
            && slot.Content is ChatOverlayContent && slot.IsStaticOverlay)
        {
            ScheduleOverlayContentUpdate(slot);
        }
    }

    /// <summary>Mirrors <see cref="OnSlotPropertyChanged"/> for edits to a slot's kind-specific
    /// Content object (ImagePath, OverlayText, ChromaKeyEnabled, etc.) — these can't fire through
    /// the slot's own PropertyChanged since they live on a separate object now (see
    /// <see cref="HookContentPropertyChanged"/>).</summary>
    private void OnSlotContentPropertyChanged(SourceSlot slot, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Properties-panel edits to an existing overlay's content: re-render and re-register
        // its pixels (debounced, since text edits fire on every keystroke). A re-browsed video
        // is different — its "content" is the file the core itself decodes — so that's handled
        // by rebuilding the source id instead (below). TextStyle's property names are shared
        // across Text/Chat/Timer (see IHasTextStyle) — one check covers all three instead of
        // per-kind duplicates, since HookContentPropertyChanged forwards Style's own
        // PropertyChanged through this same handler now.
        if ((e.PropertyName is nameof(TextOverlayContent.OverlayText)
                or nameof(TextStyle.FontFamily) or nameof(TextStyle.FontSize) or nameof(TextStyle.FontColor)
                or nameof(TextStyle.IsBold) or nameof(TextStyle.IsItalic) or nameof(TextStyle.Alignment)
                or nameof(TextStyle.OutlineEnabled) or nameof(TextStyle.OutlineColor) or nameof(TextStyle.OutlineThickness)
                or nameof(ImageOverlayContent.ImagePath) or nameof(ColorOverlayContent.OverlayColor)
                or nameof(TimerOverlayContent.TimerMode) or nameof(TimerOverlayContent.TimerDurationSeconds))
            && slot.IsStaticOverlay)
        {
            ScheduleOverlayContentUpdate(slot);
        }

        if (e.PropertyName is nameof(VideoOverlayContent.VideoPath) or nameof(VideoOverlayContent.LoopVideo)
            && slot.Content is VideoOverlayContent { VideoPath: not null } video)
        {
            slot.SourceId = BuildVideoSourceId(video.VideoPath, video.LoopVideo);
        }

        if (e.PropertyName is nameof(TextOverlayContent.OverlayText)
            or nameof(TextStyle.FontFamily) or nameof(TextStyle.FontSize) or nameof(TextStyle.FontColor)
            or nameof(TextStyle.IsBold) or nameof(TextStyle.IsItalic) or nameof(TextStyle.Alignment)
            or nameof(TextStyle.OutlineEnabled) or nameof(TextStyle.OutlineColor) or nameof(TextStyle.OutlineThickness)
            or nameof(ImageOverlayContent.ImagePath)
            or nameof(ColorOverlayContent.OverlayColor) or nameof(VideoOverlayContent.VideoPath) or nameof(VideoOverlayContent.LoopVideo)
            or nameof(BlurOverlayContent.BlurRadius)
            or nameof(IChromaKeyable.ChromaKeyEnabled) or nameof(IChromaKeyable.ChromaKeyColor) or nameof(IChromaKeyable.ChromaKeySimilarity)
            or nameof(TimerOverlayContent.TimerMode) or nameof(TimerOverlayContent.TimerDurationSeconds)
            or nameof(TimerOverlayContent.AutoStartOnGoLive))
        {
            NotifyChanged();
        }

        if (e.PropertyName is nameof(BlurOverlayContent.BlurRadius)
            or nameof(IChromaKeyable.ChromaKeyEnabled) or nameof(IChromaKeyable.ChromaKeyColor) or nameof(IChromaKeyable.ChromaKeySimilarity))
        {
            ScheduleLiveConfigPush();
        }

        // Local editor-canvas WYSIWYG preview only — the actual composited/stream output is
        // already chroma-keyed by the core regardless of this. No-ops (and clears any stale
        // preview) for anything other than an image overlay with chromakey enabled; video's own
        // live thumbnail is chroma-keyed in place instead (see GoLiveView.xaml.cs).
        if (e.PropertyName is nameof(IChromaKeyable.ChromaKeyEnabled) or nameof(IChromaKeyable.ChromaKeyColor)
            or nameof(IChromaKeyable.ChromaKeySimilarity) or nameof(ImageOverlayContent.ImagePath))
        {
            ScheduleChromaKeyPreviewUpdate(slot);
        }
    }

    private CancellationTokenSource? _chromaKeyPreviewDebounceCts;

    private void ScheduleChromaKeyPreviewUpdate(SourceSlot slot)
    {
        if (slot.Content is not ImageOverlayContent { ChromaKeyEnabled: true, ImagePath: not null } image)
        {
            if (slot.Content is ImageOverlayContent noPreview) noPreview.KeyedPreviewSource = null;
            return;
        }

        _chromaKeyPreviewDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _chromaKeyPreviewDebounceCts = cts;

        var imagePath = image.ImagePath;
        var keyColor = image.ChromaKeyColor;
        var similarity = image.ChromaKeySimilarity;

        // Decoding + masking runs off the UI thread (debounced, since the Similarity slider
        // fires on every drag tick) — only the final WriteableBitmap construction/assignment
        // needs to happen back on it.
        _ = Task.Delay(100, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            var rendered = OverlayContentRenderer.DecodeImageToBgra(imagePath);
            if (rendered is not var (width, height, pixels)) return;
            OverlayContentRenderer.ApplyChromaKey(pixels, keyColor, similarity);

            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (cts.IsCancellationRequested) return;
                var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                    width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);
                bitmap.Freeze();
                image.KeyedPreviewSource = bitmap;
            });
        }, TaskScheduler.Default);
    }

    private CancellationTokenSource? _overlayContentDebounceCts;

    /// <summary>Re-renders and re-registers an existing static overlay's pixels after an edit
    /// in the properties panel (debounced — text content changes on every keystroke). Also
    /// used by GoLiveViewModel.Chat.cs after appending an incoming chat message to a chat
    /// overlay slot's ChatMessages.</summary>
    public void ScheduleOverlayContentUpdate(SourceSlot slot)
    {
        _overlayContentDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _overlayContentDebounceCts = cts;

        _ = Task.Delay(400, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled || slot.SourceId is not string sourceId) return;
            // Re-renders content and adjusts the box's size, both WPF-bound — must run on the
            // UI thread the way the rest of this ViewModel's dispatched work already does.
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => RestoreStaticOverlayAsync(slot, sourceId));
        }, TaskScheduler.Default);
    }

    private CancellationTokenSource? _liveConfigDebounceCts;

    /// <summary>Pushes a fresh Config to the core shortly after a drag/resize settles, so
    /// repositioning a slot actually changes what's composited instead of only updating the
    /// local editor. This matters even while idle, not just while streaming: the local preview's
    /// composited background layer (the "PreviewImage" the Rust core renders) is a separate
    /// render from each slot's own live-thumbnail overlay in the editor Canvas, and only reflects
    /// a moved/resized PiP or overlay once Config is resent — otherwise it keeps compositing at
    /// the old bounds, showing as a stale duplicate left behind the correctly-positioned one.</summary>
    private void ScheduleLiveConfigPush()
    {
        _liveConfigDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _liveConfigDebounceCts = cts;

        // A Config send is just a small JSON line over stdin (the core only updates a
        // mutex-guarded struct in response) — cheap enough that this is about drag
        // responsiveness, not send frequency.
        _ = Task.Delay(50, cts.Token).ContinueWith(async t =>
        {
            if (t.IsCanceled) return;
            await _core.SendCommandAsync(BuildConfigCommand());
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Starts/moves a slot's own live capture+preview to follow its selected source — applies
    /// to any capture-source slot (primary or PiP), not just the primary, so PiP boxes get a
    /// live thumbnail as soon as a source is picked instead of only once streaming starts.
    /// Tracked per-slot since multiple independent captures can now be active simultaneously.
    /// </summary>
    private async Task UpdateSlotCaptureAsync(SourceSlot slot)
    {
        // Only the active scene's slots have live capture sessions — a slot in a scene that
        // isn't currently shown just records its selection (DisplayName/AspectRatio above);
        // ActivateSceneAsync starts the real session once that scene becomes active. During
        // settings restore, ActiveScene is still null/unset when every scene's slots get their
        // SourceId assigned, so this naturally no-ops for all of them until ApplySettings picks
        // one at the end.
        if (ActiveScene is null || !ActiveScene.Slots.Contains(slot)) return;

        var newSourceId = slot.SourceId;
        _activeCaptureBySlot.TryGetValue(slot, out var oldSourceId);
        if (oldSourceId == newSourceId) return;

        await ReleaseCaptureAsync(slot);

        if (newSourceId is not null)
        {
            await AcquireCaptureAsync(slot, newSourceId);
            // The compositor only emits a composited frame once it knows the primary/PiP
            // layout, and the core only forwards a PiP's raw thumbnail for sources currently
            // listed here — either way, nothing shows until Config reflects this slot.
            await _core.SendCommandAsync(BuildConfigCommand());
            await _core.SendCommandAsync(new EnablePreviewCommand());
        }
    }

    /// <summary>Starts a capture session for every capture-based slot in the given scene that
    /// doesn't already have one running — used both when a scene becomes active and when
    /// streaming starts (in case that somehow hasn't happened yet, e.g. a settings-restore edge
    /// case), rather than tearing down and recreating an already-active session.</summary>
    public async Task StartAllSlotCapturesAsync(GoLiveSceneViewModel scene)
    {
        foreach (var slot in scene.Slots.Where(s => !string.IsNullOrEmpty(s.SourceId) && !s.IsStaticOverlay))
        {
            if (_activeCaptureBySlot.TryGetValue(slot, out var active) && active == slot.SourceId) continue;
            await AcquireCaptureAsync(slot, slot.SourceId!);
        }
    }

    /// <summary>Releases every capture session belonging to a scene that's no longer active —
    /// only the active scene keeps live sessions running (see UpdateSlotCaptureAsync). A source
    /// also needed by the scene becoming active (see AcquireCaptureAsync's refcount) is left
    /// running rather than stopped-then-restarted — see SwitchSceneCoreStateAsync, which always
    /// activates the new scene (acquiring shared sources first) before deactivating the old one,
    /// specifically so that ordering is what lets the refcount ever see a shared source this way.</summary>
    public async Task DeactivateSceneAsync(GoLiveSceneViewModel scene)
    {
        foreach (var slot in scene.Slots)
            await ReleaseCaptureAsync(slot);
    }

    public async Task ForceReacquireActiveCapturesAsync()
    {
        if (ActiveScene is null) return;
        foreach (var slot in ActiveScene.Slots.Where(s => !string.IsNullOrEmpty(s.SourceId) && !s.IsStaticOverlay))
        {
            await ReleaseCaptureAsync(slot);
        }
        await StartAllSlotCapturesAsync(ActiveScene);
    }

    /// <summary>Starts capture sessions for a scene's slots and pushes a fresh Config so it
    /// actually becomes what's composited. <paramref name="animate"/> is false for the very
    /// first scene activation (app/session startup — nothing composited yet to transition from
    /// anyway) and true for every subsequent switch, in which case the configured
    /// <see cref="TransitionKind"/>/<see cref="TransitionDurationMs"/> is sent along.</summary>
    private async Task ActivateSceneAsync(GoLiveSceneViewModel scene, bool animate)
    {
        await StartAllSlotCapturesAsync(scene);
        var transition = animate && TransitionKind != SceneTransitionKind.Cut
            ? new TransitionDef(TransitionKindToWire(TransitionKind), (uint)Math.Max(0, TransitionDurationMs))
            : null;
        await _core.SendCommandAsync(BuildConfigCommand(transition));

        // Static overlays (image/text/color/chat) have no ongoing capture session, so
        // StartAllSlotCapturesAsync above deliberately skips them — but the compositor also
        // prunes a source's cached frame the instant it's no longer in the *current* Config
        // (see compositor.rs's recomposite! macro pruning latest_pips/pip_scalers), which
        // happens to every one of this scene's overlays the moment some other scene becomes
        // active. Returning here needs them explicitly re-registered, or they'd just silently
        // stay off the composited output (and the stream). This MUST come after the Config
        // send above, not before: the compositor recomposites (and re-runs that same pruning
        // check against whatever Config it currently has) every time a new overlay frame
        // arrives too — sending these first would race Config's own arrival and could have a
        // freshly re-registered frame pruned right back out before Config ever listed this
        // scene's sources as live.
        foreach (var slot in scene.Slots.Where(s => s.IsStaticOverlay && s.SourceId is not null))
            await RestoreStaticOverlayAsync(slot, slot.SourceId!);

        await _core.SendCommandAsync(new EnablePreviewCommand());
    }

    partial void OnActiveSceneChanged(GoLiveSceneViewModel? oldValue, GoLiveSceneViewModel? newValue)
    {
        OnPropertyChanged(nameof(Slots));
        OnPropertyChanged(nameof(PrimarySlot));
        OnPropertyChanged(nameof(IsActiveSceneDefault));
        OnPropertyChanged(nameof(AvailableGroupCandidates));
        SelectedSlot = null;
        NotifySlotAvailabilityChanged();
        SetActiveSceneAsDefaultCommand.NotifyCanExecuteChanged();

        // Covers the case where the primary's AspectRatio was already known (restored from a
        // previous session) and so ApplyAspectRatio's change-detection never re-fires this
        // session — without this, a scene loaded straight into a non-16:9 primary would keep
        // stale (16:9-default) canvas dimensions until something happened to re-trigger it. Runs
        // for every scene regardless of whether it has a primary — an overlay-only scene still
        // needs its slots' CanvasWidth/Height synced to its own CanvasResolutionWidth/Height.
        if (newValue is not null)
            UpdateCanvasReference(newValue);

        _ = SwitchSceneCoreStateAsync(oldValue, newValue);
        NotifyChatOverlayStateChanged();

        // Published alongside, not instead of, the direct SwitchSceneCoreStateAsync call above —
        // that call performs the actual required deactivate/activate/transition work and must
        // always run regardless of subscribers. This is purely an observability signal for other
        // consumers (e.g. Stream Deck's WebSocket broadcast). Skipped for the transient switch to
        // null that happens while a scene set is loading — not a meaningful switch to announce.
        if (newValue is not null)
            _eventBus.Publish(new SceneSwitchedEvent(oldValue?.Id, newValue.Id));
    }

    partial void OnDefaultSceneChanged(GoLiveSceneViewModel? value)
    {
        OnPropertyChanged(nameof(IsActiveSceneDefault));
        SetActiveSceneAsDefaultCommand.NotifyCanExecuteChanged();
    }

    private async Task SwitchSceneCoreStateAsync(GoLiveSceneViewModel? oldScene, GoLiveSceneViewModel? newScene)
    {
        // Fire-and-forget from OnActiveSceneChanged (a property-changed hook can't be awaited by
        // its caller) — without this try/catch, any exception here (e.g. a bad IPC command) is an
        // unobserved task exception that silently vanishes, leaving the UI-bound scene/slot state
        // updated but the actual compositor Config push (which is what makes the preview reflect
        // the switch) never sent, with no trace of why.
        try
        {
            // Activate the new scene BEFORE deactivating the old one — the reverse order (as
            // this used to run) means a capture source shared between both scenes (e.g. the same
            // monitor/webcam as primary in both) always gets its refcount dropped to zero and
            // immediately started right back up, i.e. a full native capture-session teardown+
            // rebuild on every single switch even though nothing about that source changed. See
            // AcquireCaptureAsync/ReleaseCaptureAsync's own doc comments — activating first means
            // the shared source's refcount goes 1→2→1 instead of 1→0→1, so it's never actually
            // stopped at all.
            if (newScene is not null)
                await ActivateSceneAsync(newScene, animate: oldScene is not null);
            if (oldScene is not null)
                await DeactivateSceneAsync(oldScene);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scene switch core-state update failed ({OldScene} -> {NewScene})", oldScene?.Name, newScene?.Name);
        }
    }

    private bool CanRemoveActiveScene() => ActiveScene is not null && Scenes.Count > 1;

    [RelayCommand]
    private void AddScene()
    {
        var scene = CreateBlankScene($"Scene {Scenes.Count + 1}");
        Scenes.Add(scene);
        ActiveScene = scene;
        NotifyChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveActiveScene))]
    private void RemoveActiveScene()
    {
        if (ActiveScene is not GoLiveSceneViewModel scene || Scenes.Count <= 1) return;

        scene.PropertyChanged -= OnScenePropertyChanged;
        foreach (var slot in scene.Slots)
            slot.PropertyChanged -= OnSlotPropertyChanged;

        var index = Scenes.IndexOf(scene);
        Scenes.Remove(scene);

        if (ReferenceEquals(DefaultScene, scene))
            DefaultScene = Scenes[0];

        ActiveScene = Scenes[Math.Min(index, Scenes.Count - 1)];
        NotifyChanged();
    }

    private bool CanSetActiveSceneAsDefault() => ActiveScene is not null && !ReferenceEquals(ActiveScene, DefaultScene);

    [RelayCommand(CanExecute = nameof(CanSetActiveSceneAsDefault))]
    private void SetActiveSceneAsDefault()
    {
        DefaultScene = ActiveScene;
        NotifyChanged();
    }

    public StreamSourceDef[] BuildStreamSources() =>
        Slots.Where(s => !string.IsNullOrEmpty(s.SourceId))
            .Select(s => new StreamSourceDef(
                s.SourceId!, s.IsPrimary,
                (float)s.XPercent, (float)s.YPercent, (float)s.WPercent, (float)s.HPercent,
                (float)s.CornerRadiusPercent,
                s.Content is BlurOverlayContent blur ? (uint)Math.Max(0, blur.BlurRadius) : 0,
                s.Content is IChromaKeyable { ChromaKeyEnabled: true } keyable
                    ? new ChromaKeyDef(keyable.ChromaKeyColor.R, keyable.ChromaKeyColor.G, keyable.ChromaKeyColor.B, (float)(keyable.ChromaKeySimilarity / 100.0))
                    : null,
                (float)(s.OpacityPercent / 100.0),
                (ushort)s.RotationDegrees))
            .ToArray();

    /// <summary>Builds the Config command to push to the core — always includes the active
    /// scene's CanvasResolutionWidth/Height alongside the sources; the core only actually uses
    /// them when no primary has delivered a live frame yet, so it's safe to send unconditionally.
    /// <paramref name="transition"/> defaults to null (plain instant cut) — only
    /// <see cref="ActivateSceneAsync"/> (an actual scene switch) passes one; every other call
    /// site (drag/resize live-push, capture start/stop, start-stream) must NOT animate.</summary>
    public ConfigCommand BuildConfigCommand(TransitionDef? transition = null) =>
        new(BuildStreamSources(), ActiveScene?.CanvasResolutionWidth, ActiveScene?.CanvasResolutionHeight, transition);

    [RelayCommand]
    private async Task RemoveSlot(SourceSlot slot)
    {
        slot.PropertyChanged -= OnSlotPropertyChanged;
        Slots.Remove(slot);
        OnPropertyChanged(nameof(PrimarySlot));

        if (ReferenceEquals(SelectedSlot, slot))
            SelectedSlot = null;

        foreach (var otherSlot in Slots)
        {
            if (otherSlot.Content is GroupOverlayContent group)
            {
                group.Children.Remove(slot);
            }
        }

        if (slot.Content is GroupOverlayContent deletedGroup)
        {
            foreach (var child in deletedGroup.Children)
            {
                child.ParentGroup = null;
            }
        }

        // The compositor (and the data pipe's "is this a live PiP thumbnail" check) only ever
        // reads the *last* Config it was sent — without this, a removed PiP/overlay keeps
        // being composited (frozen on its last frame once capture stops, or forever for a
        // static overlay, which has no capture to stop at all) in both the local preview and
        // the actual stream, since nothing else tells the core it's gone.
        await _core.SendCommandAsync(BuildConfigCommand());

        await ReleaseCaptureAsync(slot);

        NotifyChanged();
        NotifyChatOverlayStateChanged();
    }

    [RelayCommand]
    private void DuplicateActiveScene()
    {
        if (ActiveScene is null) return;

        var sourceScene = ActiveScene;
        var name = $"{sourceScene.Name} Copy";
        // SwitchHotkey deliberately isn't copied — two scenes sharing one combo would be an
        // instant self-conflict the moment the duplicate exists (see HotkeyConflictService).
        var duplicatedScene = new GoLiveSceneViewModel(Guid.NewGuid().ToString("N"), name)
        {
            CanvasResolutionWidth = sourceScene.CanvasResolutionWidth,
            CanvasResolutionHeight = sourceScene.CanvasResolutionHeight,
        };
        duplicatedScene.PropertyChanged += OnScenePropertyChanged;

        foreach (var sourceSlot in sourceScene.Slots)
        {
            var newSlot = new SourceSlot(
                sourceSlot.IsPrimary, sourceSlot.XPercent, sourceSlot.YPercent, sourceSlot.WPercent, sourceSlot.HPercent,
                sourceSlot.IsOverlay, CloneContent(sourceSlot.Content))
            {
                SourceId = sourceSlot.IsOverlay ? $"overlay_{Guid.NewGuid():N}" : sourceSlot.SourceId,
                DisplayName = sourceSlot.DisplayName,
                CornerRadiusPercent = sourceSlot.CornerRadiusPercent,
                IsAspectLocked = sourceSlot.IsAspectLocked,
                OpacityPercent = sourceSlot.OpacityPercent,
                RotationDegrees = sourceSlot.RotationDegrees,
            };

            if (newSlot.IsStaticOverlay && !string.IsNullOrEmpty(newSlot.SourceId))
            {
                // CloneContent deliberately doesn't copy chat messages (they're live/transient
                // either way) — refill with placeholder content the same as any other idle empty
                // chat overlay.
                if (newSlot.Content is ChatOverlayContent duplicatedChat)
                    PopulateChatPlaceholder(newSlot, duplicatedChat);

                _ = RestoreStaticOverlayAsync(newSlot, newSlot.SourceId);
            }

            // Same reasoning as BuildSceneFromSettings: Content was set via the constructor
            // above, before PropertyChanged is wired up.
            ScheduleChromaKeyPreviewUpdate(newSlot);

            newSlot.PropertyChanged += OnSlotPropertyChanged;
            HookContentPropertyChanged(newSlot);
            duplicatedScene.Slots.Add(newSlot);
        }

        Scenes.Add(duplicatedScene);
        ActiveScene = duplicatedScene;
        NotifyChanged();
    }

    /// <summary>
    /// Resolution/aspect-ratio detection: the core reports each source's native capture size
    /// as part of GetSources (read from the platform capture item at enumeration time — no
    /// capture session needs to be running), so the ratio is known as soon as a source is
    /// picked rather than needing to wait for preview frames. (The data pipe's frames are the
    /// *composited* output — always tagged "preview" — so they can't be used to identify which
    /// source they came from once more than one is involved.)
    /// </summary>
    public void TryApplyAspectRatioFromSource(SourceSlot slot)
    {
        var source = AvailableSources.FirstOrDefault(s => s.Id == slot.SourceId);
        if (source is null || source.Width == 0 || source.Height == 0) return;

        ApplyAspectRatio(slot, source.Width, source.Height);
    }

    public void ApplyAspectRatio(SourceSlot slot, uint width, uint height)
    {
        var aspectRatio = width / (double)height;
        if (slot.AspectRatio is double existing && Math.Abs(existing - aspectRatio) < 0.01) return;

        // A brand-new primary (never had a resolution before) gets a sensible full-frame
        // starting position; a later resolution change (e.g. switching which monitor it
        // captures) only re-syncs the canvas shape/resolution, without silently resetting
        // wherever the user has since moved/resized it to.
        var isFirstResolution = slot.AspectRatio is null;

        _logger.LogDebug("Aspect ratio detected for {SourceId}: {W}x{H} ({Ratio:F3}), locked={Locked}",
            slot.SourceId, width, height, aspectRatio, slot.IsAspectLocked);

        slot.AspectRatio = aspectRatio;

        if (slot.IsPrimary)
        {
            // A live primary's real resolution is always authoritative for the scene's actual
            // output size (see compositor.rs) — a manually-set/pre-selected resolution only
            // applies until a real primary reports its own.
            var scene = Scenes.FirstOrDefault(sc => sc.Slots.Contains(slot));
            if (scene is not null)
            {
                scene.CanvasResolutionWidth = width;
                scene.CanvasResolutionHeight = height;
                UpdateCanvasReference(scene);
            }

            if (isFirstResolution)
            {
                var wasLocked = slot.IsAspectLocked;
                slot.IsAspectLocked = false;
                slot.XPercent = 0;
                slot.YPercent = 0;
                slot.WPercent = 100;
                slot.HPercent = 100;
                slot.IsAspectLocked = wasLocked;
            }
        }
        else if (slot.IsAspectLocked)
        {
            slot.ResizeToWidthPercent(slot.WPercent);
        }
    }

    [ObservableProperty]
    private uint _manualCanvasWidth = 1920;

    [ObservableProperty]
    private uint _manualCanvasHeight = 1080;

    /// <summary>Manually sets the active scene's real canvas resolution — for a primary-less
    /// scene, since there's otherwise no way to know what size to render overlays at. A no-op
    /// effect-wise once a primary exists and reports its own resolution, since that's always
    /// authoritative (see ApplyAspectRatio).</summary>
    [RelayCommand]
    private void ApplyManualCanvasResolution()
    {
        if (ActiveScene is null) return;
        SetCanvasResolution(ActiveScene, ManualCanvasWidth, ManualCanvasHeight);
    }

    /// <summary>Pre-seeds the active scene's canvas resolution from a known device's reported
    /// native resolution, without starting a capture session — lets a primary-less scene match
    /// a capture source's real shape ahead of time, so adding that source later as an actual
    /// layer doesn't jump/resize the canvas.</summary>
    [RelayCommand]
    private void SetCanvasResolutionFromDevice(NativeCaptureSource device)
    {
        if (ActiveScene is null || device is not { Width: > 0, Height: > 0 }) return;
        SetCanvasResolution(ActiveScene, device.Width, device.Height);
    }

    private void SetCanvasResolution(GoLiveSceneViewModel scene, uint width, uint height)
    {
        scene.CanvasResolutionWidth = width;
        scene.CanvasResolutionHeight = height;
        UpdateCanvasReference(scene);
        ScheduleLiveConfigPush();
        NotifyChanged();
    }

    /// <summary>Keeps every slot's CanvasWidth/CanvasHeight — the placement canvas's reference
    /// pixel size, used for all percent/pixel conversions and drag/resize/snap math — in sync
    /// with the scene's real CanvasResolutionWidth/Height (or a 16:9 default until one is known).
    /// Width is an arbitrary fixed constant; height is derived so the canvas's shape always
    /// matches the real composited output, eliminating the letterbox-margin mismatch described
    /// on SourceSlot.CanvasHeight.</summary>
    private void UpdateCanvasReference(GoLiveSceneViewModel scene)
    {
        var ar = scene.CanvasResolutionWidth is > 0 && scene.CanvasResolutionHeight is > 0
            ? scene.CanvasResolutionWidth.Value / (double)scene.CanvasResolutionHeight.Value
            : 16.0 / 9.0;
        var canvasHeight = SourceSlot.DefaultCanvasWidth / ar;

        scene.CanvasWidth = SourceSlot.DefaultCanvasWidth;
        scene.CanvasHeight = canvasHeight;

        foreach (var slot in scene.Slots)
        {
            slot.CanvasWidth = SourceSlot.DefaultCanvasWidth;
            slot.CanvasHeight = canvasHeight;
        }
    }

    private List<SceneSettings> BuildScenesSnapshot() =>
        Scenes.Select(scene => new SceneSettings
        {
            Id = scene.Id,
            Name = scene.Name,
            CanvasResolutionWidth = scene.CanvasResolutionWidth,
            CanvasResolutionHeight = scene.CanvasResolutionHeight,
            SwitchHotkeyKey = scene.SwitchHotkey?.Key.ToString(),
            SwitchHotkeyModifiers = scene.SwitchHotkey?.Modifiers.ToString(),
            Slots = scene.Slots.Select(ToSlotSettings).ToList(),
        }).ToList();

    /// <summary>Flattens a SourceSlot's Content back into the persisted SlotSettings shape —
    /// SlotSettings itself stays a flat, all-nullable DTO for full backward compatibility with
    /// existing settings files, even though the runtime SourceSlot no longer is.</summary>
    private static SlotSettings ToSlotSettings(SourceSlot s) => new()
    {
        IsPrimary = s.IsPrimary,
        IsOverlay = s.IsOverlay,
        OverlayKind = s.OverlayKind,
        SourceId = s.SourceId,
        DisplayName = s.DisplayName,
        GroupChildIds = (s.Content as GroupOverlayContent)?.Children.Select(c => c.SourceId).Where(id => id is not null).ToList()!,
        ImagePath = (s.Content as ImageOverlayContent)?.ImagePath,
        OverlayText = (s.Content as TextOverlayContent)?.OverlayText,
        OverlayColorHex = (s.Content as ColorOverlayContent)?.OverlayColor?.ToString(CultureInfo.InvariantCulture),
        VideoPath = (s.Content as VideoOverlayContent)?.VideoPath,
        LoopVideo = (s.Content as VideoOverlayContent)?.LoopVideo ?? true,
        XPercent = s.XPercent,
        YPercent = s.YPercent,
        WPercent = s.WPercent,
        HPercent = s.HPercent,
        CornerRadiusPercent = s.CornerRadiusPercent,
        BlurRadius = (s.Content as BlurOverlayContent)?.BlurRadius ?? 0,
        ChromaKeyEnabled = (s.Content as IChromaKeyable)?.ChromaKeyEnabled ?? false,
        ChromaKeyColorHex = (s.Content as IChromaKeyable)?.ChromaKeyColor.ToString(CultureInfo.InvariantCulture),
        ChromaKeySimilarity = (s.Content as IChromaKeyable)?.ChromaKeySimilarity ?? 40,
        OpacityPercent = s.OpacityPercent,
        RotationDegrees = s.RotationDegrees,
        TimerMode = (s.Content as TimerOverlayContent)?.TimerMode ?? TimerMode.CountDown,
        TimerDurationSeconds = (s.Content as TimerOverlayContent)?.TimerDurationSeconds ?? 300,
        TimerAutoStartOnGoLive = (s.Content as TimerOverlayContent)?.AutoStartOnGoLive ?? false,
        // Text* fields predate TextStyle and were Text-overlay-only; now sourced from
        // IHasTextStyle.Style so Chat/Timer formatting persists too — see
        // ApplyTextStyleFromSettings for the load-side counterpart.
        TextFontFamily = (s.Content as IHasTextStyle)?.Style.FontFamily,
        TextFontSize = (s.Content as IHasTextStyle)?.Style.FontSize,
        TextFontColorHex = (s.Content as IHasTextStyle)?.Style.FontColor.ToString(CultureInfo.InvariantCulture),
        TextIsBold = (s.Content as IHasTextStyle)?.Style.IsBold,
        TextIsItalic = (s.Content as IHasTextStyle)?.Style.IsItalic,
        TextAlignment = (s.Content as IHasTextStyle)?.Style.Alignment.ToString(),
        TextOutlineEnabled = (s.Content as IHasTextStyle)?.Style.OutlineEnabled,
        TextOutlineColorHex = (s.Content as IHasTextStyle)?.Style.OutlineColor.ToString(CultureInfo.InvariantCulture),
        TextOutlineThickness = (s.Content as IHasTextStyle)?.Style.OutlineThickness,
    };

    /// <summary>Imports a .sfset file from disk (registering it, same as Go Live's own "Import
    /// Set" button) without loading it — callers combine this with
    /// <see cref="LoadSceneSetForEditingAsync"/> to import-and-load in one action.</summary>
    public SceneSetRegistration ImportSceneSetFile(string zipPath)
    {
        var reg = _sceneSetService.ImportSceneSet(zipPath);
        RegisteredSceneSets.Add(reg);
        return reg;
    }

    /// <summary>Writes the current Scenes content to a portable .sfset file at the given path —
    /// unlike <see cref="SaveActiveSceneSet"/> (which overwrites the loaded registration's own
    /// cached files), this always creates a fresh, relocatable archive, matching Go Live's own
    /// "Export Current" button.</summary>
    public void ExportActiveSceneSet(string zipPath) =>
        _sceneSetService.ExportSceneSet(zipPath, SceneSetName, SceneSetAuthor, BuildScenesSnapshot());

    /// <summary>Exports an arbitrary registered Scene Set (not necessarily the one currently
    /// loaded into this editor) straight from its own cached files — used by the Scenes page's
    /// library browser to export a set without disturbing whatever's actively being edited.</summary>
    public void ExportRegisteredSceneSet(SceneSetRegistration reg, string zipPath) =>
        _sceneSetService.ExportRegisteredSceneSet(reg, zipPath);

    /// <summary>Deletes a registered Scene Set's cached files and removes it from
    /// <see cref="RegisteredSceneSets"/> — shared by Go Live's own Manage-Scene-Sets removal
    /// (which layers a "linked to the active streaming profile" safety check on top, a
    /// Go-Live-only concept this shared editor has no notion of) and the Scenes page's own
    /// library browser delete action.</summary>
    public void UninstallSceneSet(SceneSetRegistration reg)
    {
        _sceneSetService.UninstallSceneSet(reg);
        RegisteredSceneSets.Remove(reg);
    }

    /// <summary>Replaces Scenes/ActiveScene with a single fresh blank scene, unlinked from any
    /// registered Scene Set — the starting point for building a layout from nothing rather than
    /// loading an existing one. Since Scenes/ActiveScene are shared, this also replaces whatever
    /// Go Live currently shows/streams, same as the other load actions.</summary>
    public void CreateNewSceneSet()
    {
        ActiveScene = null;
        Scenes.Clear();
        Scenes.Add(CreateBlankScene("Scene 1"));
        ActiveSceneSet = null;
        SetSceneSetMetadataFromRegistration(null);
        ActiveScene = Scenes[0];
        HasUnsavedChanges = true;
    }

    /// <summary>Loads a specific registered Scene Set's content directly, with no awareness of
    /// streaming profiles/overrides (that's Go Live's own, more elaborate LoadSceneSetAsync in
    /// GoLiveViewModel.Streaming.cs) — used by the Scenes page's own load actions to bring in a
    /// different Scene Set than whatever Go Live currently has active. Since Scenes/ActiveScene
    /// are shared, this also changes what Go Live shows/streams — by design.</summary>
    public async Task LoadSceneSetForEditingAsync(SceneSetRegistration reg)
    {
        var loadedSettings = _sceneSetService.LoadSceneSetLayout(reg);

        Scenes.Clear();
        foreach (var savedScene in loadedSettings)
        {
            Scenes.Add(BuildSceneFromSettings(savedScene));
        }

        if (Scenes.Count == 0)
        {
            Scenes.Add(CreateBlankScene("Scene 1"));
        }

        ActiveSceneSet = reg;
        SetSceneSetMetadataFromRegistration(reg);
        // Triggers OnActiveSceneChanged, which deactivates whatever scene was previously active
        // and activates the newly loaded one — the same mechanism Go Live's own scene switching
        // already relies on.
        ActiveScene = Scenes[0];
        HasUnsavedChanges = false;
    }

    private bool CanSaveActiveSceneSet() => ActiveSceneSet is not null && HasUnsavedChanges;

    /// <summary>Writes the current Scenes content directly to the loaded Scene Set's own files —
    /// unlike Go Live's "Save Layout", this never touches streaming-profile overrides or the
    /// app's overall settings file, since the Scenes page has no concept of either.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveActiveSceneSet))]
    private void SaveActiveSceneSet()
    {
        if (ActiveSceneSet is null) return;

        _sceneSetService.SaveSceneSetLayout(ActiveSceneSet, BuildScenesSnapshot());
        HasUnsavedChanges = false;
    }

    partial void OnHasUnsavedChangesChanged(bool value) => SaveActiveSceneSetCommand.NotifyCanExecuteChanged();

    partial void OnActiveSceneSetChanged(SceneSetRegistration? value) => SaveActiveSceneSetCommand.NotifyCanExecuteChanged();
}
