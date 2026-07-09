using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

using StreamFlow.Core.AudioProperties;

namespace StreamFlow.Core.AudioHandling;

public class AudioTypeConverter : CustomCreationConverter<Audio>
{
    private AudioTypes audioType;

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var jo = JObject.ReadFrom(reader);
        audioType = jo["AudioType"]!.ToObject<AudioTypes>();
        return base.ReadJson(jo.CreateReader(), objectType, existingValue, serializer);
    }

    public override Audio Create(Type objectType)
    {
        return audioType switch
        {
            AudioTypes.AudioTrack => new AudioTrack(),
            AudioTypes.SoundEffect => new SoundEffect(),
            _ => throw new NotImplementedException(),
        };
    }

    public override bool Equals(object? obj)
    {
        return base.Equals(obj);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        base.WriteJson(writer, value, serializer);
    }

    public override bool CanConvert(Type objectType)
    {
        return base.CanConvert(objectType);
    }
}
