using StreamFlow.App.Services;

namespace StreamFlow.App.ViewModels.Pages;

public partial class StreamingProfile : ObservableObject
{
    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private StreamServiceKind _serviceKind;

    [ObservableProperty]
    private string _serverUrl = "";

    [ObservableProperty]
    private string _streamKey = "";

    [ObservableProperty]
    private uint _bitrateKbps = 6000;

    [ObservableProperty]
    private uint _fps = 30;

    [ObservableProperty]
    private string _encoder = "libx264";

    [ObservableProperty]
    private string? _linkedSceneSetId;

    [ObservableProperty]
    private string? _connectedAccountLabel;

    public Dictionary<string, List<SceneSettings>> SceneSetOverrides { get; set; } = [];

    public bool IsConnected => ConnectedAccountLabel is not null;

    public bool IsNone => Id == "none";
    public bool IsNotNone => Id != "none";

    public bool SupportsOAuth => ServiceKind is StreamServiceKind.Twitch or StreamServiceKind.YouTube;
    public bool KeyRetrievableViaApi => ServiceKind == StreamServiceKind.YouTube;
    public string? KeyHelpText => ServiceKind == StreamServiceKind.Twitch
        ? "Find this in Twitch → Creator Dashboard → Settings → Stream"
        : null;

    partial void OnConnectedAccountLabelChanged(string? value) => OnPropertyChanged(nameof(IsConnected));

    partial void OnServiceKindChanged(StreamServiceKind value)
    {
        OnPropertyChanged(nameof(SupportsOAuth));
        OnPropertyChanged(nameof(KeyRetrievableViaApi));
        OnPropertyChanged(nameof(KeyHelpText));

        // Assign default server URLs for convenience when switching service type
        ServerUrl = value switch
        {
            StreamServiceKind.Twitch => "rtmp://live.twitch.tv/app",
            StreamServiceKind.YouTube => "rtmp://a.rtmp.youtube.com/live2",
            _ => ""
        };

        // A stream key (or "connected as X" label) from whatever service this profile used
        // a moment ago is meaningless — and actively dangerous — for the newly selected one:
        // BuildRtmpUrl would happily pair the new service's URL with the old service's key,
        // producing a "successful" RTMP handshake against the new server that's silently
        // rejected or ignored once the server checks the key. GoLiveViewModel.
        // OnProfilePropertyChanged re-derives the real connection state (and, for YouTube,
        // re-fetches the key) right after this from the actual OAuth session — this is just
        // the synchronous clear so nothing stale is ever shown even for that brief window.
        StreamKey = "";
        ConnectedAccountLabel = null;
    }

    public StreamingProfile(string id, string name)
    {
        Id = id;
        _name = name;
    }

    /// <summary>Twitch-specific: appending "?bandwidthtest=true" to the stream key puts the
    /// stream into Twitch's bandwidth-test mode — ingested and processed normally, but never
    /// shown as live to followers/subscribers. Lets Test Stream verify the whole
    /// capture/encode/publish pipeline against real Twitch infrastructure without going visible.
    /// Harmless to include for other services too; they just ignore an unrecognized query
    /// param, so this isn't gated on ServiceKind.</summary>
    public string BuildRtmpUrl(bool testMode = false)
    {
        if (string.IsNullOrEmpty(StreamKey)) return ServerUrl;
        var key = testMode ? $"{StreamKey}?bandwidthtest=true" : StreamKey;
        return $"{ServerUrl.TrimEnd('/')}/{key}";
    }
}
