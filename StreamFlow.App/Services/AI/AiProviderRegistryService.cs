using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI;

/// <summary>Owns the set of configured AI provider profiles (loaded from AppModel.Instance.
/// AiSettings + AiCredentialStore at construction) and hands out typed clients for future feature
/// code to consume. Deliberately doesn't test-connect any provider on construction — unlike
/// Twitch/YouTube's cached-token validation on startup, firing several network calls against paid
/// APIs on every app launch is wasteful and surprising; each profile's ConnectionStatus starts
/// Unknown and is only refreshed by an explicit Connect/Test Connection action from the Settings
/// UI. Plain singleton, no IHostedService — no background work needed yet.</summary>
public sealed class AiProviderRegistryService
{
    private readonly AiCredentialStore _credentialStore;

    public ObservableCollection<AiProviderProfile> Profiles { get; } = [];

    /// <summary>Profile fields that live in AiProviderProfileSettings and so need a re-save
    /// whenever edited in place (a Name/BaseUrl/model-picker edit) — mirrors the equivalent
    /// property-name gates elsewhere in this app (e.g. SceneEditorViewModel.OnSlotPropertyChanged)
    /// rather than saving on every property change indiscriminately (ConnectionStatus/
    /// StatusMessage/ApiKey change far more often and aren't meant to auto-save the same way —
    /// ApiKey specifically is only ever persisted through the explicit Connect/Disconnect flow).</summary>
    private static readonly HashSet<string> PersistedProfileProperties =
    [
        nameof(AiProviderProfile.Name), nameof(AiProviderProfile.BaseUrl),
        nameof(AiProviderProfile.DefaultModelText), nameof(AiProviderProfile.DefaultModelImage),
        nameof(AiProviderProfile.ComfyUiWorkflowTemplatePath), nameof(AiProviderProfile.IsEnabled),
    ];

    public AiProviderRegistryService(AiCredentialStore? credentialStore = null)
    {
        _credentialStore = credentialStore ?? new AiCredentialStore();

        // Subscribed once, here, rather than per-load — Profiles.CollectionChanged already fires
        // for every future Add (from LoadFromSettings, AddAiProvider, etc.), so this alone is
        // enough to keep every profile's PropertyChanged wired for the collection's lifetime.
        Profiles.CollectionChanged += OnProfilesCollectionChanged;
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (AiProviderProfile p in e.OldItems)
                p.PropertyChanged -= OnProfilePropertyChanged;
        if (e.NewItems is not null)
            foreach (AiProviderProfile p in e.NewItems)
                p.PropertyChanged += OnProfilePropertyChanged;
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || !PersistedProfileProperties.Contains(e.PropertyName)) return;

        // Cheap in-memory rebuild now; the actual disk write is already debounced by
        // AppModel.RequestSave (5s default), so an in-place edit (typing a name, picking a
        // model) doesn't hit disk on every keystroke/selection change.
        RebuildSettingsSnapshot();
        AppModel.Instance.RequestSave();
    }

    /// <summary>Populates Profiles from AppModel.Instance.AiSettings — called once from
    /// ApplicationHostService.HandleActivationAsync, the same place PluginManagerService.
    /// LoadPlugins is called, rather than eagerly in the constructor. AppModel.Instance touches
    /// WPF's ObservableCollection/Dispatcher machinery on first access, which only exists once a
    /// real Application/UI thread is running — doing this in the constructor would make this
    /// service (and anything that resolves it, including plain DI-container unit tests with no
    /// Dispatcher) blow up outside that context.</summary>
    public void LoadFromSettings()
    {
        Profiles.Clear();
        var keys = _credentialStore.LoadAll();
        foreach (var saved in AppModel.Instance.AiSettings.Providers)
        {
            var profile = new AiProviderProfile(saved.Id, saved.Name, saved.Kind)
            {
                IsEnabled = saved.IsEnabled,
                BaseUrl = saved.BaseUrl ?? AiProviderCatalog.For(saved.Kind).DefaultBaseUrl ?? "",
                DefaultModelText = saved.DefaultModelText,
                DefaultModelImage = saved.DefaultModelImage,
                ComfyUiWorkflowTemplatePath = saved.ComfyUiWorkflowTemplatePath,
                ApiKey = keys.TryGetValue(saved.Id, out var key) ? key : "",
            };
            Profiles.Add(profile);
        }
    }

    /// <summary>Non-secret fields only — rebuilds AppModel.Instance.AiSettings.Providers from the
    /// current Profiles collection in memory. Doesn't touch AiCredentialStore or call RequestSave
    /// itself; see SaveToSettings for the full (credentials + disk write) version used by
    /// explicit actions (Add/Remove/Connect/Disconnect), and OnProfilePropertyChanged for the
    /// lighter in-place-edit path that only needs this part.</summary>
    private void RebuildSettingsSnapshot()
    {
        AppModel.Instance.AiSettings.Providers = Profiles.Select(p => new AiProviderProfileSettings
        {
            Id = p.Id,
            Name = p.Name,
            Kind = p.Kind,
            IsEnabled = p.IsEnabled,
            BaseUrl = p.IsLocal ? p.BaseUrl : null,
            DefaultModelText = p.DefaultModelText,
            DefaultModelImage = p.DefaultModelImage,
            ComfyUiWorkflowTemplatePath = p.ComfyUiWorkflowTemplatePath,
        }).ToList();
    }

    /// <summary>Full save: non-secret fields (see RebuildSettingsSnapshot) plus each profile's API
    /// key via AiCredentialStore, then a debounced disk write — mirrors GoLiveViewModel.
    /// Streaming.cs's SaveSceneSet shape. Used by explicit actions (Add/Remove/Connect/
    /// Disconnect); in-place edits (Name/BaseUrl/model pickers) go through the lighter
    /// OnProfilePropertyChanged path instead, which skips the credential-store pass since none of
    /// those fields ever touch a profile's API key.</summary>
    public void SaveToSettings()
    {
        RebuildSettingsSnapshot();

        foreach (var profile in Profiles)
        {
            if (!string.IsNullOrEmpty(profile.ApiKey))
                _credentialStore.SetApiKey(profile.Id, profile.ApiKey);
            else
                _credentialStore.RemoveApiKey(profile.Id);
        }

        AppModel.Instance.RequestSave();
    }

    public void RemoveProfile(AiProviderProfile profile)
    {
        Profiles.Remove(profile);
        _credentialStore.RemoveApiKey(profile.Id);
        SaveToSettings();
    }

    public AiProviderProfile? FindProfile(string profileId) =>
        Profiles.FirstOrDefault(p => p.Id == profileId);

    public ITextGenerationClient? GetTextClient(string profileId)
    {
        var profile = FindProfile(profileId);
        return profile is { SupportsText: true } ? AiProviderClientFactory.CreateTextClient(profile, profile.ApiKey) : null;
    }

    public IImageGenerationClient? GetImageClient(string profileId)
    {
        var profile = FindProfile(profileId);
        return profile is { SupportsImage: true } ? AiProviderClientFactory.CreateImageClient(profile, profile.ApiKey) : null;
    }
}
