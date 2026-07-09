#nullable enable
using System;
using System.ComponentModel;

using Newtonsoft.Json;

using StreamFlow.Core.AudioProperties;

using SoundFlow.Enums;
namespace StreamFlow.Core.Contracts;

/// <summary>
/// Represents the characteristics and behavior of a track that is being visualized and (potentially) manipulated by the visualization Control.
/// </summary>
public interface IAudio
{
    /// <summary>
    /// Human-friendly name of the track. Could be the track title from the metadata.
    /// </summary>
    string Name
    {
    get; set; 
    }

    /// <summary>
    /// An array of starting / ending "loop points", positions in time where the track should start and finish playback,
    /// expressed as percentages (0-1)
    /// </summary>
    List<LoopPoint> LoopPoints { get; set; }

}