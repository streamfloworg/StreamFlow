using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AdonisUI.Controls;

using StreamFlow.App.Views.Windows;

namespace StreamFlow.App.Controls;

public class SimpleDialog : AdonisWindow
{
    private readonly TaskCompletionSource<bool> _tcs = new();
    private DispatcherTimer? AutoCloseTimer;

    public SimpleDialog(System.Windows.Controls.UserControl DialogContent, System.Windows.Controls.Button CloseButton)
    {
        WindowStyle = WindowStyle.None;
        Owner = MainWindow.Current;
        ShowInTaskbar = false;
        MaxHeight = 450;
        MinHeight = 450;
        MaxWidth = DialogContent.MinWidth + 30;
        MinWidth = MaxWidth;
        Padding = new Thickness(10);
        
        CloseButton.ClickMode = ClickMode.Press;
        CloseButton.Click += (s, e) =>
        {
            this.Close();
        };
        Content = DialogContent;
    }

    public async Task<bool> ShowAsync(Window? owner = null)
    {
        if (owner != null)
        {
            Owner = owner;
        }
        else if (MainWindow.Current != null)
        {
            Owner = MainWindow.Current;
        }

        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // ShowDialog() already blocks input to the owner natively — the Owner.IsEnabled toggle
        // this used to also do was redundant, and was the actual cause of the owner's controls
        // (the audio ListView in particular) flashing to WPF's default disabled-state appearance
        // for as long as this dialog was open.
        Closed += (s, e) =>
        {
            Owner?.Focus();
            _tcs.TrySetResult(true);
        };

        ShowDialog();
        return await _tcs.Task;
    }

    public void Show(Task? runningTask = null)
    {
        _ = ShowAsync();
        runningTask?.ContinueWith(t =>
            {
                Dispatcher.BeginInvoke(new Action(() => this.Close()));
            });
    }

    public SimpleDialog(string title, string message, int timeout = 5000, bool autoClose = true)
    {
        WindowStyle = WindowStyle.None;
        Owner = MainWindow.Current;
        ShowInTaskbar = false;
        MaxHeight = 450;
        MaxWidth = 660;

        Title = title;
        var _card = new Border();
        if (System.Windows.Application.Current.TryFindResource("DialogCard") is Style cardStyle)
        {
            _card.Style = cardStyle;
        }

        _card.MinWidth = 420;
        _card.MaxWidth = 640;

        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Title/message row
        var titleBlock = new TextBlock 
        { 
            Text = message, 
            Margin = new Thickness(0, 16, 0, 16),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        if (System.Windows.Application.Current.TryFindResource("DialogTitleText") is Style titleStyle)
        {
            titleBlock.Style = titleStyle;
        }
        Grid.SetRow(titleBlock, 0);

        // Progress bar row
        var progBar = new System.Windows.Controls.ProgressBar 
        { 
            IsIndeterminate = true, 
            Width = 200, 
            Height = 10, 
            Margin = new Thickness(0, 8, 0, 16), 
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center 
        };
        Grid.SetRow(progBar, 1);

        root.Children.Add(titleBlock);
        root.Children.Add(progBar);
        _card.Child = root;
        Content = _card;

        if (autoClose)
        {
            AutoCloseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(timeout), DispatcherPriority.Background, CloseThisDialog, Dispatcher);
            AutoCloseTimer.Start();
        }
    }

    public new void Hide()
    {
        if (AutoCloseTimer is not null && AutoCloseTimer.IsEnabled)
        {
            AutoCloseTimer.Stop();
        }
        Close();
    }

    private void CloseThisDialog(object? sender, EventArgs e)
    {
        if (sender is DispatcherTimer timer)
        {
            timer.Stop();
            Close();
        }
    }

    public Task<bool> WaitForResultAsync() => _tcs.Task;
}
