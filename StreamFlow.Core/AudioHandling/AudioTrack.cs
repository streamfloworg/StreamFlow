using System.Diagnostics;
using System.Text.Json.Serialization;

using SoundFlow.Structs;

namespace StreamFlow.Core.AudioHandling;

/// <summary>
/// Longer audio with repeat and fade out
/// </summary>
[DebuggerDisplay("(AudioTrack) {Name,np} - Volume: {System.Math.Round(Volume * 100),np}")]
public class AudioTrack : Audio
{

    private AudioTrack? nextAudioTrack;

    /// <summary>
    /// Next audiotrack to be played after the current finished playing
    /// </summary>
    public AudioTrack? NextAudioTrack
    {
        get => nextAudioTrack;
        set
        {
            nextAudioTrack = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public AudioFormat AudioFormat { get; set; } = AudioFormat.DvdHq;

    /// <summary>
    /// Creates a new empty AudioTrack
    /// </summary>
    public AudioTrack()
    {
    }

    /// <summary>
    /// Creates a new AudioTrack
    /// </summary>
    /// <param name="audioFile">Audio File Path</param>
    /// <param name="name">Audio Name</param>
    public AudioTrack(string audioFile, string name, AudioFormat? audioFormat = null) : base(audioFile, name)
    {
        AudioFormat = audioFormat ?? AudioFormat.DvdHq;
        AudioType = AudioProperties.AudioTypes.AudioTrack;
        if (Volume == 0)
        {
            Volume = 25;
        }
    }

    /// <summary>
    /// Get a default AudioTrack
    /// </summary>
    public static readonly AudioTrack Default = new()
    {
        Name = "",
        FilePath = ""
    };

    public override string ToString()
    {
        return Name;
    }
}
