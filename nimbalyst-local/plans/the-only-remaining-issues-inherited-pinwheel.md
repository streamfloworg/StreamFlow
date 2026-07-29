# Plan: Modular Overlay Inspector via Property Sections

## Context

The overlay properties panel in `GoLiveView.xaml` and `ScenesView.xaml` is ~300 lines of `Visibility`-flag soup duplicated in both files. `SourceSlot` carries 9 `Is*Overlay` bool properties (`IsImageOverlay`, `IsTextOverlay`, …) that the panel uses to show/hide type-specific controls. Adding a new overlay type — especially from a plugin — requires editing both XAML files and adding a new flag. The canvas preview section in the same panel uses an identical type-flag DataTrigger approach.

This plan replaces the flag system with a **property section model**: each `IOverlayTypeDescriptor` returns an ordered `IReadOnlyList<IOverlayPropertySection>` for the selected slot. A generic `ItemsControl` + WPF `DataType`-implicit `DataTemplate`s renders any combination without knowing overlay types. The canvas preview section is replaced by a `DataTemplateSelector`. Plugin overlays participate without modifying app XAML.

---

## Section Type System

### Location

All **primitive section types** go in `StreamFlow.Plugin.SDK` (so plugin descriptors can use them). **Composite app-specific section types** (`TextStyleSection`, `ChromaKeySection`, `AlertSubLayerManagerSection`, `GroupMembershipSection`, `CommandGroupSection`) go in `StreamFlow.App` (they reference App types like `TextStyle`, `AlertOverlayContent`, etc.).

### Primitive section types (SDK — `StreamFlow.Plugin.SDK/Overlays/Sections/`)

Each is a plain `ObservableObject` (CommunityToolkit) with a `Label`, control-type metadata, and getter/setter closures. Closures read live from the content instance. An optional `(INotifyPropertyChanged? source, string? propertyName)` pair lets the section re-fire `PropertyChanged(nameof(Value))` when the underlying content property changes externally (e.g. a reset-to-default action).

```csharp
public interface IOverlayPropertySection { }

public sealed class SliderSection : ObservableObject, IOverlayPropertySection {
    public string Label { get; }
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }           // 0 = continuous
    public string? Format { get; }        // e.g. "{0:0}%"
    public double Value { get => _get(); set { _set(value); OnPropertyChanged(); } }
    // constructor: (label, get, set, min, max, step=0, format=null, INotifyPropertyChanged? source=null, string? propName=null)
}

public sealed class TextBoxSection : ObservableObject, IOverlayPropertySection {
    public string Label { get; }
    public bool IsMultiLine { get; }
    public string? Value { get => _get(); set { _set(value); OnPropertyChanged(); } }
}

public sealed class ToggleSection : ObservableObject, IOverlayPropertySection {
    public string Label { get; }
    public bool Value { get => _get(); set { _set(value); OnPropertyChanged(); } }
}

public sealed class ColorSection : ObservableObject, IOverlayPropertySection {
    public string Label { get; }
    public Color Value { get => _get(); set { _set(value); OnPropertyChanged(); } }
}

public sealed class FilePickerSection : ObservableObject, IOverlayPropertySection {
    public string Label { get; }
    public string Filter { get; }         // e.g. "Images|*.png;*.jpg"
    public string? Value { get => _get(); set { _set(value); OnPropertyChanged(); } }
}

public sealed class ComboSection : ObservableObject, IOverlayPropertySection {
    public string Label { get; }
    public IEnumerable Options { get; }
    public object? Value { get => _get(); set { _set(value); OnPropertyChanged(); } }
}

public sealed class InfoSection : IOverlayPropertySection {
    public string Text { get; }           // read-only descriptive label
}

public sealed class SeparatorSection : IOverlayPropertySection { }
```

### Composite section types (App — `StreamFlow.App/Services/Overlays/Sections/`)

```csharp
// Wraps a TextStyle ObservableObject directly — DataTemplate binds Style.FontFamily etc.
public sealed class TextStyleSection : IOverlayPropertySection {
    public TextStyle Style { get; }
}

// Wraps IChromaKeyable — DataTemplate binds Target.ChromaKeyEnabled etc.
public sealed class ChromaKeySection : IOverlayPropertySection {
    public IChromaKeyable Target { get; }
}

// One named button entry in a group
public sealed record CommandEntry(string Label, ICommand Command, object? Parameter = null);

// For timer Start/Pause/Reset or any command group
public sealed class CommandGroupSection : IOverlayPropertySection {
    public string? Header { get; }
    public IReadOnlyList<CommandEntry> Commands { get; }
}

// Alert overlay's sub-layer list + Add/Remove management
// (commands passed in by SceneEditorViewModel so descriptor doesn't know ViewModel)
public sealed class AlertSubLayerManagerSection : IOverlayPropertySection {
    public AlertOverlayContent AlertContent { get; }
    public ICommand AddSubLayerCommand { get; }
    public ICommand RemoveSubLayerCommand { get; }
}

// Group overlay's child-membership candidate list
public sealed class GroupMembershipSection : IOverlayPropertySection {
    public IEnumerable<object> Candidates { get; }   // AvailableGroupCandidates from SceneEditorViewModel
}
```

---

## IOverlayTypeDescriptor Changes

Add one method to the interface in `StreamFlow.Plugin.SDK/IOverlayTypeDescriptor.cs`:

```csharp
IReadOnlyList<IOverlayPropertySection> GetInspectorSections(IOverlayContent content);
```

### Descriptor implementations

Each descriptor returns sections for its content type. Examples:

**BlurOverlayTypeDescriptor:**
```csharp
public IReadOnlyList<IOverlayPropertySection> GetInspectorSections(IOverlayContent content) {
    var blur = (BlurOverlayContent)content;
    return [new SliderSection("Blur Strength", () => blur.BlurRadius, v => blur.BlurRadius = v, 0, 100, source: blur, propName: nameof(blur.BlurRadius))];
}
```

**TextOverlayTypeDescriptor:**
```csharp
return [
    new TextBoxSection("Text", () => text.OverlayText, v => text.OverlayText = v, isMultiLine: true),
    new TextStyleSection(text.Style),
];
```

**ImageOverlayTypeDescriptor:**
```csharp
return [
    new FilePickerSection("Image File", () => img.ImagePath, v => img.ImagePath = v, "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp"),
    new ChromaKeySection(img),
];
```

**VideoOverlayTypeDescriptor:**
```csharp
return [
    new FilePickerSection("Video File", () => vid.VideoPath, v => vid.VideoPath = v, "Videos|*.mp4;*.mov;*.mkv;*.webm;*.avi"),
    new ToggleSection("Loop", () => vid.LoopVideo, v => vid.LoopVideo = v),
    new ChromaKeySection(vid),
];
```

**ColorOverlayTypeDescriptor:**
```csharp
return [new ColorSection("Fill Color", () => color.OverlayColor ?? Colors.White, v => color.OverlayColor = v)];
```

**TimerOverlayTypeDescriptor** (descriptor returns property sections only; Start/Pause/Reset buttons are appended by SceneEditorViewModel):
```csharp
return [
    new ComboSection("Mode", TimerOverlayContent.AllModes, () => timer.TimerMode, v => timer.TimerMode = (TimerMode)v!),
    new TextBoxSection("Duration (minutes)", () => FormatMinutes(timer.TimerDurationSeconds), v => timer.TimerDurationSeconds = ParseMinutes(v)),
    new ToggleSection("Auto-start on Go Live", () => timer.AutoStartOnGoLive, v => timer.AutoStartOnGoLive = v),
    new TextStyleSection(timer.Style),
];
```

**ChatOverlayTypeDescriptor:**
```csharp
return [
    new InfoSection("Displays live chat messages from the connected platform."),
    new TextStyleSection(chat.Style),
];
```

**GroupOverlayTypeDescriptor** (candidates passed in by SceneEditorViewModel via factory, see below):
```csharp
// descriptor.GetInspectorSections not called directly for group — SceneEditorViewModel builds its sections
// OR: descriptor accepts candidates collection at GetInspectorSections time (awkward)
```
> **Note:** `GroupOverlayTypeDescriptor.GetInspectorSections` requires `AvailableGroupCandidates` from `SceneEditorViewModel`. Since descriptors don't know the ViewModel, `SceneEditorViewModel.RefreshInspectorSections()` special-cases Group and appends a `GroupMembershipSection` with the candidates list. Same for Alert: it appends `AlertSubLayerManagerSection` with the commands.

**AlertOverlayTypeDescriptor** (pure content properties only):
```csharp
return [
    new ComboSection("Trigger Type", AlertOverlayContent.AllAlertTypes, () => alert.AlertType, v => alert.AlertType = (StreamAlertType)v!),
    new SliderSection("Duration (sec)", () => alert.DurationSeconds, v => alert.DurationSeconds = (int)v, 1, 30, 1, "{0:0}s"),
    new ComboSection("Entrance", AlertOverlayContent.AllEntranceAnimations, () => alert.EntranceAnimation, v => alert.EntranceAnimation = (AlertEntranceAnimation)v!),
    new ComboSection("Exit", AlertOverlayContent.AllExitAnimations, () => alert.ExitAnimation, v => alert.ExitAnimation = (AlertExitAnimation)v!),
    new ComboSection("Alert Sound", AppModel.Instance.Audios, () => alert.AudioPath, v => alert.AudioPath = v as string),
    new SliderSection("Sound Volume", () => alert.AudioVolumePercent, v => alert.AudioVolumePercent = v, 0, 100, 5, "{0:0}%"),
    new ToggleSection("Loop Sound", () => alert.IsAudioLooping, v => alert.IsAudioLooping = v),
    new ToggleSection("Duck Stream Audio", () => alert.EnableAudioDucking, v => alert.EnableAudioDucking = v),
    new SliderSection("Duck Amount", () => alert.DuckingAmountPercent, v => alert.DuckingAmountPercent = v, 0, 100, 5, "{0:0}%"),
];
// SceneEditorViewModel appends: AlertSubLayerManagerSection, CommandGroupSection(test triggers)
```

**MarqueePluginDescriptor** (plugin update):
```csharp
return [
    new TextBoxSection("Marquee Text", () => m.MarqueeText, v => m.MarqueeText = v ?? ""),
    new ColorSection("Background", () => ParseHex(m.BackgroundColorHex), v => m.BackgroundColorHex = ToHex(v)),
    new ColorSection("Text Color", () => ParseHex(m.TextColorHex), v => m.TextColorHex = ToHex(v)),
    new SliderSection("Font Size", () => m.FontSize, v => m.FontSize = (int)v, 8, 200),
];
```

---

## SceneEditorViewModel Changes

File: `StreamFlow.App/ViewModels/Pages/SceneEditorViewModel.cs`

Add two observable properties and a rebuild method:

```csharp
[ObservableProperty]
private IReadOnlyList<IOverlayPropertySection>? _selectedSlotInspectorSections;

[ObservableProperty]
private IReadOnlyList<IOverlayPropertySection>? _selectedAlertSubLayerInspectorSections;
```

`RefreshInspectorSections()` — called from `OnSelectedSlotChanged` and from a content-change hook:

```csharp
private void RefreshInspectorSections()
{
    SelectedAlertSubLayerInspectorSections = null;

    var content = SelectedSlot?.Content;
    if (content is null) { SelectedSlotInspectorSections = null; return; }

    var descriptor = _overlayRegistry.GetForContent(content);
    var sections = descriptor?.GetInspectorSections(content).ToList() ?? [];

    // Append ViewModel-owned sections that descriptors can't self-provide:
    switch (content)
    {
        case TimerOverlayContent:
            sections.Add(new CommandGroupSection("Controls", [
                new("▶ Start",  StartTimerCommand),
                new("⏸ Pause",  PauseTimerCommand),
                new("↺ Reset",  ResetTimerCommand),
            ]));
            break;

        case AlertOverlayContent alert:
            sections.Add(new AlertSubLayerManagerSection(alert, AddSubLayerToAlertCommand, RemoveSubLayerFromAlertCommand));
            alert.PropertyChanged -= OnAlertSubLayerChanged;
            alert.PropertyChanged += OnAlertSubLayerChanged;
            RefreshAlertSubLayerSections(alert);
            break;

        case GroupOverlayContent:
            sections.Add(new GroupMembershipSection(AvailableGroupCandidates));
            break;
    }

    SelectedSlotInspectorSections = sections;
}

private void OnAlertSubLayerChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(AlertOverlayContent.SelectedSubLayer) && sender is AlertOverlayContent alert)
        RefreshAlertSubLayerSections(alert);
}

private void RefreshAlertSubLayerSections(AlertOverlayContent alert)
{
    var sub = alert.SelectedSubLayer;
    if (sub?.Content is null) { SelectedAlertSubLayerInspectorSections = null; return; }
    var descriptor = _overlayRegistry.GetForContent(sub.Content);
    SelectedAlertSubLayerInspectorSections = descriptor?.GetInspectorSections(sub.Content);
}
```

Also subscribe to `SelectedSlot.PropertyChanged` for `nameof(SourceSlot.Content)` to re-call `RefreshInspectorSections()` when the same slot's overlay type changes.

---

## WPF DataTemplate Infrastructure

Create `StreamFlow.App/Resources/OverlayInspectorSectionTemplates.xaml` (merged into `App.xaml` resources). Contains `DataType`-keyed implicit `DataTemplate`s for every section type — WPF selects them automatically based on the item's runtime type in the `ItemsControl`:

| Section Type | Template renders |
|---|---|
| `SliderSection` | Label + Slider + value `TextBlock`, all bound to section's `Label`/`Value`/`Min`/`Max`/`Format` |
| `TextBoxSection` | Label + `TextBox` (or multiline) bound to `Value` |
| `ToggleSection` | `CheckBox` bound to `Value`, `Content` = `Label` |
| `ColorSection` | Label + color swatch `Border` + "Change" `Button` (opens color dialog, writes back to `Value`) |
| `FilePickerSection` | Label + path `TextBlock` + "Browse…" `Button` (writes to `Value`) |
| `ComboSection` | Label + `ComboBox` (`ItemsSource`=`Options`, `SelectedItem`=`Value`) |
| `InfoSection` | Muted `TextBlock` |
| `SeparatorSection` | Thin `Separator` |
| `TextStyleSection` | The existing `TextStyleEditorTemplate` content, bound to `Style` |
| `ChromaKeySection` | The existing chroma key block, bound to `Target.*` |
| `CommandGroupSection` | Optional `Header` + `WrapPanel` of `Button`s over `Commands` |
| `AlertSubLayerManagerSection` | Sub-layer `ListBox` + "Add Sub-Layer ▾" `Button` menu |
| `GroupMembershipSection` | Candidate `ListBox` with check/uncheck items |

No `DataTemplateSelector` is needed — WPF's `DataType` resolution handles dispatch automatically.

---

## Properties Panel XAML Replacement

Both files receive the same change:

**`StreamFlow.App/Views/Pages/GoLiveView.xaml`**  
**`StreamFlow.App/Views/Pages/ScenesView.xaml`**

Replace the `<StackPanel Margin="12" DataContext="{Binding ViewModel.SceneEditor.SelectedSlot}">` block (currently ~300 lines each) with approximately:

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto">
    <StackPanel Margin="12">
        <!-- Capture source picker (non-overlay slots) -->
        <StackPanel Visibility="{Binding ViewModel.SceneEditor.SelectedSlot.IsOverlay,
                    Converter={StaticResource InverseBoolToVisibility}}">
            <!-- existing ComboBox for SourceId -->
        </StackPanel>

        <!-- Generic property sections -->
        <ItemsControl ItemsSource="{Binding ViewModel.SceneEditor.SelectedSlotInspectorSections}"
                      Margin="0,0,0,8"/>

        <!-- Alert sub-layer quick inspector (shown only when a sub-layer is selected) -->
        <StackPanel Visibility="{Binding ViewModel.SceneEditor.SelectedAlertSubLayerInspectorSections,
                    Converter={StaticResource NullToVisibility}, ConverterParameter=True}">
            <TextBlock Text="{Binding ViewModel.SceneEditor.SelectedSlot.Content.SelectedSubLayer.DisplayName,
                       StringFormat='Selected Element: {0}'}" FontWeight="Bold" FontSize="11" Margin="0,4,0,6"/>
            <ItemsControl ItemsSource="{Binding ViewModel.SceneEditor.SelectedAlertSubLayerInspectorSections}"/>
        </StackPanel>

        <!-- Layout settings — always shown for overlay slots -->
        <StackPanel Visibility="{Binding ViewModel.SceneEditor.SelectedSlot.IsOverlay,
                    Converter={StaticResource BoolToVisibility}}">
            <!-- X/Y/W/H, opacity, rotation, corner radius — unchanged -->
        </StackPanel>

        <!-- Canvas preview section — see below -->
        <ContentControl Content="{Binding ViewModel.SceneEditor.SelectedSlot}"
                        ContentTemplateSelector="{StaticResource OverlayCanvasPreviewSelector}"/>
    </StackPanel>
</ScrollViewer>
```

The duplicated `TextStyleEditorTemplate` declarations in both XAML files are **deleted** — the template moves into `OverlayInspectorSectionTemplates.xaml`.

---

## Canvas Preview Replacement

Repurpose `OverlayInspectorTemplateSelector` → rename to `OverlayCanvasPreviewTemplateSelector` (or add a second set of template properties). Each of the 9 `DataTemplate` slots contains the WYSIWYG preview markup that was previously inside DataTrigger blocks (lines 1512-1574 of GoLiveView.xaml).

The 9 named `DataTemplate` resources move to `OverlayInspectorSectionTemplates.xaml`. A `ContentControl` in both views selects among them:

```xml
<ContentControl Content="{Binding ViewModel.SceneEditor.SelectedSlot}"
                ContentTemplateSelector="{StaticResource OverlayCanvasPreviewSelector}"/>
```

---

## SourceSlot Cleanup

After the XAML migration is verified:

1. Remove the 9 `Is*Overlay` properties from `SourceSlot` (`IsImageOverlay`, `IsTextOverlay`, `IsColorOverlay`, `IsVideoOverlay`, `IsChatOverlay`, `IsBlurOverlay`, `IsTimerOverlay`, `IsAlertOverlay`, `IsGroupOverlay`) and the corresponding re-notifications in `OnContentChanged`.
2. Retain `IsAdvancedOverlay`, `IsStaticOverlay`, `SupportsChromaKey`, `HasLiveThumbnail`, `CanBeAddedToGroup` — these are still used in C# logic and layer-list DataTriggers.
3. Remove or trim C# usages of removed flags in `SceneEditorViewModel` (lines 672–699, slot naming/loading logic) — replace with `content is XOverlayContent` checks inline.

---

## Files to Create / Modify

| File | Action |
|---|---|
| `StreamFlow.Plugin.SDK/Overlays/Sections/IOverlayPropertySection.cs` | **Create** — marker interface + all primitive section types |
| `StreamFlow.Plugin.SDK/IOverlayTypeDescriptor.cs` | **Modify** — add `GetInspectorSections` method |
| `StreamFlow.App/Services/Overlays/Sections/` | **Create** — `TextStyleSection`, `ChromaKeySection`, `CommandGroupSection`, `AlertSubLayerManagerSection`, `GroupMembershipSection` |
| `StreamFlow.App/Services/Overlays/Descriptors/*.cs` (all 9) | **Modify** — implement `GetInspectorSections` |
| `StreamFlow.App/ViewModels/Pages/SceneEditorViewModel.cs` | **Modify** — add `SelectedSlotInspectorSections`, `SelectedAlertSubLayerInspectorSections`, `RefreshInspectorSections()` |
| `StreamFlow.App/Resources/OverlayInspectorSectionTemplates.xaml` | **Create** — all `DataType`-implicit DataTemplates; merge into App.xaml |
| `StreamFlow.App/Services/Overlays/UI/OverlayInspectorTemplateSelector.cs` | **Modify** — repurpose/rename for canvas preview |
| `StreamFlow.App/Views/Pages/GoLiveView.xaml` | **Modify** — replace 300-line block; remove TextStyleEditorTemplate |
| `StreamFlow.App/Views/Pages/ScenesView.xaml` | **Modify** — same as above |
| `StreamFlow.App/ViewModels/Pages/SourceSlot.cs` | **Modify** — remove 9 `Is*Overlay` booleans after migration |
| `plugins/StreamFlow.Plugin.Marquee/MarqueePluginDescriptor.cs` | **Modify** — implement `GetInspectorSections` |

---

## Build Order

1. SDK: `IOverlayPropertySection` types + `GetInspectorSections` on interface  
2. App Sections: composite section types  
3. Descriptors: implement `GetInspectorSections` (all 9 + Marquee)  
4. ViewModel: `RefreshInspectorSections`, new observable props  
5. Resources: `OverlayInspectorSectionTemplates.xaml` with all DataTemplates  
6. Views: GoLiveView.xaml + ScenesView.xaml replacement  
7. Canvas preview: repurpose OverlayInspectorTemplateSelector  
8. Cleanup: remove `Is*Overlay` booleans, verify build  

---

## Verification

1. Open Go Live → select an image overlay → File picker and chroma key sections appear. Change a property → persists to settings.
2. Select a text overlay → text box + text style editor appear.
3. Select a timer overlay → mode/duration/auto-start + text style + Start/Pause/Reset buttons appear. Buttons function.
4. Select an alert overlay → all alert config sections appear. Sub-layer list shows children. Selecting a sub-layer shows that sub-layer's sections below. Test trigger buttons function.
5. Select a group overlay → LockChildren toggle + candidate membership list appear.
6. Install Marquee plugin → select a marquee overlay → its 4 sections appear (using only SDK types, no XAML shipped by plugin).
7. Build without errors. Verify no remaining XAML references to `Is*Overlay`, `IsBlurOverlay`, etc.
