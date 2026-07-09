using System.ComponentModel;

namespace StreamFlow.Core.AudioProperties;
public enum AudioTypes
{
    Unknown = 0,
    [Description("Audio Track")]
    AudioTrack = 1,
    [Description("Sound Effect")]
    SoundEffect = 2,
    Other = 3
}
