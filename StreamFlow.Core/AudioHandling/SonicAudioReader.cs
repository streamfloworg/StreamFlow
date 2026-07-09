using SoundFlow.Abstracts;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace StreamFlow.Core.AudioHandling;

internal class SonicAudioReader : SoundPlayerBase

{
    public SonicAudioReader(ISoundDataProvider dataProvider) : base(null, AudioFormat.Cd, dataProvider) { }

    /// <inheritdoc />
    public override string Name { get; set; } = "Module Reader";
}
