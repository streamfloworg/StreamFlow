using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AdonisUI.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

using StreamFlow.App.Views.Windows;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;
using StreamFlow.Core.Persistence;

using Path = System.IO.Path;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace StreamFlow.App.Controls;

[ObservableObject]
public partial class FileDragDrop : AdonisWindow
{
    private readonly string droppedFile;
    private readonly IPersistenceDataManager? _persistenceDataManager = App.Services.GetService<IPersistenceDataManager>();
    private readonly TaskCompletionSource<bool> _tcs = new();

    [ObservableProperty]
    private string? directoryPath;

    [ObservableProperty]
    private string? fileName;

    [ObservableProperty]
    private string? audioName;

    [ObservableProperty]
    private string? fileNameExt;

    [ObservableProperty]
    private Scene? importedScene;

    [ObservableProperty]
    private string? importedSceneStoragePath;

    [ObservableProperty]
    private bool isImportBusy = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadataTags))]
    private ImageSource? _importedImageSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadataTags))]
    private string? _artistName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadataTags))]
    private string? _albumName;

    public bool HasMetadataTags => !string.IsNullOrEmpty(ArtistName) || !string.IsNullOrEmpty(AlbumName);

    public FileDragDrop(string filesDropped)
    {
        InitializeComponent();
        DataContext = this;

        droppedFile = filesDropped;

        FileName = Path.GetFileNameWithoutExtension(droppedFile);
        AudioName = Path.GetFileNameWithoutExtension(droppedFile);
        FileNameExt = Path.GetFileName(droppedFile);
        DirectoryPath = Path.GetDirectoryName(droppedFile)!;
        PopulateMetadata(droppedFile);
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
            _tcs.TrySetResult(true);
        };

        // ShowDialog() blocks input to the owner natively (no visual side effects) — this used
        // to be Show() + manually disabling the owner window, which made the owner's controls
        // (the audio ListView in particular) flash to WPF's default disabled-state appearance
        // for as long as the dialog was open.
        this.ShowDialog();
        return await _tcs.Task;
    }

    private void Abort_Click(object sender, RoutedEventArgs e)
    {
        AudioName = "-";
        this.Close();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        // Matched by full, normalized path (not just filename) — two files that happen to share a
        // name in different folders are legitimately different sounds, but the same file added
        // twice (e.g. re-dropping it, or picking it again via Add Audio's multiselect) is very
        // easy to do by accident and just clutters the list with a dead duplicate.
        var candidatePath = Path.GetFullPath(droppedFile);
        var duplicate = AppModel.Instance.Audios.FirstOrDefault(a =>
            string.Equals(Path.GetFullPath(a.FilePath), candidatePath, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            var dlg = App.Services.GetService(typeof(Services.IDialogService)) as Services.IDialogService;
            if (dlg is not null)
            {
                await dlg.WarningAsync("Duplicate Audio",
                    $"'{Path.GetFileName(droppedFile)}' is already in the list as '{duplicate.Name}'.");
            }
            this.Close();
            return;
        }

        if (tglbtn_audioTrack.IsChecked == true)
        {
            AppModel.Instance.Audios.Add(new AudioTrack(droppedFile, AudioName!));
        }
        else if (tglbtn_soundEffect.IsChecked == true)
        {
            AppModel.Instance.Audios.Add(new SoundEffect(droppedFile, AudioName!));
        }
        ProcessDroppedFile();
        AppModel.Instance.RequestSave();
        this.Close();
    }

    private async void ProcessDroppedFile()
    {
        var path = droppedFile;
        if (!FileExtension.EndsWith(AppModel.Instance.ValidAudioExtensions, path) && 
            !FileExtension.EndsWith(AppModel.Instance.ValidModuleExtensions, path) && 
            !FileExtension.EndsWith(AppModel.Instance.ValidScenePackageExtensions, path))
        {
            try
            {
                var dlg = App.Services.GetService(typeof(Services.IDialogService)) as Services.IDialogService;
                if (dlg is not null)
                {
                    await dlg.WarningAsync("Unsupported File", $"Skipped '{Path.GetFileName(path)}' — unsupported type.");
                }
            }
            catch { }
        }

        if (FileExtension.EndsWith(AppModel.Instance.ValidScenePackageExtensions, path))
        {
            grd_sceneImport.Visibility = Visibility.Visible;
            grd_audio.Visibility = Visibility.Collapsed;
            grd_convert.Visibility = Visibility.Collapsed;

            ImportedScene = await _persistenceDataManager!.PeekScene(path);
        }
        else
        {
            FileName = Path.GetFileNameWithoutExtension(path);
            AudioName = Path.GetFileNameWithoutExtension(path);
            FileNameExt = Path.GetFileName(path);
            DirectoryPath = Path.GetDirectoryName(path)!;

            ImportedImageSource = null;
            ArtistName = null;
            AlbumName = null;
            PopulateMetadata(path);

            grd_audio.Visibility = Visibility.Visible;
            grd_sceneImport.Visibility = Visibility.Collapsed;
            grd_convert.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulateMetadata(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            var metadata = AudioEngine.GetAudioMetadata(path);
            if (metadata != null)
            {
                if (metadata.Tags != null)
                {
                    if (!string.IsNullOrWhiteSpace(metadata.Tags.Title))
                    {
                        AudioName = metadata.Tags.Title;
                    }

                    if (metadata.Tags.AlbumArt != null && metadata.Tags.AlbumArt.Length > 0)
                    {
                        try
                        {
                            var bmp = new BitmapImage();
                            using (var mem = new MemoryStream(metadata.Tags.AlbumArt))
                            {
                                bmp.BeginInit();
                                bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.StreamSource = mem;
                                bmp.EndInit();
                            }
                            bmp.Freeze();
                            ImportedImageSource = bmp;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(metadata.Tags.Artist))
                    {
                        ArtistName = metadata.Tags.Artist;
                    }

                    if (!string.IsNullOrWhiteSpace(metadata.Tags.Album))
                    {
                        AlbumName = metadata.Tags.Album;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading metadata during import: {ex.Message}");
        }
    }

    private void SoundEffect_Clicked(object sender, RoutedEventArgs e)
    {
        if (tglbtn_soundEffect == null || tglbtn_audioTrack == null)
        {
            return;
        }

        tglbtn_audioTrack.IsChecked = !tglbtn_soundEffect.IsChecked;
    }

    private void AudioTrack_Clicked(object sender, RoutedEventArgs e)
    {
        if (tglbtn_soundEffect == null || tglbtn_audioTrack == null)
        {
            return;
        }

        tglbtn_soundEffect.IsChecked = !tglbtn_audioTrack.IsChecked;
    }

    private void btn_browseSceneStoragePath_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog();
        var result = dialog.ShowDialog();

        if (result == System.Windows.Forms.DialogResult.OK)
        {
            ImportedSceneStoragePath = dialog.SelectedPath;
        }
    }
}
