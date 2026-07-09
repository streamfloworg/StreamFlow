using StreamFlow.Core.Data;

namespace StreamFlow.App.Tests;

public class AppOptionsTests
{
    [Fact]
    public void DefaultAudioTrackVolume_IsClamped_To_One()
    {
        var options = new ApplicationSettings();

        options.DefaultAudioTrackVolume = 2.0;

        Assert.Equal(1.0, options.DefaultAudioTrackVolume, 3);
    }

    [Fact]
    public void SetView_Updates_SelectedView_And_Notifies()
    {
        var options = new ApplicationSettings();
        var notified = false;

        options.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(ApplicationSettings.SelectedView))
            {
                notified = true;
            }
        };

        options.SetView(AudioViewType.GridView);

        Assert.True(notified);
        Assert.Equal(AudioViewType.GridView, options.SelectedView);
    }
}

