using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace StreamFlow.App.Helpers;

public class WindowHelper
{
    static public Window CreateWindow()
    {
        Window newWindow = new Window();
        TrackWindow(newWindow);
        return newWindow;
    }

    static public void TrackWindow(Window window)
    {
        window.Closed += (sender, args) => {
            _activeWindows.Remove(window);
        };
        _activeWindows.Add(window);
    }

    static public Window? GetWindowForElement(UIElement element)
    {
        DependencyObject? parent = element;
        while (parent != null)
        {
            if (parent is Window window)
            {
                return window;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    static public List<Window> ActiveWindows { get { return _activeWindows; } }

    static private List<Window> _activeWindows = new List<Window>();
}
