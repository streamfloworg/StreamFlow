using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Data;

namespace StreamFlow.Core.Persistence;

public class PersistentData
{
    public List<Category> Categories = [];
    public List<AudioTag> Tags = [];
    public List<Audio> Audios = [];
    public List<Scene> SceneList = [];
    public ApplicationSettings Settings = AppModel.Instance.Settings;
    public WindowOptions WindowOptions = new();
}
