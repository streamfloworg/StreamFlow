using SoundFlow.Components;
using SoundFlow.Enums;

namespace StreamFlow.Core.AudioHandling;
public sealed class NullAudio : Audio
{

    public static readonly AudioTrack NullTrack = new()
    {
        Name = "No audio playing",
        FilePath = "",
        Volume = 0,
        Repeat = false,
    };

    public static readonly SoundEffect NullEffect = new()
    {
        Name = "No audio playing",
        FilePath = "",
        Volume = 0,
        Repeat = false,
    };
}
