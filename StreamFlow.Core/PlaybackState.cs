namespace StreamFlow.Core;

/// <summary>
/// Describes the current state of audio playback.
/// Re-exported from SoundFlow for application layer usage.
/// </summary>
public enum PlaybackState
{
    /// <summary>
    /// The player is stopped.
    /// </summary>
    Stopped = 0,

    /// <summary>
    /// The player is playing.
    /// </summary>
    Playing = 1,

    /// <summary>
    /// The player is paused.
    /// </summary>
    Paused = 2
}

/// <summary>
/// Extension methods for converting between Core and SoundFlow PlaybackState enums.
/// </summary>
public static class PlaybackStateExtensions
{
    /// <summary>
    /// Converts SoundFlow.Enums.PlaybackState to Core PlaybackState.
    /// </summary>
    public static PlaybackState ToCore(this SoundFlow.Enums.PlaybackState soundFlowState)
    {
        return soundFlowState switch
        {
            SoundFlow.Enums.PlaybackState.Stopped => PlaybackState.Stopped,
            SoundFlow.Enums.PlaybackState.Playing => PlaybackState.Playing,
            SoundFlow.Enums.PlaybackState.Paused => PlaybackState.Paused,
            _ => PlaybackState.Stopped
        };
    }

    /// <summary>
    /// Converts Core PlaybackState to SoundFlow.Enums.PlaybackState.
    /// </summary>
    public static SoundFlow.Enums.PlaybackState ToSoundFlow(this PlaybackState coreState)
    {
        return coreState switch
        {
            PlaybackState.Stopped => SoundFlow.Enums.PlaybackState.Stopped,
            PlaybackState.Playing => SoundFlow.Enums.PlaybackState.Playing,
            PlaybackState.Paused => SoundFlow.Enums.PlaybackState.Paused,
            _ => SoundFlow.Enums.PlaybackState.Stopped
        };
    }
}
