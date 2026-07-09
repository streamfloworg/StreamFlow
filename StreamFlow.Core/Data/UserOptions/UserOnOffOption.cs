using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamFlow.Core.Data.UserOptions;

public partial class UserOnOffOption : UserBindingOption<bool>
{
    [ObservableProperty]
    private bool invert;
}
