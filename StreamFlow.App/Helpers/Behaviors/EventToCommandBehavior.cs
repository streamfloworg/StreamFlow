//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.Windows.Input;

//namespace StreamFlow.App.Helpers.Behaviors;

//public class EventToCommandBehavior : Behavior<FrameworkElement>
//{
//    public static readonly DependencyProperty RoutedEventProperty = DependencyProperty.Register(
//        nameof(RoutedEvent),
//        typeof(RoutedEvent),
//        typeof(EventToCommandBehavior),
//        new PropertyMetadata(null));

//    public RoutedEvent RoutedEvent
//    {
//        get => (RoutedEvent)GetValue(RoutedEventProperty);
//        set => SetValue(RoutedEventProperty, value);
//    }

//    public static readonly DependencyProperty WithCommandProperty = DependencyProperty.Register(
//        nameof(WithCommand),
//        typeof(ICommand),
//        typeof(EventToCommandBehavior),
//        new PropertyMetadata(null));

//    public ICommand WithCommand
//    {
//        get => (ICommand)GetValue(WithCommandProperty);
//        set => SetValue(WithCommandProperty, value);
//    }

//    readonly RoutedEventHandler _handler;

//    public EventToCommandBehavior()
//    {
//        _handler = (s, e) =>
//        {
//            var args = e.OriginalSource;

//            if (WithCommand.CanExecute(args))
//            {
//                WithCommand.Execute(args);
//            }
//        };
//    }

//    protected override void OnAttached()
//    {
//        base.OnAttached();
//        AssociatedObject.AddHandler(RoutedEvent, _handler);
//    }

//    protected override void OnDetaching()
//    {
//        base.OnDetaching();
//        AssociatedObject.RemoveHandler(RoutedEvent, _handler);
//    }
//}
