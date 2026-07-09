using System;
using System.Windows;
using System.Windows.Controls.Primitives;

using StreamFlow.App.Models.Canvas;
using StreamFlow.App.ViewModels.Pages.Compose;

namespace StreamFlow.App.Helpers.Behaviors;

public static class CanvasResizeBehavior
{

    public static readonly DependencyProperty HandleProperty = DependencyProperty.RegisterAttached(
        "Handle",
        typeof(CanvasResizeHandle),
        typeof(CanvasResizeBehavior),
        new PropertyMetadata(OnHandleChanged));

    public static readonly DependencyProperty RowSizeProperty = DependencyProperty.RegisterAttached(
        "RowSize",
        typeof(double),
        typeof(CanvasResizeBehavior),
        new PropertyMetadata(16d));

    private static readonly DependencyProperty ResizeStateProperty = DependencyProperty.RegisterAttached(
        "ResizeState",
        typeof(ResizeState),
        typeof(CanvasResizeBehavior),
        new PropertyMetadata(null));

    public static CanvasResizeHandle GetHandle(DependencyObject element) => (CanvasResizeHandle)element.GetValue(HandleProperty);

    public static void SetHandle(DependencyObject element, CanvasResizeHandle value) => element.SetValue(HandleProperty, value);

    public static double GetRowSize(DependencyObject element) => (double)element.GetValue(RowSizeProperty);

    public static void SetRowSize(DependencyObject element, double value) => element.SetValue(RowSizeProperty, value);

    private static ResizeState? GetResizeState(DependencyObject element) => (ResizeState?)element.GetValue(ResizeStateProperty);

    private static void SetResizeState(DependencyObject element, ResizeState? value) => element.SetValue(ResizeStateProperty, value);

    private static void OnHandleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Thumb thumb)
        {
            return;
        }

        if (e.NewValue is CanvasResizeHandle newHandle)
        {
                Attach(thumb);
        }
        else if (e.OldValue is CanvasResizeHandle oldHandle)
        {

                Detach(thumb);
        }
    }

    private static void Attach(Thumb thumb)
    {
        thumb.DragStarted += OnDragStarted;
        thumb.DragDelta += OnDragDelta;
        thumb.DragCompleted += OnDragCompleted;
    }

    private static void Detach(Thumb thumb)
    {
        thumb.DragStarted -= OnDragStarted;
        thumb.DragDelta -= OnDragDelta;
        thumb.DragCompleted -= OnDragCompleted;
        SetResizeState(thumb, null);
    }

    private static void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb thumb)
        {
            return;
        }

        if (thumb.DataContext is not CanvasAudioTypeViewModel audioType)
        {
            return;
        }
        

        var state = new ResizeState
        {
            AudioType = audioType,
            Handle = GetHandle(thumb),
            OriginalRow = (audioType.Y / audioType.MinHeight) + 1,
            OriginalLeft = audioType.X,
            OriginalRight = audioType.X + audioType.Width,
            CurrentRow = (audioType.Y / audioType.MinHeight) + 1,
            CurrentLeft = audioType.X,
            CurrentRight = audioType.X + audioType.Width,
        };

        SetResizeState(thumb, state);
    }

    private static void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb)
        {
            return;
        }

        var state = GetResizeState(thumb);
        if (state?.AudioType is not CanvasAudioTypeViewModel AudioType)
        {
            return;
        }

        var row = state.CurrentRow;
        var left = state.CurrentLeft;
        var right = state.CurrentRight;

        var handle = state.Handle;

        switch (handle)
        {
            case CanvasResizeHandle.Left:
                left += e.HorizontalChange;
                break;

            case CanvasResizeHandle.Right:
                right += e.HorizontalChange;
                break;
        }

        var minWidth = AudioType.MinWidth;
        var minHeight = AudioType.MinHeight;

        if (right - left < minWidth)
        {
            switch (handle)
            {
                case CanvasResizeHandle.Left:
                    left = right - minWidth;
                    break;
                default:
                    right = left + minWidth;
                    break;
            }
        }

        var width = Math.Max(minWidth, right - left);

        var y = (row * minHeight) - minHeight;

        AudioType.Y = y;
        AudioType.X = left;
        AudioType.Width = width;
        AudioType.Height = minHeight;

        state.CurrentRow = row;
        state.CurrentLeft = left;
        state.CurrentRight = left + width;

        e.Handled = true;
    }

    private static void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is not Thumb thumb)
        {
            return;
        }

        var state = GetResizeState(thumb);
        if (state?.AudioType is CanvasAudioTypeViewModel audioType)
        {
            SnapAudioTypeToTimeAndRow(state.CurrentLeft, state.CurrentRow, audioType);
        }

        SetResizeState(thumb, null);
    }

    private static double Snap(double row, double rowSize)
    {
        if (rowSize <= 0)
        {
            return row;
        }

        return (row * rowSize) - rowSize;
    }

    private static void SnapAudioTypeToTimeAndRow(double newTime, double currentRow, CanvasAudioTypeViewModel audioType)
    {

        var top = Snap(currentRow, audioType.Height);

        var left = newTime;
        var right = left + audioType.Width;


        if (Math.Abs(right - left) < double.Epsilon)
        {
            right = left + audioType.MinWidth;
        }

        var width = Math.Max(audioType.MinWidth, right - left);

        audioType.X = left;
        audioType.Y = top;
        audioType.Width = width;
    }

    //        RowSize (MinHeight) = 96
    //        Row = 7
    //        
    //
    //
    //
    //

    private sealed class ResizeState
    {
        public CanvasAudioTypeViewModel? AudioType
        {
            get;
            set;
        }

        public CanvasResizeHandle Handle
        {
            get;
            set;
        }

        public double CurrentRow
        {
            get;
            set;
        }

        public double OriginalRow
        {
            get;
            set;
        }

        public double OriginalLeft
        {
            get;
            set;
        }

        public double OriginalRight
        {
            get;
            set;
        }

        public double CurrentLeft
        {
            get;
            set;
        }

        public double CurrentRight
        {
            get;
            set;
        }
    }
}
