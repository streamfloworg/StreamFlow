namespace StreamFlow.Core.Contracts;

public interface ITimedPlayback
{
    /// <summary>
    /// Represents the current time in playback of the track.
    /// </summary>
    TimeSpan CurrentPosition
    {
        get;
    }

    /// <summary>
    /// The total time it takes to play back this track.
    /// </summary>
    TimeSpan Duration
    {
        get;
    }
}
