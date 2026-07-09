using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamFlow.Core.Data.UserOptions;

public abstract partial class UserBindingOption<T> : UserOption
{
    [ObservableProperty]
    public T _value = default!;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(T), typeof(UserBindingOption<T>), new FrameworkPropertyMetadata(default(T), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
}
