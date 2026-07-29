using System.Threading.Tasks;
using StreamFlow.App.Services;
using StreamFlow.App.Services.Overlays.Descriptors;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;
using Xunit;

namespace StreamFlow.Tests;

public class AlertAudioEventTests
{
    [Fact]
    public void EventBus_PublishesAndSubscribes_PlayAudioEventAndStopAudioEvent()
    {
        var bus = new EventBus();
        PlayAudioEvent? receivedPlay = null;
        StopAudioEvent? receivedStop = null;

        using var subPlay = bus.Subscribe<PlayAudioEvent>(e => receivedPlay = e);
        using var subStop = bus.Subscribe<StopAudioEvent>(e => receivedStop = e);

        var playEvent = new PlayAudioEvent("C:\\audio\\alert.mp3", IsLooping: true, VolumePercent: 80, TargetChannelId: "ch1", EnableAudioDucking: true, DuckingAmountPercent: 40);
        bus.Publish(playEvent);

        Assert.NotNull(receivedPlay);
        Assert.Equal("C:\\audio\\alert.mp3", receivedPlay.AudioPath);
        Assert.True(receivedPlay.IsLooping);
        Assert.Equal(80, receivedPlay.VolumePercent);
        Assert.Equal("ch1", receivedPlay.TargetChannelId);
        Assert.True(receivedPlay.EnableAudioDucking);
        Assert.Equal(40, receivedPlay.DuckingAmountPercent);

        var stopEvent = new StopAudioEvent("C:\\audio\\alert.mp3");
        bus.Publish(stopEvent);

        Assert.NotNull(receivedStop);
        Assert.Equal("C:\\audio\\alert.mp3", receivedStop.AudioPath);
    }

    [Fact]
    public void AlertOverlayTypeDescriptor_SerializesAndDeserializes_AudioAndDuckingProperties()
    {
        var descriptor = new AlertOverlayTypeDescriptor();
        var alert = new AlertOverlayContent
        {
            AudioPath = "C:\\sounds\\notification.wav",
            IsAudioEnabled = true,
            IsAudioLooping = true,
            AudioVolumePercent = 75,
            TargetAudioChannelId = "channel_2",
            EnableAudioDucking = true,
            DuckingAmountPercent = 60
        };

        var settings = new SlotSettings();
        descriptor.Serialize(alert, settings);

        Assert.Equal("C:\\sounds\\notification.wav", settings.AlertAudioPath);
        Assert.True(settings.AlertIsAudioEnabled);
        Assert.True(settings.AlertIsAudioLooping);
        Assert.Equal(75, settings.AlertAudioVolumePercent);
        Assert.Equal("channel_2", settings.AlertTargetAudioChannelId);
        Assert.True(settings.AlertEnableAudioDucking);
        Assert.Equal(60, settings.AlertDuckingAmountPercent);

        var restored = (AlertOverlayContent)descriptor.Deserialize(settings)!;

        Assert.Equal("C:\\sounds\\notification.wav", restored.AudioPath);
        Assert.True(restored.IsAudioEnabled);
        Assert.True(restored.IsAudioLooping);
        Assert.Equal(75, restored.AudioVolumePercent);
        Assert.Equal("channel_2", restored.TargetAudioChannelId);
        Assert.True(restored.EnableAudioDucking);
        Assert.Equal(60, restored.DuckingAmountPercent);
    }

    [Fact]
    public async Task TriggerAsync_PublishesPlayAudioEventAndStopAudioEvent()
    {
        var bus = new EventBus();
        PlayAudioEvent? playEvt = null;
        StopAudioEvent? stopEvt = null;

        bus.Subscribe<PlayAudioEvent>(e => playEvt = e);
        bus.Subscribe<StopAudioEvent>(e => stopEvt = e);

        var alert = new AlertOverlayContent
        {
            DurationSeconds = 1,
            AudioPath = "test.mp3",
            IsAudioEnabled = true,
            IsAudioLooping = false,
            AudioVolumePercent = 90,
            EnableAudioDucking = true,
            DuckingAmountPercent = 50
        };

        var triggerContext = new AlertTriggerContext(StreamAlertType.TwitchFollower, "User followed!");

        await alert.TriggerAsync(triggerContext, _ => Task.CompletedTask, bus);

        Assert.NotNull(playEvt);
        Assert.Equal("test.mp3", playEvt.AudioPath);
        Assert.Equal(90, playEvt.VolumePercent);
        Assert.True(playEvt.EnableAudioDucking);

        Assert.NotNull(stopEvt);
        Assert.Equal("test.mp3", stopEvt.AudioPath);
    }

    [Fact]
    public void TextHorizontalAlignmentConverter_SupportsJustify()
    {
        var converter = new StreamFlow.App.Converter.TextHorizontalAlignmentToTextAlignmentConverter();
        var converted = converter.Convert(TextHorizontalAlignment.Justify, typeof(System.Windows.TextAlignment), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(System.Windows.TextAlignment.Justify, converted);

        var back = converter.ConvertBack(System.Windows.TextAlignment.Justify, typeof(TextHorizontalAlignment), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(TextHorizontalAlignment.Justify, back);
    }

    [Fact]
    public void SourceSlot_TogglingIsAspectLocked_UpdatesAspectRatioToCurrentDimensions()
    {
        var slot = new SourceSlot(isPrimary: false, x: 10, y: 10, w: 40, h: 10)
        {
            CanvasWidth = 640,
            CanvasHeight = 360,
            IsAspectLocked = false
        };

        // Current width pixel = 40% of 640 = 256. Height pixel = 10% of 360 = 36. Ratio = 256 / 36 = 7.111...
        slot.IsAspectLocked = true;

        Assert.NotNull(slot.AspectRatio);
        Assert.Equal(256.0 / 36.0, slot.AspectRatio.Value, 4);
    }

    [Fact]
    public void AlertOverlayContent_SubLayersNotExposedViaSourceSlotChildren()
    {
        var alertContent = new AlertOverlayContent();
        var alertSlot = new SourceSlot(isPrimary: false, x: 0, y: 0, w: 50, h: 50, isOverlay: true, content: alertContent);

        var subContent = new TextOverlayContent { OverlayText = "Test Sublayer" };
        var subSlot = new SourceSlot(isPrimary: false, x: 0, y: 0, w: 20, h: 20, isOverlay: true, content: subContent);

        alertContent.Children.Add(subSlot);

        Assert.Null(alertSlot.Children);
        Assert.Single(alertContent.SubLayers);
        Assert.Same(subSlot, alertContent.SubLayers[0]);
    }
}
