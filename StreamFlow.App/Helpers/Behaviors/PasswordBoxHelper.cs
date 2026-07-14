using System.Windows;
using System.Windows.Controls;

namespace StreamFlow.App.Helpers.Behaviors;

/// <summary>
/// PasswordBox.Password is deliberately not a DependencyProperty (so its value can't leak via
/// binding/data-template introspection) — this attached property bridges it to normal MVVM
/// binding. IsUpdating guards against the write-back from the user's own typing re-triggering
/// a Password assignment, which would reset the caret position mid-keystroke.
/// </summary>
public static class PasswordBoxHelper
{
    // FrameworkPropertyMetadataOptions.BindsTwoWayByDefault matters here: a plain PropertyMetadata
    // defaults an unqualified `{Binding StreamKey}` (no explicit Mode=TwoWay) to OneWay, so typing
    // into the box would update the PasswordBox's own native display (unrelated to any binding)
    // but never flow back to the source.
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword", typeof(string), typeof(PasswordBoxHelper),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    // Subscribing to PasswordChanged from OnBoundPasswordChanged alone is not reliable: WPF skips
    // invoking a DependencyProperty's PropertyChangedCallback when the value a binding applies is
    // .Equals() to the property's current effective value — which is exactly the empty-string
    // default here for any freshly created profile. That means for a profile whose StreamKey
    // starts as "", OnBoundPasswordChanged never runs on initial load, so the PasswordChanged
    // handler never gets attached, and everything typed afterward updates only the PasswordBox's
    // own native display with nothing ever flowing back to the ViewModel. Attach is a plain bool
    // toggled once from XAML (false -> true is always a genuine change, unlike ""->""), giving a
    // subscription trigger that doesn't depend on the bound value's content.
    public static readonly DependencyProperty AttachProperty = DependencyProperty.RegisterAttached(
        "Attach", typeof(bool), typeof(PasswordBoxHelper),
        new PropertyMetadata(false, OnAttachChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBoxHelper));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    public static bool GetAttach(DependencyObject d) => (bool)d.GetValue(AttachProperty);
    public static void SetAttach(DependencyObject d, bool value) => d.SetValue(AttachProperty, value);

    private static bool GetIsUpdating(DependencyObject d) => (bool)d.GetValue(IsUpdatingProperty);
    private static void SetIsUpdating(DependencyObject d, bool value) => d.SetValue(IsUpdatingProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        if ((bool)e.OldValue) box.PasswordChanged -= OnPasswordChanged;
        if ((bool)e.NewValue) box.PasswordChanged += OnPasswordChanged;
    }

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        if (GetIsUpdating(box)) return;

        SetIsUpdating(box, true);
        box.Password = e.NewValue as string ?? string.Empty;
        SetIsUpdating(box, false);
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        if (GetIsUpdating(box)) return;

        SetIsUpdating(box, true);
        SetBoundPassword(box, box.Password);
        SetIsUpdating(box, false);
    }
}
