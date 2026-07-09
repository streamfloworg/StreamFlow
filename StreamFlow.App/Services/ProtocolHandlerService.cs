using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using Microsoft.Extensions.Logging;

using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;
using StreamFlow.Core.Protocol;

namespace StreamFlow.App.Services;

/// <summary>
/// Handles streamflow:// protocol URIs by navigating to songs and applying playback parameters
/// </summary>
public class ProtocolHandlerService
{
    private readonly ILogger<ProtocolHandlerService>? _logger;
    private readonly AudioViewModel? _audioViewModel;
    private readonly Dispatcher _dispatcher;

    public ProtocolHandlerService(AudioViewModel? audioViewModel = null, ILogger<ProtocolHandlerService>? logger = null)
    {
        _audioViewModel = audioViewModel;
        _logger = logger;
        _dispatcher = System.Windows.Application.Current.Dispatcher;
    }

    /// <summary>
    /// Processes a streamflow:// URI and performs the requested action
    /// </summary>
    /// <param name="uriString">The URI string to process</param>
    /// <returns>True if the URI was processed successfully, false otherwise</returns>
    public async Task<bool> HandleUriAsync(string uriString)
    {
        try
        {
            _logger?.LogInformation("Processing URI: {Uri}", uriString);

            if (!StreamFlowUri.TryParse(uriString, out var parsedUri))
            {
                _logger?.LogError("Failed to parse URI: {Error}", parsedUri.ErrorMessage);
                await ShowErrorNotificationAsync($"Invalid URI: {parsedUri.ErrorMessage}");
                return false;
            }

            if (string.IsNullOrEmpty(parsedUri.AudioId))
            {
                _logger?.LogError("Audio ID is missing from URI");
                await ShowErrorNotificationAsync("Audio ID is required");
                return false;
            }

            // Find the audio track by ID
            var audioTrack = FindAudioById(parsedUri.AudioId);
            if (audioTrack == null)
            {
                _logger?.LogWarning("Audio track not found with ID: {AudioId}", parsedUri.AudioId);
                await ShowErrorNotificationAsync($"Audio track not found: {parsedUri.AudioId}");
                return false;
            }

            // Execute on UI thread
            await _dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Load and play the audio track
                    await LoadAndPlayAudioAsync(audioTrack, parsedUri);

                    _logger?.LogInformation("Successfully loaded audio track: {AudioName}", audioTrack.Name);
                    await ShowSuccessNotificationAsync($"Playing: {audioTrack.Name}");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error loading audio track");
                    await ShowErrorNotificationAsync($"Error loading audio: {ex.Message}");
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error handling URI: {Uri}", uriString);
            await ShowErrorNotificationAsync($"Unexpected error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds an audio track by its ID
    /// </summary>
    private Audio? FindAudioById(string audioId)
    {
        return AppModel.Instance.Audios.FirstOrDefault(a => a.Id == audioId);
    }

    /// <summary>
    /// Loads and plays an audio track with optional loop and position parameters
    /// </summary>
    private async Task LoadAndPlayAudioAsync(Audio audio, StreamFlowUri parsedUri)
    {
        if (_audioViewModel == null)
        {
            _logger?.LogWarning("AudioViewModel is not available");
            return;
        }

        // Check if the requested audio is already loaded (playing or paused)
        var isAlreadyLoaded = _audioViewModel.AudioTrack == audio && _audioViewModel.CanStop;

        if (isAlreadyLoaded)
        {
            _logger?.LogInformation("Audio already loaded: {AudioName}, applying parameters only", audio.Name);

            // Audio is already loaded, just apply parameters
            var hasParameters = !string.IsNullOrEmpty(parsedUri.LoopIdentifier) || parsedUri.PositionSeconds.HasValue;

            if (!hasParameters)
            {
                // No parameters provided, just restart playback from the beginning
                _logger?.LogInformation("Restarting playback from beginning");
                TrackPlayer.Seek(TimeSpan.Zero);

                // If paused, resume playback
                if (_audioViewModel.PlaybackState == PlaybackState.Paused)
                {
                    TrackPlayer.Play();
                }
                return;
            }

            // Apply loop point if specified
            if (!string.IsNullOrEmpty(parsedUri.LoopIdentifier) && audio is AudioTrack audioTrack)
            {
                ApplyLoopPoint(audioTrack, parsedUri.LoopIdentifier);
            }

            // Apply position if specified
            if (parsedUri.PositionSeconds.HasValue && TrackPlayer.HasPlayer())
            {
                await Task.Delay(50); // Small delay to ensure loop is applied first

                var position = TimeSpan.FromSeconds(parsedUri.PositionSeconds.Value);
                if (position <= TrackPlayer.Duration)
                {
                    TrackPlayer.Seek(position);
                    _logger?.LogInformation("Seeked to position: {Position}", position);
                }
                else
                {
                    _logger?.LogWarning("Position {Position} exceeds track duration {Duration}", 
                        position, TrackPlayer.Duration);
                }
            }

            // If paused, resume playback
            if (_audioViewModel.PlaybackState == PlaybackState.Paused)
            {
                TrackPlayer.Play();
            }

            _logger?.LogInformation("Applied parameters to already-loaded track: {AudioName}", audio.Name);
            return;
        }

        // Audio is not loaded, proceed with normal loading
        _audioViewModel.SelectedAudio = audio;

        if (audio is AudioTrack audioTrackToLoad)
        {
            try
            {
                _logger?.LogInformation("Executing PlayAudioCommand for: {AudioName}", audioTrackToLoad.Name);

                // Use reflection to call the underlying PlayAudio method directly
                // This bypasses the CanExecute check which may return false when no audio is loaded
                var playAudioMethod = _audioViewModel.GetType().GetMethod("PlayAudio", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (playAudioMethod != null)
                {
                    var playTask = playAudioMethod.Invoke(_audioViewModel, new object?[] { audio }) as Task;
                    if (playTask != null)
                    {
                        await playTask;
                    }
                    else
                    {
                        _logger?.LogError("PlayAudio method did not return a Task");
                        return;
                    }
                }
                else
                {
                    _logger?.LogError("PlayAudio method not found via reflection");
                    return;
                }

                // Wait for the track to load and AudioTrack property to be set
                var waitCount = 0;
                while (_audioViewModel.AudioTrack != audioTrackToLoad && waitCount < 50)
                {
                    await Task.Delay(50);
                    waitCount++;
                }

                if (_audioViewModel.AudioTrack != audioTrackToLoad)
                {
                    _logger?.LogWarning("AudioTrack property was not set after loading");
                    return;
                }

                _logger?.LogInformation("Track loaded successfully: {AudioName}", audioTrackToLoad.Name);

                // Now apply loop point if specified (after track is loaded)
                if (!string.IsNullOrEmpty(parsedUri.LoopIdentifier))
                {
                    ApplyLoopPoint(audioTrackToLoad, parsedUri.LoopIdentifier);
                }

                // Apply position if specified (after playback starts)
                if (parsedUri.PositionSeconds.HasValue && TrackPlayer.HasPlayer())
                {
                    // Wait a bit more for the track to be ready
                    await Task.Delay(100);

                    var position = TimeSpan.FromSeconds(parsedUri.PositionSeconds.Value);
                    if (position <= TrackPlayer.Duration)
                    {
                        TrackPlayer.Seek(position);
                        LoggerService.InfoLog(GetType(), $"Seeked to position: {position}");
                    }
                    else
                    {
                        LoggerService.WarnLog(GetType(), $"Position {position} exceeds track duration {TrackPlayer.Duration}");
                    }
                }

                _logger?.LogInformation("Playback initiated successfully for: {AudioName}", audioTrackToLoad.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during playback initiation");
                throw;
            }
        }
    }

    /// <summary>
    /// Applies a loop point to the current audio track by ID or name
    /// </summary>
    private void ApplyLoopPoint(AudioTrack audioTrack, string loopIdentifier)
    {
        if (_audioViewModel == null || audioTrack.LoopPoints == null || audioTrack.LoopPoints.Count == 0)
        {
            _logger?.LogWarning("No loop points available for audio track");
            return;
        }

        // Try to find loop point by ID first, then by name
        var loopPoint = audioTrack.LoopPoints.FirstOrDefault(lp => lp.Id == loopIdentifier)
                     ?? audioTrack.LoopPoints.FirstOrDefault(lp => lp.Name.Equals(loopIdentifier, StringComparison.OrdinalIgnoreCase));

        if (loopPoint == null)
        {
            _logger?.LogWarning("Loop point not found: {LoopIdentifier}", loopIdentifier);
            return;
        }

        // Find the index of the loop point
        var loopIndex = audioTrack.LoopPoints.IndexOf(loopPoint);
        
        // Apply the loop point to the view model
        _audioViewModel.IsLoopingEnabled = true;
        _audioViewModel.IsLoopEnabled = true;
        _audioViewModel.SelectionStart = loopPoint.StartLoopSample.TotalSeconds;
        _audioViewModel.SelectionEnd = loopPoint.EndLoopSample.TotalSeconds;
        _audioViewModel.CurrentLoopPointIndex = loopIndex;

        _logger?.LogInformation("Applied loop point: {LoopName} (ID: {LoopId})", loopPoint.Name, loopPoint.Id);
    }

    /// <summary>
    /// Shows a success notification to the user
    /// </summary>
    private async Task ShowSuccessNotificationAsync(string message)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            MainWindow.ShowNotification("StreamFlow", message, InfoBarSeverity.Success);
        });
    }

    /// <summary>
    /// Shows an error notification to the user
    /// </summary>
    private async Task ShowErrorNotificationAsync(string message)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            MainWindow.ShowNotification("StreamFlow Error", message, InfoBarSeverity.Error);
        });
    }
}
