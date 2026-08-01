using System.Collections.ObjectModel;

using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.ViewModels.Pages;

public enum AiConnectionStatus { Unknown, Testing, Connected, Failed }

/// <summary>Runtime counterpart of AiProviderProfileSettings — mirrors StreamingProfile's
/// multi-instance named-profile pattern. ApiKey is in-memory only (loaded from/written to
/// AiCredentialStore's DPAPI store, never serialized as part of AiSettings/AiProviderProfileSettings).</summary>
public partial class AiProviderProfile : ObservableObject
{
    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private AiProviderKind _kind;

    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>Local providers only (Ollama/LM Studio/Automatic1111/ComfyUI) — the user's
    /// already-running server. Meaningless for cloud providers, which use a fixed endpoint.</summary>
    [ObservableProperty]
    private string _baseUrl = "";

    /// <summary>In-memory only — never persisted to AiSettings/AiProviderProfileSettings.
    /// See AiCredentialStore for the DPAPI-backed storage this is loaded from/saved to.</summary>
    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string? _defaultModelText;

    [ObservableProperty]
    private string? _defaultModelImage;

    [ObservableProperty]
    private string? _comfyUiWorkflowTemplatePath;

    [ObservableProperty]
    private AiConnectionStatus _connectionStatus = AiConnectionStatus.Unknown;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Populated by a successful Connect/Test Connection call (a models-list request) —
    /// not persisted, since it's cheap to re-fetch and can go stale the moment the user installs/
    /// removes a local model.</summary>
    public ObservableCollection<string> AvailableModels { get; } = [];

    public AiProviderCapabilityInfo Capabilities => AiProviderCatalog.For(Kind);
    public bool IsLocal => Capabilities.Transport == AiProviderTransport.LocalHttp;
    public bool SupportsText => Capabilities.SupportedModalities.Contains(AiModality.Text);
    public bool SupportsImage => Capabilities.SupportedModalities.Contains(AiModality.Image);
    public bool RequiresWorkflowTemplate => Capabilities.RequiresWorkflowTemplate;
    public bool IsConnected => ConnectionStatus == AiConnectionStatus.Connected;

    public AiProviderProfile(string id, string name, AiProviderKind kind)
    {
        Id = id;
        _name = name;
        _kind = kind;
        _baseUrl = Capabilities.DefaultBaseUrl ?? "";
    }

    partial void OnKindChanged(AiProviderKind value)
    {
        OnPropertyChanged(nameof(Capabilities));
        OnPropertyChanged(nameof(IsLocal));
        OnPropertyChanged(nameof(SupportsText));
        OnPropertyChanged(nameof(SupportsImage));
        OnPropertyChanged(nameof(RequiresWorkflowTemplate));

        // A key/connection state from whatever provider this profile used a moment ago is
        // meaningless for the newly selected one — same reasoning as StreamingProfile.
        // OnServiceKindChanged clearing StreamKey/ConnectedAccountLabel on a service switch.
        BaseUrl = Capabilities.DefaultBaseUrl ?? "";
        ApiKey = "";
        ConnectionStatus = AiConnectionStatus.Unknown;
        StatusMessage = null;
        AvailableModels.Clear();
    }

    partial void OnConnectionStatusChanged(AiConnectionStatus value) => OnPropertyChanged(nameof(IsConnected));
}
