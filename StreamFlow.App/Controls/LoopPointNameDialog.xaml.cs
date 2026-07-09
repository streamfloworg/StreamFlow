using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using AdonisUI.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

using StreamFlow.App.Views.Windows;

namespace StreamFlow.App.Controls;

[ObservableObject]
public partial class LoopPointNameDialog : AdonisWindow
{
    [ObservableProperty]
    private string loopPointName = string.Empty;

    private readonly TaskCompletionSource<(string?, bool)> _tcs = new();

    public LoopPointNameDialog(string existingName, int loopPointIndex)
    {
        InitializeComponent();
        DataContext = this;
        
        // Set default or existing name
        LoopPointName = string.IsNullOrWhiteSpace(existingName) 
            ? $"Loop {loopPointIndex + 1}"
            : existingName;

        Loaded += (_, __) => 
        {
            NameTextBox.SelectAll();
            NameTextBox.Focus();
        };
    }

    public async Task<(string?, bool)> ShowAsync(Window? owner = null)
    {
        if (owner != null)
        {
            this.Owner = owner;
        }
        else if (MainWindow.Current != null)
        {
            this.Owner = MainWindow.Current;
        }

        this.Closed += (s, e) =>
        {
            this.Owner?.Focus();
            _tcs.TrySetResult((null, false));
        };

        // ShowDialog() blocks input to the owner natively (no visual side effects) — this used
        // to be Show() + manually disabling the owner window, which made the owner's controls
        // (the audio ListView in particular) flash to WPF's default disabled-state appearance
        // for as long as the dialog was open.
        this.ShowDialog();
        return await _tcs.Task;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult((null, false));
        this.Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = LoopPointName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Loop {DateTime.Now:HHmmss}";
        }
        _tcs.TrySetResult((name, true));
        this.Close();
    }

    public Task<(string?, bool)> WaitForResultAsync() => _tcs.Task;
}
