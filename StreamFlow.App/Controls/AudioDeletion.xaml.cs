using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AdonisUI.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

using StreamFlow.App.Views.Windows;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Controls;

[ObservableObject]
public partial class AudioDeletion : AdonisWindow
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    [ObservableProperty]
    private Audio audioToDelete = null!;

    [ObservableProperty]
    private string fileNameExt = string.Empty;

    public AudioDeletion(Audio audioToDelete)
    {
        InitializeComponent();
        AudioToDelete = audioToDelete;
        DataContext = this;
    }

    public async Task<bool> ShowAsync(Window? owner = null)
    {
        if (owner != null)
        {
            this.Owner = owner;
        }
        else if (MainWindow.Current != null)
        {
            this.Owner = MainWindow.Current;
        }

        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        this.Closed += (s, e) =>
        {
            this.Owner?.Focus();
            _tcs.TrySetResult(false);
        };

        // ShowDialog() blocks input to the owner natively (no visual side effects) — this used
        // to be Show() + manually disabling the owner window, which made the owner's controls
        // (the audio ListView in particular) flash to WPF's default disabled-state appearance
        // for as long as the dialog was open.
        this.ShowDialog();
        return await _tcs.Task;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(false);
        this.Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        Delete(AudioToDelete);
        _tcs.TrySetResult(true);
        this.Close();
    }

    public void DeleteWithoutConfirmation()
    {
        Delete(AudioToDelete);
    }

    private static void Delete(Audio audioToDelete)
    {
        var foundAudioToDelete = AppModel.Instance.Audios.FirstOrDefault(x => x.Name == audioToDelete.Name && x.FilePath == audioToDelete.FilePath);
        if (foundAudioToDelete is null)
        {
            return;
        }

        if (foundAudioToDelete.AudioType == AudioTypes.AudioTrack)
        {
            // Remove references in scenes
            foreach (var scene in AppModel.Instance.SceneList)
            {
                if (scene.SceneAudioTrack == foundAudioToDelete)
                {
                    scene.SceneAudioTrack = null;
                }
            }
            AppModel.Instance.Audios.Remove(foundAudioToDelete);
        }

        if (foundAudioToDelete.AudioType == AudioTypes.SoundEffect)
        {
            foreach (var scene in AppModel.Instance.SceneList)
            {
                scene.SceneSoundEffects.Remove((SoundEffect)foundAudioToDelete);
            }
            AppModel.Instance.Audios.Remove((SoundEffect)foundAudioToDelete);
        }

        AppModel.Instance.RequestSave();
        MainWindow.Current?.ViewModel.AVM.AudioListCollectionView?.Refresh();
    }
}
