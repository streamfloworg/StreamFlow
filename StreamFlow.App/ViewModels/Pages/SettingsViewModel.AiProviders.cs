using Microsoft.Extensions.DependencyInjection;

using StreamFlow.App.Services;
using StreamFlow.App.Services.AI;
using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Settings UI for AI/LLM provider connections — mirrors GoLiveViewModel.Streaming.cs's
/// Connect/Disconnect shape for Twitch/YouTube, but backed by an API key (cloud) or a base URL +
/// Test Connection (local) instead of a real OAuth flow, since none of these providers offer
/// OAuth for API access. See AiProviderRegistryService for where profiles/credentials actually
/// live; this partial only reacts to user-driven UI actions.</summary>
public partial class SettingsViewModel
{
    private readonly IDialogService _aiDialogs = App.Services.GetRequiredService<IDialogService>();

    public AiProviderRegistryService AiProviders { get; } = App.Services.GetRequiredService<AiProviderRegistryService>();

    public static IReadOnlyList<AiProviderCapabilityInfo> AvailableAiProviderKinds => AiProviderCatalog.All;

    /// <summary>Backs the "Add a provider" ComboBox — a row of 7 buttons (one per catalog entry)
    /// didn't fit the settings window on one line, so this replaced it with a single dropdown +
    /// Add button. Defaults to the first catalog entry so Add works immediately without the user
    /// having to pick something first.</summary>
    [ObservableProperty]
    private AiProviderCapabilityInfo _selectedAiProviderKindToAdd = AiProviderCatalog.All[0];

    [RelayCommand]
    private void AddAiProvider()
    {
        var info = SelectedAiProviderKindToAdd;
        var profile = new AiProviderProfile(Guid.NewGuid().ToString("N"), info.DisplayName, info.Kind);
        AiProviders.Profiles.Add(profile);
        AiProviders.SaveToSettings();
    }

    [RelayCommand]
    private void RemoveAiProvider(AiProviderProfile? profile)
    {
        if (profile is not null) AiProviders.RemoveProfile(profile);
    }

    /// <summary>Cloud providers (API key entry) — on success, persists the key and populates
    /// AvailableModels; on failure, shows a warning dialog (mirrors GoLiveViewModel.Streaming.cs's
    /// ConnectTwitchAsync failure path) and leaves the bad key unsaved.</summary>
    [RelayCommand]
    private async Task ConnectAiProviderAsync(AiProviderProfile? profile)
    {
        if (profile is null) return;

        profile.ConnectionStatus = AiConnectionStatus.Testing;
        var result = await TestConnectionCoreAsync(profile);

        if (result.Success)
        {
            profile.ConnectionStatus = AiConnectionStatus.Connected;
            profile.StatusMessage = null;
            profile.AvailableModels.Clear();
            foreach (var model in result.AvailableModels) profile.AvailableModels.Add(model);
            AiProviders.SaveToSettings();
        }
        else
        {
            profile.ConnectionStatus = AiConnectionStatus.Failed;
            profile.StatusMessage = result.ErrorMessage;
            await _aiDialogs.WarningAsync($"Connect {profile.Capabilities.DisplayName}", result.ErrorMessage ?? "Couldn't connect. Check the API key and try again.");
        }
    }

    [RelayCommand]
    private void DisconnectAiProvider(AiProviderProfile? profile)
    {
        if (profile is null) return;

        profile.ApiKey = "";
        profile.ConnectionStatus = AiConnectionStatus.Unknown;
        profile.StatusMessage = null;
        profile.AvailableModels.Clear();
        AiProviders.SaveToSettings();
    }

    /// <summary>Local providers (base URL) — same underlying test, but failures show inline via
    /// StatusMessage rather than a modal dialog: a local server not being up yet is routine while
    /// the user is still starting it, unlike a cloud auth failure.</summary>
    [RelayCommand]
    private async Task TestAiProviderConnectionAsync(AiProviderProfile? profile)
    {
        if (profile is null) return;

        profile.ConnectionStatus = AiConnectionStatus.Testing;
        var result = await TestConnectionCoreAsync(profile);

        profile.ConnectionStatus = result.Success ? AiConnectionStatus.Connected : AiConnectionStatus.Failed;
        profile.StatusMessage = result.Success ? null : result.ErrorMessage;
        profile.AvailableModels.Clear();
        foreach (var model in result.AvailableModels) profile.AvailableModels.Add(model);

        AiProviders.SaveToSettings();
    }

    /// <summary>A provider can support both text and image (OpenAI, Google) — either client is
    /// equally representative of whether the credential/base URL actually works, so this just
    /// picks whichever modality the provider supports (preferring text) rather than testing both.</summary>
    private static async Task<AiConnectionTestResult> TestConnectionCoreAsync(AiProviderProfile profile)
    {
        if (profile.SupportsText)
        {
            var client = AiProviderClientFactory.CreateTextClient(profile, profile.ApiKey);
            if (client is not null) return await client.TestConnectionAsync();
        }
        if (profile.SupportsImage)
        {
            var client = AiProviderClientFactory.CreateImageClient(profile, profile.ApiKey);
            if (client is not null) return await client.TestConnectionAsync();
        }
        return AiConnectionTestResult.Failed("This provider doesn't support any known modality.");
    }
}
