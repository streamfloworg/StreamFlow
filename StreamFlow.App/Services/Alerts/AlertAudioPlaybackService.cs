using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace StreamFlow.App.Services.Alerts;

public sealed class AlertAudioPlaybackService : IDisposable
{
    private readonly EventBus _eventBus;
    private readonly ILogger<AlertAudioPlaybackService> _logger;
    private readonly IDisposable _playSubscription;
    private readonly IDisposable _stopSubscription;

    private readonly ConcurrentDictionary<string, ActiveAlertAudio> _activePlayers = new(StringComparer.OrdinalIgnoreCase);

    public AlertAudioPlaybackService(EventBus eventBus, ILogger<AlertAudioPlaybackService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;

        _playSubscription = _eventBus.Subscribe<PlayAudioEvent>(OnPlayAudio);
        _stopSubscription = _eventBus.Subscribe<StopAudioEvent>(OnStopAudio);
    }

    private void OnPlayAudio(PlayAudioEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.AudioPath) || !File.Exists(evt.AudioPath))
        {
            _logger.LogWarning("Alert audio file not found or empty: {Path}", evt.AudioPath);
            return;
        }

        try
        {
            // Stop any existing player for the same file path
            StopPlayerForPath(evt.AudioPath);

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var player = new MediaPlayer();
                    var activeAudio = new ActiveAlertAudio(evt.AudioPath, player, evt.EnableAudioDucking);

                    player.Open(new Uri(evt.AudioPath, UriKind.RelativeOrAbsolute));
                    player.Volume = Math.Clamp(evt.VolumePercent / 100.0, 0.0, 1.0);

                    if (evt.IsLooping)
                    {
                        player.MediaEnded += (s, e) =>
                        {
                            player.Position = TimeSpan.Zero;
                            player.Play();
                        };
                    }
                    else
                    {
                        player.MediaEnded += (s, e) =>
                        {
                            StopPlayerForPath(evt.AudioPath);
                        };
                    }

                    player.MediaFailed += (s, e) =>
                    {
                        _logger.LogError(e.ErrorException, "Alert audio playback failed for {Path}", evt.AudioPath);
                        StopPlayerForPath(evt.AudioPath);
                    };

                    _activePlayers[evt.AudioPath] = activeAudio;
                    player.Play();
                    _logger.LogInformation("Playing alert audio: {Path} (Volume: {Vol}%, Loop: {Loop}, Ducking: {Duck})",
                        evt.AudioPath, evt.VolumePercent, evt.IsLooping, evt.EnableAudioDucking);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize WPF MediaPlayer for alert audio: {Path}", evt.AudioPath);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling PlayAudioEvent for {Path}", evt.AudioPath);
        }
    }

    private void OnStopAudio(StopAudioEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.AudioPath)) return;
        StopPlayerForPath(evt.AudioPath);
    }

    private void StopPlayerForPath(string path)
    {
        if (_activePlayers.TryRemove(path, out var active))
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    active.Player.Stop();
                    active.Player.Close();
                    _logger.LogInformation("Stopped alert audio: {Path}", path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing player for {Path}", path);
                }
            });
        }
    }

    public void Dispose()
    {
        _playSubscription.Dispose();
        _stopSubscription.Dispose();

        foreach (var path in _activePlayers.Keys)
        {
            StopPlayerForPath(path);
        }
    }

    private sealed record ActiveAlertAudio(string Path, MediaPlayer Player, bool IsDuckingEnabled);
}
