using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Data;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.Core.Persistence;

public class PersistentData
{
    public List<Category> Categories = [];
    public List<AudioTag> Tags = [];
    public List<Audio> Audios = [];
    public List<Scene> SceneList = [];
    public ApplicationSettings Settings = AppModel.Instance.Settings;
    public WindowOptions WindowOptions = new();
    public GoLiveSettings GoLiveSettings { get; set; } = new();
    public AiSettings AiSettings { get; set; } = new();
}
