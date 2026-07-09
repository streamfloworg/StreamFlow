using System.Windows;
using System.Windows.Controls;

using CommunityToolkit.Mvvm.ComponentModel;

using SoundFlow.Structs;

using Control = System.Windows.Controls.Control;

namespace StreamFlow.Core.Data.UserOptions;

[ObservableObject]
public abstract partial class UserOption : Control
{
    [ObservableProperty]
    private UserOptionCategory _category;

    [ObservableProperty]
    public string optionName = string.Empty;

    [ObservableProperty]
    public Type? dataType;

    public static readonly DependencyProperty OptionNameProperty =
        DependencyProperty.Register(nameof(OptionName), typeof(string), typeof(UserOption));
}
