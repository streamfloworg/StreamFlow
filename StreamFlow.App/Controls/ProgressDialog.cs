using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AdonisUI.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

using StreamFlow.App.Views.Windows;

namespace StreamFlow.App.Controls;

[ObservableObject]
public partial class ProgressDialog : AdonisWindow
{
    private readonly TaskCompletionSource<bool> _tcs = new();
    private readonly Border _card;

    [ObservableProperty]
    private int _percentage;

    public ProgressDialog(string title, string message)
    {
        this.WindowStyle = WindowStyle.None;
        this.AllowsTransparency = true;
        this.Background = System.Windows.Media.Brushes.Transparent;
        this.ShowInTaskbar = false;
        
        Title = title;
        DataContext = this;

        _card = new Border();
        if (System.Windows.Application.Current.TryFindResource("DialogCard") is Style cardStyle)
        {
            _card.Style = cardStyle;
        }

        _card.MinWidth = 420;
        _card.MaxWidth = 480;

        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new System.Windows.Controls.ProgressBar
        {
            IsIndeterminate = false,
            Width = 300,
            Height = 15,
            Margin = new Thickness(0, 16, 0, 12),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Minimum = 0,
            Maximum = 100
        };
        bar.SetBinding(System.Windows.Controls.ProgressBar.ValueProperty, new System.Windows.Data.Binding(nameof(Percentage)) { Source = this });
        Grid.SetRow(bar, 0);

        var percentText = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            FontWeight = FontWeights.Bold
        };
        var textBinding = new System.Windows.Data.Binding(nameof(Percentage)) 
        { 
            Source = this, 
            StringFormat = "{0}%" 
        };
        percentText.SetBinding(TextBlock.TextProperty, textBinding);
        Grid.SetRow(percentText, 1);

        var text = new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 8, 0, 16),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(text, 2);

        root.Children.Add(bar);
        root.Children.Add(percentText);
        root.Children.Add(text);
        _card.Child = root;
        Content = _card;
    }

    public async Task<bool> ShowAsync(Window? owner = null)
    {
        if (owner != null)
        {
            this.Owner = owner;
        }
        else if (MainWindow.Current != null)
        {
            this.Owner = MainWindow.Current;
        }

        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        this.Closed += (s, e) =>
        {
            this.Owner?.Focus();
            _tcs.TrySetResult(true);
        };

        // ShowDialog() blocks input to the owner natively (no visual side effects) — this used
        // to be Show() + manually disabling the owner window, which made the owner's controls
        // (the audio ListView in particular) flash to WPF's default disabled-state appearance
        // for as long as the dialog was open.
        this.ShowDialog();
        return await _tcs.Task;
    }

    public async Task CloseAsync()
    {
        this.Close();
        await Task.CompletedTask;
    }
}
