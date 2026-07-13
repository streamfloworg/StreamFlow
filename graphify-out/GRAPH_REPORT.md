# Graph Report - .  (2026-07-11)

## Corpus Check
- Large corpus: 1247 files · ~1,201,562 words. Semantic extraction will be expensive (many Claude tokens). Consider running on a subfolder.

## Summary
- 4050 nodes · 6806 edges · 274 communities (239 shown, 35 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 134 edges (avg confidence: 0.8)
- Token cost: 0 input · 173,412 output

## Community Hubs (Navigation)
- GoLive & Settings View Bindings
- Scene Set Browser View
- Stream Deck Server & EventBus
- Core IPC Bridge Service
- Scene Editor ViewModel
- Rust Streaming Encoder
- OAuth & Stream Ingest Services
- Source Slot Capture ViewModel
- Scene Editor View Converters
- Audio Metadata Display Controls
- Audio Command Queue
- Solution & Package References
- Twitch Chat ViewModel
- Update Service & App Lifecycle
- Core Namespace Structure
- Scene Set Persistence & Dialogs
- Settings View Bindings
- User Options Data Model
- Video Overlay ViewModel
- Rust IPC & Capture Core
- Rust Compositor Render Backend
- Main Window ViewModel
- Coding Rules & Stream Deck UI
- Audio Playback Engine
- App Namespace Structure
- Slot Placement Behavior
- Audio Device Engine
- Audio Channel Strip Control
- Global Keyboard Hotkey Listener
- FFmpeg Bindgen Build Script
- Drag & Drop Behavior
- Stream Deck Plugin Build Tooling
- Src Module
- Windows Module
- Src Module
- Src Module
- Src Module
- Pages Module
- Src Module
- Src Module
- Misc Module
- Controls Module
- Theme Module
- Src Module
- Pages Module
- Audio Handling Module
- Behaviors Module
- Windows Module
- Persistence Module
- Misc Module
- Pages Module
- Pages Module
- Controls Module
- Src Module
- Misc Module
- Controls Module
- Converter Module
- Misc Module
- Behaviors Module
- Controls Module
- Misc Module
- Misc Module
- Helpers Module
- Controls Module
- Workflows Module
- Misc Module
- Behaviors Module
- Pages Module
- Resources Module
- Misc Module
- Stream Flow Tests Module
- Misc Module
- Controls Module
- Helpers Module
- Pages Module
- Audio Handling Module
- Rendering Module
- Stream Flow App Module
- Misc Module
- Rendering Module
- Misc Module
- Audio Properties Module
- Windows Module
- Misc Module
- Services Module
- Services Module
- Misc Module
- Pages Module
- Misc Module
- Data Module
- Com Streamfloworg Streamflow Sd Sd Plugin Module
- Windows Module
- Controls Module
- Controls Module
- Compose Module
- Src Module
- Behaviors Module
- Pages Module
- Src Module
- Controls Module
- Misc Module
- Controls Module
- Stream Flow Stream Deck Module
- Contracts Module
- Contracts Module
- Misc Module
- Misc Module
- Misc Module
- Audio Properties Module
- Actions Module
- Controls Module
- Pages Module
- Converter Module
- Avutil Module
- Audio Handling Module
- Windows Module
- Misc Module
- Services Module
- Actions Module
- Actions Module
- Behaviors Module
- Pages Module
- Helpers Module
- Controls Module
- Pages Module
- Windows Module
- Audio Properties Module
- Controls Module
- Helpers Module
- Helpers Module
- Services Module
- Helpers Module
- Services Module
- Services Module
- Pages Module
- Pages Module
- Pages Module
- Converter Module
- Misc Module
- Misc Module
- Com Streamfloworg Streamflow Sd Sd Plugin Module
- Audio Handling Module
- Actions Module
- Misc Module
- Audio Properties Module
- Misc Module
- Helpers Module
- Helpers Module
- Helpers Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Resources Module
- Audio Handling Module
- Pages Module
- Pages Module
- Data Module
- Helpers Module
- Protocol Module
- Src Module
- Helpers Module
- Pages Module
- Misc Module
- Src Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Converter Module
- Services Module
- Stream Flow Core Module
- Audio Handling Module
- Actions Module
- Windows Module
- Misc Module
- Data Module
- Src Module
- Pages Module
- Helpers Module
- Controls Module
- Controls Module
- Helpers Module
- Stream Flow App Module
- Controls Module
- Resources Module
- Resources Module
- Pages Module
- Pages Module
- Data Module
- Resources Module
- Pages Module
- Windows Module
- Resources Module
- Models Module
- Resources Module
- Audio Handling Module
- Controls Module
- Controls Module
- Misc Module
- Pages Module
- Pages Module
- Pages Module
- Pages Module
- Pages Module
- Pages Module
- Pages Module
- Pages Module
- Audio Properties Module
- Data Module
- Resources Module
- Resources Module
- Theme Module
- Theme Module
- Theme Module
- Theme Module
- Pages Module
- Pages Module
- Pages Module
- Pages Module
- Pages Module
- Audio Properties Module
- Ui Module
- Stream Flow Stream Deck Module

## God Nodes (most connected - your core abstractions)
1. `Page` - 212 edges
2. `Page` - 147 edges
3. `SceneEditorViewModel` - 113 edges
4. `AudioViewModel` - 98 edges
5. `StreamFlow.App.Converter` - 56 edges
6. `SourceSlot` - 54 edges
7. `MainWindow` - 51 edges
8. `Audio` - 51 edges
9. `StreamFlow.App.ViewModels.Pages` - 50 edges
10. `GoLiveViewModel` - 47 edges

## Surprising Connections (you probably didn't know these)
- `GitHub Release Publisher workflow (generic template, manual dispatch)` --semantically_similar_to--> `Velopack publish flow, gated on Release config AND is_release (tag push or manual dispatch)`  [INFERRED] [semantically similar]
  StreamFlow.App/.github/workflows/dotnet-release.yml → .github/workflows/dotnet-desktop.yml
- `Versioning scheme: crate major.minor tracks FFmpeg major.minor; patch level is crate-only bug fixes` --semantically_similar_to--> `Versioning and releases (Directory.Build.props single version, vX.Y.Z tag or workflow_dispatch triggers release)`  [INFERRED] [semantically similar]
  native/patches/ffmpeg-sys-next/README.md → README.md
- `UI (Electron) must not do heavy lifting; offload expensive processing to Core` --conceptually_related_to--> `Native Rust core (capture/compositing/streaming, talks to WPF app over IPC)`  [AMBIGUOUS]
  .agents/rules/code-style-guide.md → README.md
- `AudioControls` --references--> `AudioEngine`  [EXTRACTED]
  StreamFlow.App/Controls/AudioControls.xaml.cs → StreamFlow.Core/AudioHandling/AudioEngine.cs
- `AudioDeletion` --references--> `Audio`  [EXTRACTED]
  StreamFlow.App/Controls/AudioDeletion.xaml.cs → StreamFlow.Core/AudioHandling/Audio.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Stream Deck property inspectors sharing the Global Settings (Host URL + API Key) pattern** — streamflow_streamdeck_com_streamfloworg_streamflow_sd_sdplugin_ui_audio_control, streamflow_streamdeck_com_streamfloworg_streamflow_sd_sdplugin_ui_media_playback, streamflow_streamdeck_com_streamfloworg_streamflow_sd_sdplugin_ui_scene_control, streamflow_streamdeck_com_streamfloworg_streamflow_sd_sdplugin_ui_stream_control [EXTRACTED 1.00]
- **StreamFlow CI CD workflow triad (main desktop build release, StreamFlow.App CI, and StreamFlow.App generic release template)** — github_workflows_dotnet_desktop, streamflow_app_github_workflows_ci, streamflow_app_github_workflows_dotnet_release [INFERRED 0.85]
- **FFmpeg dependency chain supplying validating FFmpeg for StreamFlow native Rust core** — github_workflows_dotnet_desktop_ffmpeg_dev_cache, native_patches_ffmpeg_sys_next_github_workflows_build, native_patches_ffmpeg_sys_next_readme [INFERRED 0.85]

## Communities (274 total, 35 thin omitted)

### Community 0 - "GoLive & Settings View Bindings"
Cohesion: 0.01
Nodes (172): ConnectedAccountLabel, AspectRatioText, HasLiveThumbnail, IsConnected, IsNone, IsNotNone, IsRenaming, KeyHelpText (+164 more)

### Community 1 - "Scene Set Browser View"
Cohesion: 0.02
Nodes (126): Author, CollectionCountToVisibility, ViewModel.CloseSceneSetCommand, ViewModel.CreateNewSceneSetCommand, ViewModel.DeleteRegisteredSceneSetCommand, ViewModel.ExportRegisteredSceneSetCommand, ViewModel.IsSceneSetLoaded, ViewModel.LoadActiveSceneSetCommand (+118 more)

### Community 2 - "Stream Deck Server & EventBus"
Cohesion: 0.05
Nodes (39): AudioStateResponse, Func, HttpContext, RequestDelegate, SceneStateResponse, Action, Dictionary, IDisposable (+31 more)

### Community 3 - "Core IPC Bridge Service"
Cohesion: 0.05
Nodes (59): SemaphoreSlim, bool, CancellationToken, CancellationTokenSource, ILogger, JsonSerializerOptions, List, object (+51 more)

### Community 4 - "Scene Editor ViewModel"
Cohesion: 0.05
Nodes (24): Button, HashSet, bool, DependencyObject, Dictionary, Dispatcher, DispatcherTimer, double (+16 more)

### Community 5 - "Rust Streaming Encoder"
Cohesion: 0.07
Nodes (49): AtomicI32, c_char, check(), config_cache(), encode_frame(), EncoderConfig, EncoderPreset, FormatCtxSend (+41 more)

### Community 6 - "OAuth & Stream Ingest Services"
Cohesion: 0.07
Nodes (28): AccessToken, HttpListener, Id, IngestionAddress, JsonElement, Messages, NextPageToken, PollingIntervalMs (+20 more)

### Community 7 - "Source Slot Capture ViewModel"
Cohesion: 0.07
Nodes (13): NativeCaptureSource, bool, CancellationTokenSource, Dictionary, EventArgs, ILogger, int, ObservableCollection (+5 more)

### Community 8 - "Scene Editor View Converters"
Cohesion: 0.06
Nodes (39): AppliedTransform, KindToOpacityConverter, KindToVisibilityConverter, UIntToRectConverter, GridSpacing, Height, Kind, Left (+31 more)

### Community 9 - "Audio Metadata Display Controls"
Cohesion: 0.06
Nodes (40): AlbumName, ArtistName, AudioName, StringHelper, DirectoryPath, FileName, HasMetadataTags, ImportedImageSource (+32 more)

### Community 10 - "Audio Command Queue"
Cohesion: 0.06
Nodes (21): ConcurrentQueue, StreamFlow.App.Commands, DependencyObject, ICommand, Queue, FrameworkElement, AudioCommandBase, bool (+13 more)

### Community 11 - "Solution & Package References"
Cohesion: 0.05
Nodes (45): ATL, SoundFlow, StreamFlow.App.Package, AdonisUI (1.17.1), AdonisUI.ClassicTheme (1.17.1), CommunityToolkit.Common (8.4.0), CommunityToolkit.HighPerformance (8.4.0), FluentValidation (12.1.1) (+37 more)

### Community 12 - "Twitch Chat ViewModel"
Cohesion: 0.10
Nodes (33): ClientWebSocket, IReadOnlyList, ObservableObject, ChatMessage, CancellationToken, CancellationTokenSource, ILogger, string (+25 more)

### Community 13 - "Update Service & App Lifecycle"
Cohesion: 0.06
Nodes (23): CookieContainer, CancelEventArgs, Action, ILogger, Task, UpdateInfo, UpdateCheckResult, UpdateCheckStatus (+15 more)

### Community 14 - "Core Namespace Structure"
Cohesion: 0.12
Nodes (15): StreamFlow.Core.AudioHandling, StreamFlow.App.Views.Windows, StreamFlow.Core.Helpers, StreamFlow.Core, StreamFlow.App.Helpers, StreamFlow.Core.Cache, StreamFlow.Core.Persistence, StreamFlow.Core.Data (+7 more)

### Community 15 - "Scene Set Persistence & Dialogs"
Cohesion: 0.09
Nodes (13): SceneSetManifest, SceneSetRegistration, Task, IDialogService, JsonSerializerOptions, List, SceneSetManifest, SceneSetService (+5 more)

### Community 16 - "Settings View Bindings"
Cohesion: 0.06
Nodes (35): ViewModel.AppVersion, ViewModel.AudioOutputs.SourceCollection, ViewModel.CheckForUpdatesCommand, ViewModel.ClearImageCacheCommand, ViewModel.CopyStreamDeckApiKeyCommand, ViewModel.InstallUpdateCommand, ViewModel.IsStreamDeckServerEnabled, ViewModel.OpenImageCacheFolderCommand (+27 more)

### Community 17 - "User Options Data Model"
Cohesion: 0.06
Nodes (27): Control, StreamFlow.Core.Data.UserOptions, Enum, IEnumerable, CultureInfo, Type, UserOptionCategoryConverter, DependencyProperty (+19 more)

### Community 18 - "Video Overlay ViewModel"
Cohesion: 0.07
Nodes (17): StreamFlow.App.ViewModels, VideoFrame, Task, ViewModel, Action, bool, DependencyProperty, DependencyPropertyChangedEventArgs (+9 more)

### Community 19 - "Rust IPC & Capture Core"
Cohesion: 0.08
Nodes (29): NamedPipeServer, auth_command_encodes_correctly(), capture::CaptureSession, capture_mf::MFCaptureSession, CaptureSessionTrait, create_shm_overlay(), main(), read_auth_from_stdin() (+21 more)

### Community 20 - "Rust Compositor Render Backend"
Cohesion: 0.08
Nodes (28): Backend, c_void, Display, Facade, GliumContext, LRESULT, create_hidden_window(), def_window_proc() (+20 more)

### Community 21 - "Main Window ViewModel"
Cohesion: 0.06
Nodes (23): CollectionViewSource, bool, DispatcherTimer, ObservableCollection, PropertyChangedEventArgs, PropertyInfo, Type, DebugViewModel (+15 more)

### Community 22 - "Coding Rules & Stream Deck UI"
Cohesion: 0.06
Nodes (37): Code Style Guide (agents rules), No architectural changes without approval, UI (Electron) must not do heavy lifting; offload expensive processing to Core, Performance is priority unless it significantly sacrifices quality, pnpm-only package management / pnpm start policy, Post-edit build tests required (tsx, vite, lint, etc.), ffmpeg-sys-next README, Compile-time feature flags (ffmpeg_x_y, avcodec_version_greater_than_x_y, ff_api flags) (+29 more)

### Community 23 - "Audio Playback Engine"
Cohesion: 0.11
Nodes (21): AudioFormatInfo, IObservable, PlaybackState, Action, AssetDataProvider, AudioAnalyzer, AudioFormat, CancellationToken (+13 more)

### Community 24 - "App Namespace Structure"
Cohesion: 0.10
Nodes (12): StreamFlow.App, StreamFlow.App.Views.Pages, StreamFlow.App.ViewModels.Pages, StreamFlow.App.Services, StreamFlow.App.Rendering, StreamFlow.App.ViewModels.Windows, StreamFlow.App.Services.Core, InfoBarSeverity (+4 more)

### Community 25 - "Slot Placement Behavior"
Cohesion: 0.12
Nodes (13): Canvas, DependencyObject, DependencyProperty, DependencyPropertyChangedEventArgs, double, DragCompletedEventArgs, DragDeltaEventArgs, DragStartedEventArgs (+5 more)

### Community 26 - "Audio Device Engine"
Cohesion: 0.09
Nodes (16): AudioCaptureDevice, AudioPlaybackDevice, MiniAudioEngine, ReadOptions, Action, AudioFormat, CancellationToken, DeviceInfo (+8 more)

### Community 27 - "Audio Channel Strip Control"
Cohesion: 0.07
Nodes (32): DeviceVolumePercent, DisplayDb, IsDeviceMuted, IsMuted, IsRenamed, IsSolo, ResetDisplayNameCommand, SmoothedMeterFillFraction (+24 more)

### Community 28 - "Global Keyboard Hotkey Listener"
Cohesion: 0.08
Nodes (17): StreamFlow.Core.Helpers.KeyboardListener, HandledEventArgs, MarshalAs, nint, SafeHandleZeroOrMinusOneIsInvalid, Key, KeyboardEventArgs, int (+9 more)

### Community 29 - "FFmpeg Bindgen Build Script"
Cohesion: 0.15
Nodes (27): EnumVariantCustomBehavior, EnumVariantValue, IntKind, MacroParsingBehavior, build(), Callbacks, check_features(), fetch() (+19 more)

### Community 30 - "Drag & Drop Behavior"
Cohesion: 0.14
Nodes (10): DragState, DependencyObject, DependencyProperty, DependencyPropertyChangedEventArgs, FrameworkElement, List, MouseButtonEventArgs, MouseEventArgs (+2 more)

### Community 31 - "Stream Deck Plugin Build Tooling"
Cohesion: 0.06
Nodes (29): @elgato/cli, @elgato/streamdeck, rollup, @rollup/plugin-commonjs, @rollup/plugin-node-resolve, @rollup/plugin-terser, @rollup/plugin-typescript, dependencies (+21 more)

### Community 32 - "Src Module"
Cohesion: 0.16
Nodes (26): HeapProd, IAudioEndpointVolume, ActiveStream, enumerate_audio_devices_mmdevapi(), get_audio_devices(), get_device_volume(), resolve_endpoint_volume(), Drop (+18 more)

### Community 33 - "Windows Module"
Cohesion: 0.10
Nodes (13): CanExecuteRoutedEventArgs, CommandBinding, DependencyObject, DispatcherUnhandledExceptionEventArgs, DllImport, double, EventArgs, InfoBarSeverity (+5 more)

### Community 34 - "Src Module"
Cohesion: 0.14
Nodes (25): FnGetLevel, FnGetVoicemeeterType, FnLogout, HKEY, HMODULE, Bindings, find_install_dir_from_registry(), find_remote_dll() (+17 more)

### Community 35 - "Src Module"
Cohesion: 0.16
Nodes (16): HANDLE, MEMORY_MAPPED_VIEW_ADDRESS, deregister_sender_name(), get_adapter_luid(), NamedMap, register_sender_name(), Drop, ID3D11Device (+8 more)

### Community 36 - "Src Module"
Cohesion: 0.12
Nodes (20): audio_devices_event_encodes(), auth_round_trips(), capture_started_round_trips(), capture_stopped_round_trips(), ChromaKeyDef, encode_command(), encode_event(), error_event_round_trips() (+12 more)

### Community 37 - "Pages Module"
Cohesion: 0.12
Nodes (9): bool, float, List, ObservableCollection, RelayCommand, string, uint, GoLiveViewModel (+1 more)

### Community 39 - "Src Module"
Cohesion: 0.16
Nodes (25): Condvar, apply_blur_region(), bgra_blend(), BlurEngine, chroma_mask(), composite_frame(), CompositeFrame, CompositorConfig (+17 more)

### Community 40 - "Misc Module"
Cohesion: 0.13
Nodes (24): Direct3D11CaptureFrame, Direct3D11CaptureFramePool, GraphicsCaptureSession, IDirect3DDevice, CaptureSession, create_d3d11_device(), create_staging_texture(), GpuState (+16 more)

### Community 41 - "Controls Module"
Cohesion: 0.11
Nodes (11): bool, DependencyObject, DependencyProperty, DependencyPropertyChangedEventArgs, DragCompletedEventArgs, DragDeltaEventArgs, DragStartedEventArgs, FrameworkElement (+3 more)

### Community 42 - "Theme Module"
Cohesion: 0.10
Nodes (25): Maximum, Minimum, BaseTrack, Fill, HoverOverlay, HoverRing, PART_Thumb, PART_Track (+17 more)

### Community 43 - "Src Module"
Cohesion: 0.15
Nodes (18): check(), CodecCtx, FormatCtx, OwnedAvFrame, OwnedAvPacket, probe_dimensions(), Arc, AtomicBool (+10 more)

### Community 45 - "Audio Handling Module"
Cohesion: 0.11
Nodes (9): CancelEventArgs, IObserver, string, Subscription, IDisposable, IObserver, PlaybackState, Statuses (+1 more)

### Community 46 - "Behaviors Module"
Cohesion: 0.11
Nodes (13): AudioSegment, Composition, StreamFlow.App.ViewModels.Pages.Compose, StreamFlow.App.Models.Canvas, StreamFlow.App.Helpers.Behaviors, RichCanvasPanel, Point, DragState (+5 more)

### Community 47 - "Windows Module"
Cohesion: 0.10
Nodes (23): AppModelInstance.Settings.OutputDevice, Current, ViewModel.ApplicationTitle, ViewModel.CoreStatsText, ViewModel.CoreStatusBrush, ViewModel.CoreStatusText, ViewModel.HasSoundEffectsPlaying, ViewModel.IsDebug (+15 more)

### Community 48 - "Persistence Module"
Cohesion: 0.12
Nodes (12): JsonSerializerSettings, ImageSource, ObservableCollection, string, Scene, Task, IPersistenceDataManager, string (+4 more)

### Community 49 - "Misc Module"
Cohesion: 0.13
Nodes (14): NotifyCollectionChangedEventArgs, Random, Row, CanvasAudioKind, bool, double, string, Visibility (+6 more)

### Community 50 - "Pages Module"
Cohesion: 0.11
Nodes (7): RelayCommand, bool, double, ImageSource, int, string, SourceSlot

### Community 51 - "Pages Module"
Cohesion: 0.10
Nodes (22): AddAudioCommand, CopyAudioUriCommand, EditAudioPropertiesCommand, PlayAudioItemCommand, QueueAudioItemCommand, QueueAudioItemNextCommand, RemoveAudioCommand, SetAudioViewCommand (+14 more)

### Community 52 - "Controls Module"
Cohesion: 0.09
Nodes (22): Audio, FilePath, HasMetadata, Hotkey, Metadata.BitsPerSample, Metadata.ChannelCount, Metadata.Duration, Metadata.FormatName (+14 more)

### Community 53 - "Src Module"
Cohesion: 0.20
Nodes (21): HDC, HMONITOR, capture_item_for_hwnd(), capture_item_for_id(), capture_item_for_monitor(), capture_item_for_primary_monitor(), enumerate(), enumerate_monitors() (+13 more)

### Community 54 - "Misc Module"
Cohesion: 0.12
Nodes (13): bool, CancellationTokenSource, EventArgs, float, ILogger, int, ObservableCollection, Queue (+5 more)

### Community 55 - "Controls Module"
Cohesion: 0.10
Nodes (21): AudioTrack.ImageSource, CanStop, ElapsedText, IsChecked, IsRepeatEnabled, IsShuffleEnabled, PlayNextTrackCommand, PlayPreviousTrackCommand (+13 more)

### Community 56 - "Converter Module"
Cohesion: 0.12
Nodes (13): List, CultureInfo, IValueConverter, Type, LookupTableHelper, CultureInfo, IValueConverter, Type (+5 more)

### Community 57 - "Misc Module"
Cohesion: 0.11
Nodes (20): RawFrame, Drop, Vec, ActiveTransition, blend_transition(), Arc, Duration, Instant (+12 more)

### Community 58 - "Behaviors Module"
Cohesion: 0.16
Nodes (9): ResizeState, DependencyObject, DependencyProperty, DependencyPropertyChangedEventArgs, DragCompletedEventArgs, DragDeltaEventArgs, DragStartedEventArgs, CanvasResizeBehavior (+1 more)

### Community 59 - "Controls Module"
Cohesion: 0.11
Nodes (20): MeterFillFraction, MasterMeterBars, MasterMeterClip1, MasterMeterClip2, MeterHost, PART_Track, Root, SlotInner (+12 more)

### Community 60 - "Misc Module"
Cohesion: 0.12
Nodes (14): BitmapSource, Canvas, Color, DllImport, double, Image, int, IntPtr (+6 more)

### Community 61 - "Misc Module"
Cohesion: 0.12
Nodes (15): AVAudioFifo, F, AudioEncoder, AVCodecContext, AVFormatContext, AVFrame, AVPacket, AVRational (+7 more)

### Community 62 - "Helpers Module"
Cohesion: 0.15
Nodes (12): EventArgs, Form, ManualResetEvent, Message, MessageWindow, DllImport, int, IntPtr (+4 more)

### Community 63 - "Controls Module"
Cohesion: 0.14
Nodes (10): double, EventArgs, float, int, MouseButtonEventArgs, RoutedEventArgs, SizeChangedEventArgs, Visibility (+2 more)

### Community 64 - "Workflows Module"
Cohesion: 0.12
Nodes (19): .NET Core Desktop workflow (build/test/MSIX/Velopack), build job, Build Rust core step (separately labeled so native build failures surface distinctly), Determine version step (tag push vs workflow_dispatch vs placeholder 0.0.1-dev), Execute unit tests step (dotnet test StreamFlow.Tests), Cache/download FFmpeg dev package (Ffmpeg_Dev_Dir must match native cargo config FFMPEG_DIR), MSIX packaging (Windows Application Packaging project, signed with pfx), Velopack_App_Id env var (must match UpdateService GithubSource lookup on release feed) (+11 more)

### Community 65 - "Misc Module"
Cohesion: 0.12
Nodes (12): StopMode, bool, double, float, ImageSource, int, List, ObservableCollection (+4 more)

### Community 66 - "Behaviors Module"
Cohesion: 0.18
Nodes (7): DependencyObject, DependencyProperty, DependencyPropertyChangedEventArgs, DragEventArgs, ICommand, MouseButtonEventArgs, SlotReorderBehavior

### Community 67 - "Pages Module"
Cohesion: 0.13
Nodes (6): bool, double, float, RelayCommand, string, AudioSourceItem

### Community 68 - "Resources Module"
Cohesion: 0.14
Nodes (17): ., ViewModel.AudioTrack, AudioItemContextFlyout, AudioSubText, CopyUriMenuIcon, DeleteMenuIcon, EditMenuIcon, PlayMenuIcon (+9 more)

### Community 69 - "Misc Module"
Cohesion: 0.14
Nodes (12): BitmapImage, Cursor, ImageFormat, Pen, BitmapSource, Brush, DllImport, Image (+4 more)

### Community 70 - "Stream Flow Tests Module"
Cohesion: 0.12
Nodes (9): StreamFlow.App.Tests, Fact, AppOptionsTests, Fact, AudioViewModelTests, Fact, DiSmokeTests, Fact (+1 more)

### Community 71 - "Misc Module"
Cohesion: 0.12
Nodes (11): MarkupExtension, IServiceProvider, Type, EnumBindingSourceExtension, IServiceProvider, List, Type, EnumCollectionExtension (+3 more)

### Community 72 - "Controls Module"
Cohesion: 0.14
Nodes (8): bool, DependencyProperty, DragEventArgs, KeyEventArgs, RelayCommand, RoutedEventArgs, PropertiesEditor, SoundFormatInfo

### Community 73 - "Helpers Module"
Cohesion: 0.14
Nodes (3): IShellLinkW, StringBuilder, WIN32_FIND_DATAW

### Community 74 - "Pages Module"
Cohesion: 0.14
Nodes (7): double, ObservableCollection, string, uint, GoLiveSceneViewModel, Height, Width

### Community 75 - "Audio Handling Module"
Cohesion: 0.12
Nodes (10): AudioAnalyzer, ISoundDataProvider, float, object, ReadOnlySpan, RealtimeWaveformAnalyzer, SampleFormat, SoundFormatInfo (+2 more)

### Community 76 - "Rendering Module"
Cohesion: 0.16
Nodes (8): D3DImage, IDirect3D9Ex, IDirect3DDevice9Ex, IDirect3DSurface9, IDirect3DTexture9, uint, SpoutPreviewRenderer, EventArgs

### Community 77 - "Stream Flow App Module"
Cohesion: 0.14
Nodes (9): ExitEventArgs, IHost, Application, DispatcherUnhandledExceptionEventArgs, IServiceProvider, Process, string, Task (+1 more)

### Community 78 - "Misc Module"
Cohesion: 0.14
Nodes (11): ModifierOption, bool, double, Geometry, List, ListView, PropertyChangedEventArgs, RoutedEventArgs (+3 more)

### Community 79 - "Rendering Module"
Cohesion: 0.26
Nodes (7): Pixels, Color, double, Height, int, Width, OverlayContentRenderer

### Community 80 - "Misc Module"
Cohesion: 0.13
Nodes (13): Player, Resources, IDisposable, ISoundDataProvider, Result, SoundPlayer, Stream, Task (+5 more)

### Community 81 - "Audio Properties Module"
Cohesion: 0.18
Nodes (7): UriExtensions, string, TimeSpan, LoopPoint, List, TimeSpan, LoopPointExtensions

### Community 82 - "Windows Module"
Cohesion: 0.17
Nodes (10): PropertyType, Task, List, UIElement, WindowHelper, Window, Name, Value (+2 more)

### Community 83 - "Misc Module"
Cohesion: 0.13
Nodes (9): ContentControl, CornerRadius, DependencyObject, DependencyProperty, DependencyPropertyChangedEventArgs, InfoBarSeverity, FontIconSource, InfoBar (+1 more)

### Community 84 - "Services Module"
Cohesion: 0.19
Nodes (6): Dispatcher, ILogger, Task, ProtocolHandlerService, TimeSpan, TimeSpan

### Community 85 - "Services Module"
Cohesion: 0.24
Nodes (7): CancellationToken, HttpClient, HttpListenerResponse, string, Task, TwitchAuthResult, TwitchAuthService

### Community 86 - "Misc Module"
Cohesion: 0.16
Nodes (10): bool, Dictionary, double, ICollectionView, int, List, ObservableCollection, RelayCommand (+2 more)

### Community 88 - "Misc Module"
Cohesion: 0.19
Nodes (11): ID2D1Bitmap1, ID2D1DeviceContext, create_render_target(), D2DCompositor, ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D, Result (+3 more)

### Community 89 - "Data Module"
Cohesion: 0.19
Nodes (8): bool, DispatcherTimer, List, object, ObservableCollection, PropertyChangedEventArgs, TimeSpan, AppModel

### Community 90 - "Com Streamfloworg Streamflow Sd Sd Plugin Module"
Cohesion: 0.13
Nodes (14): Actions, Author, Category, CategoryIcon, CodePath, Description, Icon, Name (+6 more)

### Community 91 - "Windows Module"
Cohesion: 0.14
Nodes (14): AudioViewModel.IsPlaying, AudioViewModel.PlayAudioCommand, AudioViewModel.StopAudioCommand, AudioViewModel.VolumeDecreaseCommand, AudioViewModel.VolumeIncreaseCommand, AudioViewModel.VolumeMuteCommand, MediaButtonSelectorConverter, MediaDescriptionSelectorConverter (+6 more)

### Community 92 - "Controls Module"
Cohesion: 0.14
Nodes (12): Background, BorderBrush, BorderThickness, HeaderText, HorizontalContentAlignment, Padding, VerticalContentAlignment, ParentRoot (+4 more)

### Community 93 - "Controls Module"
Cohesion: 0.19
Nodes (9): LoopPointName, AdonisWindow, NameTextBox, RoutedEventArgs, string, Task, TaskCompletionSource, LoopPointNameDialog (+1 more)

### Community 94 - "Compose Module"
Cohesion: 0.16
Nodes (8): StreamFlow.App.Controls.Compose, UserControl, AudioChannel, UserControl, ChannelControls, UserControl, CompositionSegment, UserControl

### Community 95 - "Src Module"
Cohesion: 0.20
Nodes (13): Error, ping_encodes_and_pong_decodes(), decode_event(), decode_frame_header(), encode_frame_header(), frame_header_encode_decode_round_trips(), frame_header_is_little_endian(), FrameError (+5 more)

### Community 96 - "Behaviors Module"
Cohesion: 0.26
Nodes (5): DependencyObject, DependencyProperty, DependencyPropertyChangedEventArgs, RoutedEventArgs, PasswordBoxHelper

### Community 97 - "Pages Module"
Cohesion: 0.14
Nodes (5): NativeAudioDevice, bool, RelayCommand, string, GoLiveViewModel

### Community 98 - "Src Module"
Cohesion: 0.19
Nodes (10): IDXGIAdapter3, create_dxgi_adapter(), ProcessStats, ProcessStatsSampler, Duration, Instant, Option, Self (+2 more)

### Community 99 - "Controls Module"
Cohesion: 0.31
Nodes (6): IMultiValueConverter, CultureInfo, Type, FractionToPixelConverter, InverseFractionToPixelConverter, SizeToRectConverter

### Community 100 - "Misc Module"
Cohesion: 0.26
Nodes (10): MFCaptureSession, Arc, AtomicBool, JoinHandle, Option, Result, Self, Sender (+2 more)

### Community 101 - "Controls Module"
Cohesion: 0.18
Nodes (7): Panel, Dictionary, Size, FluidStackPanel, Dictionary, Size, FluidWrapPanel

### Community 102 - "Stream Flow Stream Deck Module"
Cohesion: 0.15
Nodes (12): node, node_modules, src/**/*.ts, @tsconfig/node20/tsconfig.json, compilerOptions, customConditions, module, moduleResolution (+4 more)

### Community 103 - "Contracts Module"
Cohesion: 0.17
Nodes (5): Complex, float, int, SampleAggregator, ISampleAggregator

### Community 104 - "Contracts Module"
Cohesion: 0.17
Nodes (7): StreamFlow.Core.Contracts, List, IAudio, ISampleNotifier, SampleEventArgs, TimeSpan, ITimedPlayback

### Community 105 - "Misc Module"
Cohesion: 0.17
Nodes (8): StreamFlow.Core.Sorting, ICloneable, ListSortDirection, bool, ObservableCollection, string, FilterOptions, SortType

### Community 106 - "Misc Module"
Cohesion: 0.21
Nodes (12): AudioSourceConfig, Command, ErrorCode, Event, AudioSourceConfig, ChromaKeyDef, Option, StreamSourceDef (+4 more)

### Community 107 - "Misc Module"
Cohesion: 0.18
Nodes (8): RECT, DllImport, int, IntPtr, string, uint, RECT, WindowPlacementService

### Community 108 - "Audio Properties Module"
Cohesion: 0.20
Nodes (4): Color, string, Category, Color

### Community 109 - "Actions Module"
Cohesion: 0.27
Nodes (4): SceneControl, SceneControlSettings, action, SceneState

### Community 110 - "Controls Module"
Cohesion: 0.27
Nodes (6): AudioToDelete.Name, AdonisWindow, RoutedEventArgs, string, TaskCompletionSource, AudioDeletion

### Community 111 - "Pages Module"
Cohesion: 0.25
Nodes (11): ViewModel.CanEditSlots, MoveThumb, MoveThumbBorder, ResizeThumb, ResizeThumbBorder, BooleanToVisibilityConverter, SlotRoleBrush, IsPrimary (+3 more)

### Community 112 - "Converter Module"
Cohesion: 0.38
Nodes (6): BindingBase, CommandBinding, DependencyProperty, ICommand, UIElement, CommandBindingHelper

### Community 113 - "Avutil Module"
Cohesion: 0.27
Nodes (7): c_double, av_cmp_q(), av_inv_q(), av_make_q(), av_q2d(), AVRational, c_int

### Community 114 - "Audio Handling Module"
Cohesion: 0.24
Nodes (6): CustomCreationConverter, JsonReader, JsonSerializer, JsonWriter, Type, AudioTypeConverter

### Community 116 - "Misc Module"
Cohesion: 0.20
Nodes (7): bool, CancellationToken, CancellationTokenSource, Mutex, string, Task, SingleInstanceManager

### Community 117 - "Services Module"
Cohesion: 0.18
Nodes (10): AudioSourceRequest, AudioStateResponse, AudioVolumeRequest, RecordingToggleRequest, SceneCycleRequest, SceneResponse, SceneStateResponse, SceneSwitchRequest (+2 more)

### Community 118 - "Actions Module"
Cohesion: 0.27
Nodes (4): AudioControl, AudioControlSettings, action, AudioState

### Community 119 - "Actions Module"
Cohesion: 0.25
Nodes (4): MediaPlayback, MediaPlaybackSettings, action, MediaState

### Community 120 - "Behaviors Module"
Cohesion: 0.24
Nodes (5): Behavior, DependencyPropertyChangedEventArgs, ListView, SelectionChangedEventArgs, ScrollToSelectedListViewItemBehavior

### Community 121 - "Pages Module"
Cohesion: 0.22
Nodes (10): ViewModel.IsSpoutOutputEnabled, HorizontalGuide, PreviewImage, SpoutPreviewImage, VerticalGuide, BoolToVisibility, InverseBoolToVisibility, ViewModel.SceneEditor.ActiveScene.CanvasHeight (+2 more)

### Community 122 - "Helpers Module"
Cohesion: 0.40
Nodes (3): Logger, Type, LoggerService

### Community 123 - "Controls Module"
Cohesion: 0.27
Nodes (5): DispatcherTimer, EventArgs, Task, TaskCompletionSource, SimpleDialog

### Community 124 - "Pages Module"
Cohesion: 0.27
Nodes (10): MoveThumb, MoveThumbBorder, ResizeThumb, ResizeThumbBorder, BooleanToVisibilityConverter, SlotRoleBrush, IsPrimary, IsSelected (+2 more)

### Community 125 - "Windows Module"
Cohesion: 0.24
Nodes (7): ContentFrame, NavMenu, NavMenuFooter, Page, SelectionChangedEventArgs, Frame, ListBox

### Community 126 - "Audio Properties Module"
Cohesion: 0.24
Nodes (4): bool, string, AudioTag, SelectableTag

### Community 127 - "Controls Module"
Cohesion: 0.22
Nodes (9): AudioTrack.Name, TimeSpanToDoubleConverter, Duration, SeekPosition, TrackLoaded, AudioTitle, PlaybackProgressSlider, TextBlock (+1 more)

### Community 128 - "Helpers Module"
Cohesion: 0.25
Nodes (7): FILETIME, Guid, string, uint, PROPERTYKEY, ShellLink, WIN32_FIND_DATAW

### Community 129 - "Helpers Module"
Cohesion: 0.28
Nodes (3): PROPERTYKEY, EventArgs, IPropertyStore

### Community 130 - "Services Module"
Cohesion: 0.28
Nodes (3): StartupEventArgs, string, ProtocolRegistration

### Community 131 - "Helpers Module"
Cohesion: 0.44
Nodes (4): Storyboard, FrameworkElement, Task, DialogAnimationHelper

### Community 133 - "Services Module"
Cohesion: 0.28
Nodes (4): HotkeyConflictService, Key, ModifierKeys, Hotkey

### Community 134 - "Pages Module"
Cohesion: 0.25
Nodes (6): Dictionary, List, string, uint, StreamingProfile, StreamServiceKind

### Community 135 - "Pages Module"
Cohesion: 0.28
Nodes (4): KeyEventArgs, RoutedEventArgs, ScrollChangedEventArgs, ScenesView

### Community 136 - "Pages Module"
Cohesion: 0.25
Nodes (5): AppModel.Instance.Settings.FilterOptions.SearchTerm, SearchBox, KeyEventArgs, TextBox, TextChangedEventArgs

### Community 137 - "Converter Module"
Cohesion: 0.32
Nodes (4): StreamFlow.App.Converter, CultureInfo, Type, PercentToFractionConverter

### Community 138 - "Misc Module"
Cohesion: 0.39
Nodes (5): IHostedService, CancellationToken, IServiceProvider, Task, ApplicationHostService

### Community 139 - "Misc Module"
Cohesion: 0.25
Nodes (5): ProgressBar, Slider, TextBlock, TimeSpan, AnimationExtension

### Community 140 - "Com Streamfloworg Streamflow Sd Sd Plugin Module"
Cohesion: 0.29
Nodes (5): EventArgs, MouseEventArgs, Nodejs, Debug, Version

### Community 141 - "Audio Handling Module"
Cohesion: 0.25
Nodes (6): int, SampleFormat, AudioFormatExtended, Channels, SampleFormatExtended, WaveformatEncoding

### Community 143 - "Misc Module"
Cohesion: 0.33
Nodes (5): AdonisWindow, int, Task, TaskCompletionSource, ProgressDialog

### Community 144 - "Audio Properties Module"
Cohesion: 0.33
Nodes (4): IEquatable, bool, AudioType, SelectableAudioType

### Community 145 - "Misc Module"
Cohesion: 0.38
Nodes (5): IValueConverter, CultureInfo, double, Type, AspectRatioTextConverter

### Community 147 - "Helpers Module"
Cohesion: 0.52
Nodes (3): PROPVARIANT, DllImport, NotificationHelper

### Community 148 - "Helpers Module"
Cohesion: 0.29
Nodes (4): short, int, IntPtr, PROPVARIANT

### Community 149 - "Converter Module"
Cohesion: 0.38
Nodes (4): SolidColorBrush, CultureInfo, Type, SlotRoleBrushConverter

### Community 150 - "Converter Module"
Cohesion: 0.38
Nodes (4): CultureInfo, Style, Type, AudioViewStyleSelector

### Community 151 - "Converter Module"
Cohesion: 0.43
Nodes (3): CultureInfo, Type, EnumerableHasAnyConverter

### Community 152 - "Converter Module"
Cohesion: 0.38
Nodes (4): CultureInfo, string, Type, HotkeyConverter

### Community 153 - "Converter Module"
Cohesion: 0.43
Nodes (3): CultureInfo, Type, SliderFillWidthConverter

### Community 154 - "Converter Module"
Cohesion: 0.43
Nodes (3): CultureInfo, Type, TimeSpanToDoubleConverter

### Community 155 - "Resources Module"
Cohesion: 0.29
Nodes (7): CopyUriMenuItem, DeleteMenuItem, EditMenuItem, PlayMenuItem, QueueMenuItem, QueueNextMenuItem, MenuItem

### Community 156 - "Audio Handling Module"
Cohesion: 0.33
Nodes (3): AudioFormat, AudioTrack, NullAudio

### Community 157 - "Pages Module"
Cohesion: 0.33
Nodes (3): AudioViewGrid, DragEventArgs, Grid

### Community 158 - "Pages Module"
Cohesion: 0.29
Nodes (6): Bar, PreviewContainer, Root, Track, SizeChangedEventArgs, Border

### Community 159 - "Data Module"
Cohesion: 0.33
Nodes (5): bool, double, string, ApplicationSettings, AudioViewType

### Community 162 - "Src Module"
Cohesion: 0.38
Nodes (5): StreamControlSettings, ConnectionState, GlobalSettings, Scene, StreamState

### Community 163 - "Helpers Module"
Cohesion: 0.33
Nodes (3): BinaryWriter, MemoryStream, ExtentionMethods

### Community 164 - "Pages Module"
Cohesion: 0.33
Nodes (6): AudioListCollectionView, AudioViewStyleSelector, SelectedAudio, ViewType, AudioListView, ListView

### Community 165 - "Misc Module"
Cohesion: 0.33
Nodes (4): IDisposable, AssetDataProvider, Span, SampleNotifier

### Community 166 - "Src Module"
Cohesion: 0.53
Nodes (5): acquire(), acquire_empty(), acquire_uninit(), release(), Vec

### Community 167 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, AddValueConverter

### Community 168 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, AudioIsPlayingConverter

### Community 169 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, AudioStateToIconConverter

### Community 170 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, AudioTypeToAbbreviationConverter

### Community 171 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, BooleanInvertConverter

### Community 172 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, BooleanToObjectConverter

### Community 173 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, BooleanToResourceConverter

### Community 174 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, BooleanToSortDirectionConverter

### Community 175 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, BooleanToVisibilityConverter

### Community 176 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, BoolToFontStyleConverter

### Community 177 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, BoolToFontWeightConverter

### Community 178 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, CollectionCountToVisibilityConverter

### Community 179 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, ColorMediaBrushConverter

### Community 180 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, ColorToBrushConverter

### Community 181 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, CornerClipConverter

### Community 182 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, DebugDummyConverter

### Community 183 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, DecimalToAudioTime

### Community 184 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, DoubleScaleConverter

### Community 185 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, DoubleToPercentageConverter

### Community 186 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, EnumToStringConverter

### Community 187 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, FontSizeToMessageSpacingMarginConverter

### Community 188 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, GreaterThanZeroToVisibilityConverter

### Community 189 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, InverseBoolToVisibilityConverter

### Community 190 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, InvertedNullVisibilityConverter

### Community 191 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, IsDefaultAudioToBooleanConverter

### Community 192 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, KindToOpacityConverter

### Community 193 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, KindToVisibilityConverter

### Community 194 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, NextAudioTrackToBooleanConverter

### Community 195 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, NullVisibilityConverter

### Community 196 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, ObjectToPlayingStringConverter

### Community 197 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, PercentToDynamicPixelConverter

### Community 198 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, PercentToPixelConverter

### Community 199 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, PipOnlyVisibilityConverter

### Community 200 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, PrecisionConverter

### Community 201 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, RotationDegreesToIndexConverter

### Community 202 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, SceneTransitionKindToIndexConverter

### Community 203 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, TextHorizontalAlignmentToIndexConverter

### Community 204 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, TextHorizontalAlignmentToTextAlignmentConverter

### Community 205 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, TimeCodeConverter

### Community 206 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, TimerModeToIndexConverter

### Community 207 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, TimerModeToVisibilityConverter

### Community 208 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, ValueExcludeToBooleanConverter

### Community 209 - "Converter Module"
Cohesion: 0.47
Nodes (3): CultureInfo, Type, VisibilityToBooleanConverter

### Community 210 - "Services Module"
Cohesion: 0.40
Nodes (3): ILogger, List, GpuEncoderDetectionService

### Community 211 - "Stream Flow Core Module"
Cohesion: 0.53
Nodes (3): PlaybackState, PlaybackState, PlaybackStateExtensions

### Community 213 - "Actions Module"
Cohesion: 0.33
Nodes (3): CounterSettings, IncrementCounter, action

### Community 214 - "Windows Module"
Cohesion: 0.60
Nodes (5): Children, AudioControlPresenter, ibPresenter, NullVisibilityConverter, StackPanel

### Community 215 - "Misc Module"
Cohesion: 0.40
Nodes (4): DependencyPropertyKey, DependencyProperty, ObservableCollection, MasterVolumeControl

### Community 216 - "Data Module"
Cohesion: 0.40
Nodes (3): INotifyPropertyChanged, double, WindowOptions

### Community 217 - "Src Module"
Cohesion: 0.70
Nodes (4): compute_peaks(), compute_peaks_unsafe(), Result, Vec

### Community 218 - "Pages Module"
Cohesion: 0.60
Nodes (4): AllFilterBtn, SfxFilterBtn, TracksFilterBtn, RadioButton

### Community 219 - "Helpers Module"
Cohesion: 0.40
Nodes (3): Task, ToastNotification, ToastContentBuilder

### Community 220 - "Controls Module"
Cohesion: 0.50
Nodes (4): CanPlayPause, PlayAudioCommand, TopPlayPauseBtn, Button

### Community 221 - "Controls Module"
Cohesion: 0.50
Nodes (4): BooleanToResourceConverter, IsActivelyPlaying, PlayAudioTrackIcon, Path

### Community 224 - "Controls Module"
Cohesion: 0.50
Nodes (4): ButtonBorder, CheckedOverlay, HoverOverlay, Border

### Community 225 - "Resources Module"
Cohesion: 0.50
Nodes (4): _Border, ImageContainer, ImagePlaceholder, Border

### Community 226 - "Resources Module"
Cohesion: 0.50
Nodes (4): _Grid, GridViewItemGrid, ListViewItemGrid, Grid

### Community 228 - "Pages Module"
Cohesion: 0.50
Nodes (4): HorizontalGuide, VerticalGuide, ViewModel.SceneEditor.ActiveScene.CanvasHeight, Line

### Community 229 - "Data Module"
Cohesion: 0.50
Nodes (3): bool, int, SonicDebugger

### Community 231 - "Pages Module"
Cohesion: 0.67
Nodes (3): CurrentViewIconData, AudioViewTypeTogglePath, Path

### Community 232 - "Windows Module"
Cohesion: 0.67
Nodes (3): ItemsViewSource.Source, DebugDataGrid, DataGrid

### Community 233 - "Resources Module"
Cohesion: 0.67
Nodes (3): ViewModel.PlayAudioItemCommand, PlayPauseBtn, Button

### Community 238 - "Controls Module"
Cohesion: 0.67
Nodes (3): AudioController, ViewModel, UserControl

### Community 240 - "Pages Module"
Cohesion: 0.67
Nodes (3): AddLayerMenu, PlacementTarget.DataContext, ContextMenu

### Community 241 - "Pages Module"
Cohesion: 0.67
Nodes (3): CanvasResolutionDeviceCombo, ViewModel.SceneEditor.AvailableSources, ComboBox

### Community 242 - "Pages Module"
Cohesion: 0.67
Nodes (3): ChatMessagesItemsControl, Content.DisplayMessages, ItemsControl

### Community 243 - "Pages Module"
Cohesion: 0.67
Nodes (3): RowDeleteButton, ViewModel.SceneEditor.RemoveSlotCommand, Button

### Community 244 - "Pages Module"
Cohesion: 0.67
Nodes (3): AddLayerMenu, PlacementTarget.DataContext, ContextMenu

### Community 245 - "Pages Module"
Cohesion: 0.67
Nodes (3): CanvasResolutionDeviceCombo, ViewModel.SceneEditor.AvailableSources, ComboBox

### Community 246 - "Pages Module"
Cohesion: 0.67
Nodes (3): ChatMessagesItemsControl, Content.DisplayMessages, ItemsControl

### Community 247 - "Pages Module"
Cohesion: 0.67
Nodes (3): RowDeleteButton, ViewModel.SceneEditor.RemoveSlotCommand, Button

## Ambiguous Edges - Review These
- `UI (Electron) must not do heavy lifting; offload expensive processing to Core` → `Native Rust core (capture/compositing/streaming, talks to WPF app over IPC)`  [AMBIGUOUS]
  .agents/rules/code-style-guide.md · relation: conceptually_related_to

## Knowledge Gaps
- **654 isolated node(s):** `Value`, `ActualHeight`, `Track`, `TextBlock`, `ActualWidth` (+649 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **35 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `UI (Electron) must not do heavy lifting; offload expensive processing to Core` and `Native Rust core (capture/compositing/streaming, talks to WPF app over IPC)`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `MainWindow` connect `Windows Module` to `Scene Editor ViewModel`, `Source Slot Capture ViewModel`, `Scene Editor View Converters`, `Pages Module`, `Core Namespace Structure`, `Misc Module`, `Settings View Bindings`, `Video Overlay ViewModel`, `Main Window ViewModel`, `Global Keyboard Hotkey Listener`, `Misc Module`, `Audio Handling Module`, `Windows Module`, `Pages Module`, `Misc Module`, `Data Module`, `Data Module`, `Windows Module`, `Windows Module`?**
  _High betweenness centrality (0.189) - this node is a cross-community bridge._
- **Why does `StreamFlow.App.ViewModels.Pages` connect `App Namespace Structure` to `Stream Deck Server & EventBus`, `Services Module`, `Pages Module`, `Scene Editor View Converters`, `Audio Command Queue`, `Twitch Chat ViewModel`, `Core Namespace Structure`, `Slot Placement Behavior`, `Behaviors Module`, `Converter Module`, `Converter Module`, `Stream Flow Tests Module`, `Converter Module`, `Converter Module`, `Converter Module`, `Converter Module`, `Converter Module`, `Misc Module`, `Pages Module`, `Controls Module`, `Pages Module`, `Services Module`?**
  _High betweenness centrality (0.188) - this node is a cross-community bridge._
- **Why does `ComposeViewModel` connect `Misc Module` to `Converter Module`, `Scene Editor View Converters`, `Behaviors Module`, `Misc Module`, `Main Window ViewModel`, `Drag & Drop Behavior`?**
  _High betweenness centrality (0.172) - this node is a cross-community bridge._
- **What connects `Value`, `ActualHeight`, `Track` to the rest of the system?**
  _662 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `GoLive & Settings View Bindings` be split into smaller, more focused modules?**
  _Cohesion score 0.011560693641618497 - nodes in this community are weakly interconnected._
- **Should `Scene Set Browser View` be split into smaller, more focused modules?**
  _Cohesion score 0.015748031496062992 - nodes in this community are weakly interconnected._