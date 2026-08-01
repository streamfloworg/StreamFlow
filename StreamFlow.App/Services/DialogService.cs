using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AdonisUI.Controls;
using MessageBox = AdonisUI.Controls.MessageBox;
using MessageBoxButton = AdonisUI.Controls.MessageBoxButton;
using MessageBoxImage = AdonisUI.Controls.MessageBoxImage;
using MessageBoxResult = AdonisUI.Controls.MessageBoxResult;
using MessageBoxModel = AdonisUI.Controls.MessageBoxModel;
using MessageBoxButtons = AdonisUI.Controls.MessageBoxButtons;

using StreamFlow.App.Controls;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services;

public class DialogService : IDialogService
{
    public async Task InfoAsync(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        await Task.CompletedTask;
    }

    public async Task WarningAsync(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        await Task.CompletedTask;
    }

    public async Task ErrorAsync(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        await Task.CompletedTask;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText = "OK", string secondaryText = "Cancel")
    {
        var model = new MessageBoxModel
        {
            Text = message,
            Caption = title,
            Icon = MessageBoxImage.Question,
            Buttons =
            [
                MessageBoxButtons.Custom(primaryText, "primary"),
                MessageBoxButtons.Custom(secondaryText, "secondary")
            ]
        };
        MessageBox.Show(model);
        var res = model.Result == MessageBoxResult.Custom && model.ButtonPressed?.Id?.ToString() == "primary";
        return await Task.FromResult(res);
    }

    public async Task<string> PromptUnsavedChangesAsync(string title, string message)
    {
        var model = new MessageBoxModel
        {
            Text = message,
            Caption = title,
            Icon = MessageBoxImage.Question,
            Buttons =
            [
                MessageBoxButtons.Custom("Save Changes", "save"),
                MessageBoxButtons.Custom("Discard", "discard"),
                MessageBoxButtons.Custom("Cancel", "cancel")
            ]
        };
        MessageBox.Show(model);
        var res = "cancel";
        if (model.Result == MessageBoxResult.Custom && model.ButtonPressed != null)
        {
            res = model.ButtonPressed.Id?.ToString() ?? "cancel";
        }
        return await Task.FromResult(res);
    }

    public async Task<string> PromptExistingCategoryMediaAsync(string title, string message)
    {
        var model = new MessageBoxModel
        {
            Text = message,
            Caption = title,
            Icon = MessageBoxImage.Question,
            Buttons =
            [
                MessageBoxButtons.Custom("Use Existing", "use-existing"),
                MessageBoxButtons.Custom("Generate New", "generate-new"),
                MessageBoxButtons.Custom("Skip", "skip")
            ]
        };
        MessageBox.Show(model);
        var res = "skip";
        if (model.Result == MessageBoxResult.Custom && model.ButtonPressed != null)
        {
            res = model.ButtonPressed.Id?.ToString() ?? "skip";
        }
        return await Task.FromResult(res);
    }

    public async Task PropertiesDialog(Audio audio)
    {
        var propEd = new PropertiesEditor() { Audio = audio };
        var propDlg = new SimpleDialog(propEd, propEd.PropCloseButton)
        {
            Padding = new Thickness(0)
        };
        propDlg.Closed += (s, e) => PropDlg_Closed();
        await propDlg.ShowAsync();
    }

    private void PropDlg_Closed()
    {
        AppModel.Instance.RequestSave();
    }
}
