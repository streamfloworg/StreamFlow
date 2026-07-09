using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace StreamFlow.Core.AudioHandling;

/// <summary>
/// Short audio
/// </summary>
[DebuggerDisplay("(SoundEffect) {Name,np} - Volume: {System.Math.Round(Volume * 100),np}")]
public class SoundEffect : Audio
{
    /// <summary>
    /// Creates a new empty SoundEffect
    /// </summary>
    public SoundEffect()
    {
    }

    /// <summary>
    /// Creates a new SoundEffect
    /// </summary>
    /// <param name="audioFile">Audio File Path</param>
    /// <param name="name">Audio Name</param>
    public SoundEffect(string audioFile, string name) : base(audioFile, name)
    {
        AudioType = AudioProperties.AudioTypes.SoundEffect;
        if (Volume == 0)
        {
            Volume = 25f;
        }
    }

    /// <summary>
    /// Get a default SoundEffect
    /// </summary>

    public static readonly SoundEffect Default = new()
    {
        Name = "",
        FilePath = ""
    };
}
