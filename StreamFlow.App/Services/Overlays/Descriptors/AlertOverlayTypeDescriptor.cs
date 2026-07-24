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
            s.GroupChildIds = alert.Children.Select(c => c.SourceId).Where(id => id is not null).ToList()!;
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        if (content is AlertOverlayContent alert)
        {
            return new AlertOverlayContent
            {
                AlertType = alert.AlertType,
                DurationSeconds = alert.DurationSeconds,
                EntranceAnimation = alert.EntranceAnimation,
                ExitAnimation = alert.ExitAnimation,
            };
        }
        return CreateDefault();
    }

    public (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, SourceSlot slot) => null;

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
}
