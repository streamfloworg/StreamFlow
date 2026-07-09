using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamFlow.Core.Data.UserOptions;

public partial class UserActionOption : UserOption
{
    [ObservableProperty]
    private string actionText = string.Empty;

    public ICommand ActionCommand
    {
        get => (ICommand)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(UserActionOption), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
}
