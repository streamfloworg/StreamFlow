using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

using ProgressBar = System.Windows.Controls.ProgressBar;

namespace StreamFlow.Core.Helpers;

public static class AnimationExtension
{
    private static readonly TimeSpan dur = TimeSpan.FromMilliseconds(30);

    public static void SetValue(this ProgressBar progressBar, double percentage)
    {
        DoubleAnimation animation = new(percentage, dur);
        progressBar.BeginAnimation(RangeBase.ValueProperty, animation);
    }

    public static void SetValue(this Slider slider, double percentage)
    {
        DoubleAnimation animation = new(percentage, dur);
        slider.BeginAnimation(RangeBase.ValueProperty, animation);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="slider"></param>
    /// <param name="value">Value to set slider to</param>
    /// <param name="duration">Duration in milliseconds for animation to complete. int </param>
    public static void SetValue(this Slider slider, double value, int duration)
    {

        DoubleAnimation animation = new(value, TimeSpan.FromMilliseconds(duration));
        slider.BeginAnimation(RangeBase.ValueProperty, animation);
    }

    public static void SetText(this TextBlock textBlock, string text)
    {
        // Only animate if the text is actually changing
        if (textBlock.Text != text)
        {
            // Fade out animation
            //TransitionAnimationProvider.ApplyTransition(textBlock, Transition.FadeIn, 100);
            //textBlock.Text = text;
            DoubleAnimation fadeOut = new(0, dur);
            fadeOut.Completed += (s, e) =>
            {
                textBlock.Text = text; // Update the text when fade-out completes
                // Fade in animation
                DoubleAnimation fadeIn = new(1, dur);
                textBlock.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            };
            textBlock.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}
