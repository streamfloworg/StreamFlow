using System.IO;

using StreamFlow.App.Services.AI;
using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;

namespace StreamFlow.App.Services;

/// <summary>Runs when the user pushes a category/game update to Twitch/YouTube (see
/// GoLiveViewModel.Chat.cs's UpdateStreamInfoAsync) and GoLiveSettings.GenerateCategoryMediaEnabled
/// is on: checks GoLiveSettings.CategoryMediaFolderPath for an existing image matching the
/// category, and — if none is found, or the user wants one anyway — generates one via whichever
/// AI provider AiSettings.DefaultImageProviderId points at. All user-facing status (checking/
/// found/not-found/generating/done/failed) goes through MainWindow.ShowNotification, the app's
/// real (already-wired) Windows toast helper — this class never blocks the caller beyond the two
/// points it explicitly awaits a user decision (IDialogService).
///
/// Image generation only — see the plan doc's Context section for why text/audio/video aren't
/// part of this pass: there's no established concrete meaning for "generate text for a category"
/// yet, and neither audio nor video generation exist in the AI provider integration layer.</summary>
public sealed class CategoryMediaService
{
    private readonly AiProviderRegistryService _aiProviders;
    private readonly IDialogService _dialogs;

    public CategoryMediaService(AiProviderRegistryService aiProviders, IDialogService dialogs)
    {
        _aiProviders = aiProviders;
        _dialogs = dialogs;
    }

    public async Task RunAsync(string category, CancellationToken ct = default)
    {
        var settings = AppModel.Instance.GoLiveSettings;
        if (!settings.GenerateCategoryMediaEnabled || string.IsNullOrWhiteSpace(category)) return;

        var providerId = AppModel.Instance.AiSettings.DefaultImageProviderId;
        var client = providerId is not null ? _aiProviders.GetImageClient(providerId) : null;
        if (client is null) return; // Nothing configured/connected — silent no-op, not an error state.

        var folder = string.IsNullOrEmpty(settings.CategoryMediaFolderPath)
            ? Path.Combine(AppDataPaths.RootFolder, "CategoryMedia")
            : settings.CategoryMediaFolderPath;
        Directory.CreateDirectory(folder);

        MainWindow.ShowNotification("Category Media", $"Checking for existing media for \"{category}\"...");
        var existing = FindExistingMedia(folder, category);

        if (existing is not null)
        {
            if (settings.AutoUseExistingCategoryMedia)
            {
                MainWindow.ShowNotification("Category Media", $"Using existing media for \"{category}\".", InfoBarSeverity.Success);
                return;
            }

            var choice = await _dialogs.PromptExistingCategoryMediaAsync("Category Media", $"Existing media found for \"{category}\".");
            if (choice == "use-existing")
            {
                MainWindow.ShowNotification("Category Media", $"Using existing media for \"{category}\".", InfoBarSeverity.Success);
                return;
            }
            if (choice != "generate-new") return; // "skip" — nothing more to do.
        }
        else
        {
            MainWindow.ShowNotification("Category Media", $"No existing media found for \"{category}\".", InfoBarSeverity.Warning);
            var proceed = await _dialogs.ConfirmAsync("Generate Media", $"Generate AI media for \"{category}\"?", "Generate", "Skip");
            if (!proceed) return;
        }

        await GenerateAsync(client, providerId!, folder, category, ct);
    }

    private async Task GenerateAsync(IImageGenerationClient client, string providerId, string folder, string category, CancellationToken ct)
    {
        MainWindow.ShowNotification("Category Media", $"Generating media for \"{category}\"...");

        var profile = _aiProviders.FindProfile(providerId);
        var request = new ImageGenerationRequest(profile?.DefaultModelImage, $"Stream overlay artwork for \"{category}\", high quality, no text");

        ImageGenerationResult result;
        try
        {
            result = await client.GenerateImageAsync(request, ct);
        }
        catch (Exception ex)
        {
            MainWindow.ShowNotification("Category Media", $"Failed to generate media: {ex.Message}", InfoBarSeverity.Error);
            return;
        }

        if (result.Success && result.Images.Count > 0)
        {
            var path = Path.Combine(folder, $"{SanitizeFileName(category)}.png");
            await File.WriteAllBytesAsync(path, result.Images[0], ct);
            MainWindow.ShowNotification("Category Media", $"Generated media for \"{category}\".", InfoBarSeverity.Success);
        }
        else
        {
            MainWindow.ShowNotification("Category Media", $"Failed to generate media: {result.ErrorMessage}", InfoBarSeverity.Error);
        }
    }

    /// <summary>Public/static/pure so it's unit-testable without touching the real filesystem
    /// beyond the caller-supplied folder — matches an existing image file whose name (without
    /// extension) contains <paramref name="category"/>, case-insensitively. Returns the first
    /// match; which one "wins" when several exist isn't meaningful since any match is treated as
    /// "media for this category already exists."
    ///
    /// <paramref name="imageExtensions"/> defaults to AppModel.Instance.ValidImageExtensions for
    /// production use, but is an explicit parameter (not read from AppModel.Instance directly)
    /// specifically so this stays testable — AppModel.Instance's first access runs LoadData(),
    /// which touches WPF's ObservableCollection/Dispatcher machinery and blows up outside a
    /// running Application (see AiCredentialStore's identical reasoning from the prior phase).</summary>
    public static string? FindExistingMedia(string folder, string category, List<FileExtension>? imageExtensions = null)
    {
        if (!Directory.Exists(folder)) return null;

        var extensions = imageExtensions ?? AppModel.Instance.ValidImageExtensions;
        return Directory.EnumerateFiles(folder)
            .FirstOrDefault(f =>
                FileExtension.EndsWith(extensions, f) &&
                Path.GetFileNameWithoutExtension(f).Contains(category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Strips characters that aren't valid in a Windows filename, so an arbitrary
    /// category/game name (which may contain ":", "/", etc.) can always be used as one.</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }
}
