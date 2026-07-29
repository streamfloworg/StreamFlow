using System.ComponentModel;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays.Descriptors;

public sealed class AlertOverlayTypeDescriptor : IOverlayTypeDescriptor
{
    public string TypeKey => "alert";
    public OverlayKind Kind => OverlayKind.Alert;
    public string DisplayName => "Stream Alert";
    public string IconGlyph => "🔔";
    public OverlaySessionMode SessionMode => OverlaySessionMode.Container;

    public IOverlayContent CreateDefault() => new AlertOverlayContent();

    public IOverlayContent? Deserialize(SlotSettings s)
    {
        return new AlertOverlayContent
        {
            AlertType = s.AlertType,
            DurationSeconds = s.AlertDurationSeconds,
            EntranceAnimation = s.AlertEntranceAnimation,
            ExitAnimation = s.AlertExitAnimation,
            AudioPath = s.AlertAudioPath,
            IsAudioEnabled = s.AlertIsAudioEnabled,
            IsAudioLooping = s.AlertIsAudioLooping,
            AudioVolumePercent = s.AlertAudioVolumePercent,
            TargetAudioChannelId = s.AlertTargetAudioChannelId,
            EnableAudioDucking = s.AlertEnableAudioDucking,
            DuckingAmountPercent = s.AlertDuckingAmountPercent,
        };
    }

    public void Serialize(IOverlayContent content, SlotSettings s)
    {
        if (content is AlertOverlayContent alert)
        {
            s.OverlayKind = OverlayKind.Alert;
            s.AlertType = alert.AlertType;
            s.AlertDurationSeconds = alert.DurationSeconds;
            s.AlertEntranceAnimation = alert.EntranceAnimation;
            s.AlertExitAnimation = alert.ExitAnimation;
            s.AlertAudioPath = alert.AudioPath;
            s.AlertIsAudioEnabled = alert.IsAudioEnabled;
            s.AlertIsAudioLooping = alert.IsAudioLooping;
            s.AlertAudioVolumePercent = alert.AudioVolumePercent;
            s.AlertTargetAudioChannelId = alert.TargetAudioChannelId;
            s.AlertEnableAudioDucking = alert.EnableAudioDucking;
            s.AlertDuckingAmountPercent = alert.DuckingAmountPercent;
            s.GroupChildIds = alert.Children.Select(c => c.SourceId).Where(id => id is not null).ToList()!;
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        if (content is AlertOverlayContent alert)
        {
            var clone = new AlertOverlayContent
            {
                AlertType = alert.AlertType,
                DurationSeconds = alert.DurationSeconds,
                EntranceAnimation = alert.EntranceAnimation,
                ExitAnimation = alert.ExitAnimation,
                AudioPath = alert.AudioPath,
                IsAudioEnabled = alert.IsAudioEnabled,
                IsAudioLooping = alert.IsAudioLooping,
                AudioVolumePercent = alert.AudioVolumePercent,
                TargetAudioChannelId = alert.TargetAudioChannelId,
                EnableAudioDucking = alert.EnableAudioDucking,
                DuckingAmountPercent = alert.DuckingAmountPercent,
            };
            foreach (var child in alert.Children)
                clone.Children.Add(child);
            return clone;
        }
        return CreateDefault();
    }

    public (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, object? slotContext) => null;

    public void HookPropertyChanges(IOverlayContent content, Action<string> onPropertyChanged)
    {
        if (content is INotifyPropertyChanged notifying)
        {
            notifying.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not null)
                    onPropertyChanged(e.PropertyName);
            };
        }
    }

    public IReadOnlyList<StreamFlow.Plugin.SDK.Overlays.Sections.IOverlayPropertySection> GetInspectorSections(IOverlayContent content)
    {
        if (content is not AlertOverlayContent alert) return [];
        return [
            new StreamFlow.App.Services.Overlays.Sections.GroupedSection("Alert Animation & Timing", [
                new StreamFlow.Plugin.SDK.Overlays.Sections.ComboSection("Trigger Type", AlertOverlayContent.AllAlertTypes, () => alert.AlertType, v => alert.AlertType = (StreamAlertType)v!, alert, nameof(alert.AlertType)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.SliderSection("Duration", () => alert.DurationSeconds, v => alert.DurationSeconds = (int)v, 1, 30, 1, "{0:0}s", alert, nameof(alert.DurationSeconds)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.ComboSection("Entrance", AlertOverlayContent.AllEntranceAnimations, () => alert.EntranceAnimation, v => alert.EntranceAnimation = (AlertEntranceAnimation)v!, alert, nameof(alert.EntranceAnimation)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.ComboSection("Exit", AlertOverlayContent.AllExitAnimations, () => alert.ExitAnimation, v => alert.ExitAnimation = (AlertExitAnimation)v!, alert, nameof(alert.ExitAnimation))
            ]),
            new StreamFlow.App.Services.Overlays.Sections.GroupedSection("Alert Audio & Ducking", [
                new StreamFlow.Plugin.SDK.Overlays.Sections.ComboSection("Alert Sound", StreamFlow.Core.Data.AppModel.Instance.Audios,
                    () => (object?)StreamFlow.Core.Data.AppModel.Instance.Audios.FirstOrDefault(a => a.FilePath == alert.AudioPath || a.Name == alert.AudioPath || a.Id == alert.AudioPath) ?? alert.AudioPath,
                    v => alert.AudioPath = (v as StreamFlow.Core.AudioHandling.Audio)?.FilePath ?? (v as StreamFlow.Core.AudioHandling.Audio)?.Name ?? v as string,
                    alert, nameof(alert.AudioPath)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.SliderSection("Sound Volume", () => alert.AudioVolumePercent, v => alert.AudioVolumePercent = v, 0, 100, 5, "{0:0}%", alert, nameof(alert.AudioVolumePercent)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.ToggleSection("Loop Sound", () => alert.IsAudioLooping, v => alert.IsAudioLooping = v, alert, nameof(alert.IsAudioLooping)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.ToggleSection("Duck Stream Audio", () => alert.EnableAudioDucking, v => alert.EnableAudioDucking = v, alert, nameof(alert.EnableAudioDucking)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.SliderSection("Duck Amount", () => alert.DuckingAmountPercent, v => alert.DuckingAmountPercent = v, 0, 100, 5, "{0:0}%", alert, nameof(alert.DuckingAmountPercent))
            ])
        ];
    }
}
