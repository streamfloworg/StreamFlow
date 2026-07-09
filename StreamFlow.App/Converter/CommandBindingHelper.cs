using System.Windows.Data;
using System.Windows.Input;

namespace StreamFlow.App.Converter;

// Modified version of https://stackoverflow.com/a/75254081
// by https://stackoverflow.com/users/13349759/eldhasp
// I just added the null checks for Binding and PreviewBinding,
// though it's probably not necessary.

public class CommandBindingHelper : CommandBinding
{
    // Using a DependencyProperty as the backing store for MyProperty.  This enables animation, styling, binding, etc...
    protected static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(CommandBindingHelper),
            new PropertyMetadata(null));

    // Using a DependencyProperty as the backing store for MyProperty.  This enables animation, styling, binding, etc...
    protected static readonly DependencyProperty PreviewCommandProperty =
        DependencyProperty.RegisterAttached(
            "PreviewCommand",
            typeof(ICommand),
            typeof(CommandBindingHelper),
            new PropertyMetadata(null));

    public BindingBase? Binding { get; set; }
    public BindingBase? PreviewBinding { get; set; }

    public CommandBindingHelper()
    {
        Executed += (s, e) => PrivateExecuted(CheckSender(s), e.Parameter, CommandProperty, Binding);
        CanExecute += (s, e) => e.CanExecute = PrivateCanExecute(CheckSender(s), e.Parameter, CommandProperty, Binding);
        PreviewExecuted += (s, e) => PrivateExecuted(CheckSender(s), e.Parameter, PreviewCommandProperty, PreviewBinding);
        PreviewCanExecute += (s, e) => e.CanExecute = PrivateCanExecute(CheckSender(s), e.Parameter, PreviewCommandProperty, PreviewBinding);
    }
    private static void PrivateExecuted(UIElement sender, object parameter, DependencyProperty commandProp, BindingBase commandBinding)
    {
        var command = GetCommand(sender, commandProp, commandBinding);
        if (command is not null && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }
    private static bool PrivateCanExecute(UIElement sender, object parameter, DependencyProperty commandProp, BindingBase commandBinding)
    {
        var command = GetCommand(sender, commandProp, commandBinding);
        return command?.CanExecute(parameter) ?? true;
    }

    private static UIElement CheckSender(object sender)
    {
        if (sender is not UIElement element)
            throw new NotImplementedException("Implemented only for UIElement.");
        return element;
    }
    private static ICommand GetCommand(UIElement sender, DependencyProperty commandProp, BindingBase commandBinding)
    {
        var binding = BindingOperations.GetBindingBase(sender, commandProp);
        if (binding != commandBinding)
        {
            if (commandBinding is null)
            {
                BindingOperations.ClearBinding(sender, commandProp);
            }
            else
            {
                BindingOperations.SetBinding(sender, commandProp, commandBinding);
            }
        }
        return (ICommand)sender.GetValue(CommandProperty);
    }
}
