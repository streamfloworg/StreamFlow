using StreamFlow.Core.AudioHandling;

namespace StreamFlow.App.Services;

public interface IDialogService
{
    Task InfoAsync(string title, string message);
    Task WarningAsync(string title, string message);
    Task ErrorAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string primaryText = "OK", string secondaryText = "Cancel");
    Task<string> PromptUnsavedChangesAsync(string title, string message);
    Task PropertiesDialog(Audio audio);
}

