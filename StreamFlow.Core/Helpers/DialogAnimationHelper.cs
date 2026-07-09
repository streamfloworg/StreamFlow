using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StreamFlow.Core.Helpers;

public static class DialogAnimationHelper
{
    public static async Task SlideInAsync(FrameworkElement target, double fromY = -24, double durationMs = 200)
    {
        if (target == null)
        {
            return;
        }

        EnsureTransform(target);
        target.Opacity = 0;
        var storyboard = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var transAnim = new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ease };
        Storyboard.SetTarget(transAnim, target);
        Storyboard.SetTargetProperty(transAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ease };
        Storyboard.SetTarget(opacityAnim, target);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));

        storyboard.Children.Add(transAnim);
        storyboard.Children.Add(opacityAnim);

        await BeginAsync(storyboard);
    }

    public static async Task SlideOutAsync(FrameworkElement target, double toY = -24, double durationMs = 180)
    {
        if (target == null)
        {
            return;
        }

        EnsureTransform(target);
        var storyboard = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var transAnim = new DoubleAnimation(0, toY, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ease };
        Storyboard.SetTarget(transAnim, target);
        Storyboard.SetTargetProperty(transAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        var opacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ease };
        Storyboard.SetTarget(opacityAnim, target);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));

        storyboard.Children.Add(transAnim);
        storyboard.Children.Add(opacityAnim);

        await BeginAsync(storyboard);
    }

    private static void EnsureTransform(FrameworkElement target)
    {
        if (target.RenderTransform is not TranslateTransform)
        {
            target.RenderTransform = new TranslateTransform();
        }
    }

    private static Task BeginAsync(Storyboard storyboard)
    {
        var tcs = new TaskCompletionSource<bool>();
        void OnComplete(object? s, EventArgs e)
        {
            storyboard.Completed -= OnComplete;
            tcs.TrySetResult(true);
        }
        storyboard.Completed += OnComplete;
        storyboard.Begin();
        return tcs.Task;
    }
}

