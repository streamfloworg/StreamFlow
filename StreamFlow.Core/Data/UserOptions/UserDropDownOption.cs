using System.Collections;
using System.Windows;

namespace StreamFlow.Core.Data.UserOptions;

public partial class UserDropDownOption : UserBindingOption<int>
{
    public IEnumerable List
    {
        get => (IEnumerable)GetValue(ListProperty);
        set => SetValue(ListProperty, value);
    }

    public static readonly DependencyProperty ListProperty =
        DependencyProperty.Register(
            nameof(List),
            typeof(IEnumerable),
            typeof(UserDropDownOption),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
}
