using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StreamFlow.App.Controls;

/// <summary>
/// A high-performance WrapPanel that animates children to their new positions
/// when the layout is updated (e.g., due to item filtering, window resizing, or items changes).
/// </summary>
public class FluidWrapPanel : System.Windows.Controls.Panel
{
    private readonly Dictionary<UIElement, System.Windows.Point> _previousPositions = new();

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        double rowWidth = 0;
        double rowHeight = 0;
        double totalWidth = 0;
        double totalHeight = 0;

        foreach (UIElement child in Children)
        {
            child.Measure(availableSize);
            System.Windows.Size childSize = child.DesiredSize;

            if (rowWidth + childSize.Width > availableSize.Width && availableSize.Width > 0)
            {
                totalWidth = Math.Max(totalWidth, rowWidth);
                totalHeight += rowHeight;
                rowWidth = childSize.Width;
                rowHeight = childSize.Height;
            }
            else
            {
                rowWidth += childSize.Width;
                rowHeight = Math.Max(rowHeight, childSize.Height);
            }
        }

        totalWidth = Math.Max(totalWidth, rowWidth);
        totalHeight += rowHeight;

        return new System.Windows.Size(
            double.IsInfinity(availableSize.Width) ? totalWidth : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? totalHeight : totalHeight
        );
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        double rowHeight = 0;
        double x = 0;
        double y = 0;

        List<(UIElement Element, Rect Rect)> arranges = new();

        foreach (UIElement child in Children)
        {
            System.Windows.Size childSize = child.DesiredSize;

            if (x + childSize.Width > finalSize.Width && finalSize.Width > 0)
            {
                y += rowHeight;
                x = 0;
                rowHeight = childSize.Height;
            }
            else
            {
                rowHeight = Math.Max(rowHeight, childSize.Height);
            }

            arranges.Add((child, new Rect(x, y, childSize.Width, childSize.Height)));
            x += childSize.Width;
        }

        foreach (var arrange in arranges)
        {
            UIElement child = arrange.Element;
            Rect newRect = arrange.Rect;

            // Get or create TranslateTransform for fluid sliding
            if (child.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                child.RenderTransform = transform;
            }

            if (_previousPositions.TryGetValue(child, out System.Windows.Point prevPos))
            {
                double deltaX = prevPos.X - newRect.X;
                double deltaY = prevPos.Y - newRect.Y;

                if (Math.Abs(deltaX) > 0.1 || Math.Abs(deltaY) > 0.1)
                {
                    // Interrupt current animations
                    transform.BeginAnimation(TranslateTransform.XProperty, null);
                    transform.BeginAnimation(TranslateTransform.YProperty, null);

                    transform.X = deltaX;
                    transform.Y = deltaY;

                    DoubleAnimation animX = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(300)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    DoubleAnimation animY = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(300)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    transform.BeginAnimation(TranslateTransform.XProperty, animX);
                    transform.BeginAnimation(TranslateTransform.YProperty, animY);
                }
            }

            child.Arrange(newRect);
            _previousPositions[child] = newRect.Location;
        }

        // Clean up dictionary for untracked/removed children
        var currentChildren = new HashSet<UIElement>();
        foreach (UIElement child in Children)
        {
            currentChildren.Add(child);
        }
        var keysToRemove = new List<UIElement>();
        foreach (var key in _previousPositions.Keys)
        {
            if (!currentChildren.Contains(key))
            {
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove)
        {
            _previousPositions.Remove(key);
        }

        return finalSize;
    }
}
