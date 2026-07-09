using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using StreamFlow.App.Models.Canvas;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.ViewModels.Pages.Compose;

namespace StreamFlow.App.Helpers.Behaviors;

using Logger = Core.Helpers.LoggerService;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

public static class CanvasDragBehavior
{
    private static ComposeViewModel? ViewModel = App.Services.GetService(typeof(ComposeViewModel)) as ComposeViewModel;
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(CanvasDragBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty RowSizeProperty = DependencyProperty.RegisterAttached(
        "RowSize",
        typeof(double),
        typeof(CanvasDragBehavior),
        new PropertyMetadata(16d));

    public static readonly DependencyProperty ClampToCanvasProperty = DependencyProperty.RegisterAttached(
        "ClampToCanvas",
        typeof(bool),
        typeof(CanvasDragBehavior),
        new PropertyMetadata(false));

    private static readonly DependencyProperty DragStateProperty = DependencyProperty.RegisterAttached(
        "DragState",
        typeof(DragState),
        typeof(CanvasDragBehavior),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static double GetRowSize(DependencyObject element) => (double)element.GetValue(RowSizeProperty);

    public static void SetRowSize(DependencyObject element, double value) => element.SetValue(RowSizeProperty, value);

    public static bool GetClampToCanvas(DependencyObject element) => (bool)element.GetValue(ClampToCanvasProperty);

    public static void SetClampToCanvas(DependencyObject element, bool value) => element.SetValue(ClampToCanvasProperty, value);

    private static DragState? GetDragState(DependencyObject element) => (DragState?)element.GetValue(DragStateProperty);

    private static void SetDragState(DependencyObject element, DragState? state) => element.SetValue(DragStateProperty, state);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        var enable = (bool)e.NewValue;
        if (enable)
        {
            Attach(element);
        }
        else
        {
            Detach(element);
        }
    }

    private static void Attach(FrameworkElement element)
    {
        var state = GetDragState(element);
        if (state == null)
        {
            state = new DragState();
            SetDragState(element, state);
        }

        element.MouseLeftButtonDown += OnMouseLeftButtonDown;
        element.MouseMove += OnMouseMove;
        element.MouseLeftButtonUp += OnMouseLeftButtonUp;
        element.LostMouseCapture += OnLostMouseCapture;
    }

    private static bool HasCollision(this FrameworkElement element, MouseEventArgs e)
    {
        var pt = e.GetPosition(element);
        pt.X += 4;
        pt.Y += 4;
        var canvasParent = FindParentCanvas(element);
        return false;
    }

    public static bool HasCollision(List<UIElement> AllCollidables, FrameworkElement element)
    {

        foreach (UIElement item in AllCollidables)
        {
            var canvasParent = FindParentCanvas(element);
            if (Canvas.GetTop((RichCanvas.RichCanvas)item) + canvasParent?.ActualWidth > 0)
                return true;
        }
        return false;
    }

    private static void Detach(FrameworkElement element)
    {
        element.MouseLeftButtonDown -= OnMouseLeftButtonDown;
        element.MouseMove -= OnMouseMove;
        element.MouseLeftButtonUp -= OnMouseLeftButtonUp;
        element.LostMouseCapture -= OnLostMouseCapture;
        SetDragState(element, null);
    }

    private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement audioElement)
        {
            return;
        }

        var state = GetDragState(audioElement) ?? new DragState();
        SetDragState(audioElement, state);

        state.Canvas ??= FindParentCanvas(audioElement);
        if (state.Canvas == null)
        {
            return;
        }

        if (audioElement.DataContext is not CanvasAudioTypeViewModel catvm)
        {
            return;
        }

        state.Position = catvm;

        var clonedElement = audioElement.Copy();
        ViewModel.Clone = audioElement.DataContext as CanvasAudioTypeViewModel;
        var clonedState = GetDragState(audioElement);
        state.IsDragging = true;
        state.PointerStart = e.GetPosition(state.Canvas);
        state.AudioItemStartX = catvm.X;
        state.AudioItemStartY = catvm.Y;

        //ViewModel.Types[0].Visibility = Visibility.Visible;
        state.Canvas.UpdateLayout();

        audioElement.CaptureMouse();
        audioElement.MouseMove += OnMouseMove;
        audioElement.MouseUp += OnMouseLeftButtonUp;
        audioElement.Cursor = System.Windows.Input.Cursors.SizeAll;
        e.Handled = true;

    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var state = GetDragState(element) ?? new DragState();


        if (state?.IsDragging != true || state.Position is null || !element.IsMouseCaptured)
        {
            return;
        }

        state.Canvas ??= FindParentCanvas(element);
        if (state.Canvas == null)
        {
            return;
        }



        //var state = GetDragState(element);

        var cloneState = ViewModel.Clone;

        var pointer = e.GetPosition(state.Canvas);

        var deltaX = pointer.X - state.PointerStart.X;
        Logger.DebugLog(typeof(CanvasDragBehavior), $"Pointer X: {pointer.X} - State Pointer Start X: {state.PointerStart.X} => Delta X: {deltaX}");

        var deltaY = pointer.Y - state.PointerStart.Y;
        Logger.DebugLog(typeof(CanvasDragBehavior), $"Pointer Y: {pointer.Y} - State Pointer Start X: {state.PointerStart.Y} => Delta Y: {deltaY}");

        var candidateX = state.AudioItemStartX + deltaX;
        Logger.DebugLog(typeof(CanvasDragBehavior), $"Audio Item Start X: {state.AudioItemStartX} + Delta X: {deltaX} => Candidate X: {candidateX}");

        var candidateY = state.AudioItemStartY + deltaY;
        Logger.DebugLog(typeof(CanvasDragBehavior), $"Audio Item Start Y: {state.AudioItemStartY} + Delta Y: {deltaY} => Candidate Y: {candidateY}");

        var rowSize = Math.Max(1d, GetRowSize(element) + 4);
        Logger.DebugLog(typeof(CanvasDragBehavior), $"Row Size: {rowSize}");

        if (element.HasCollision(e))
        {
            e.Handled = true;
            return;
        }

        state.Position.Y = candidateY;
        state.Position.X = candidateX;

        candidateY = Snap(candidateY, rowSize);
        if (GetClampToCanvas(element))
        {
            candidateY = Clamp(candidateY, 0, Math.Max(0, state.Canvas.ActualWidth - element.ActualWidth));
            var row = Math.Round(candidateY / rowSize);
            if (cloneState.Row != row)
            {
                cloneState.Row = row;
                cloneState.Y = candidateY;
            }
            cloneState.X = candidateX;
        }
        e.Handled = true;
    }

    private static void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }
        var state = GetDragState(element);
        if (state?.IsDragging != true || state.Position is null || !element.IsMouseCaptured)
        {
            return;
        }
        var cloneState = ViewModel.Clone;

        state.Row = cloneState.Row;
        state.Position.Y = cloneState.Y;
        state.Position.X = cloneState.X;
        //ViewModel.Types[0].Visibility = Visibility.Hidden;
        state.Canvas.UpdateLayout();
        EndDrag(sender as FrameworkElement);
    }

    private static void OnLostMouseCapture(object? sender, MouseEventArgs e)
    {
        EndDrag(sender as FrameworkElement);
    }

    public static FrameworkElement Copy(this FrameworkElement element)
    {
        var clonedElement = Activator.CreateInstance(element.GetType()) as FrameworkElement;
        element.GetType().GetProperties().ToList().ForEach(prop =>
        {
            if (prop.CanRead && prop.CanWrite)
            {
                try
                {
                    prop.SetValue(clonedElement, prop.GetValue(element));
                }
                catch (Exception ex)
                {
                    Logger.ErrorLog(typeof(CanvasDragBehavior), $"Failed to copy property {prop.Name}: {ex.Message}");
                }
            }
        });
        return clonedElement;
    }

    private static void EndDrag(FrameworkElement? element)
    {
        if (element == null)
        {
            return;
        }

        var state = GetDragState(element);
        if (state != null)
        {
            state.IsDragging = false;
            state.Position = null;
        }

        element.ReleaseMouseCapture();
        element.Cursor = System.Windows.Input.Cursors.Arrow;
    }

    private static RichCanvas.RichCanvasPanel? FindParentCanvas(FrameworkElement element)
    {
        DependencyObject? current = element;

        while (current != null)
        {
            if (current is RichCanvas.RichCanvasPanel canvas)
            {
                return canvas;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static double Snap(double value, double rowSize)
    {
        if (rowSize <= 0)
        {
            return value;
        }

        return Math.Round((value / rowSize) * rowSize, 0, MidpointRounding.ToZero);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        if (maximum < minimum)
        {
            return minimum;
        }

        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private sealed class DragState
    {
        public bool IsDragging
        {
            get;
            set;
        }

        public Point PointerStart
        {
            get;
            set;
        }

        public double AudioItemStartX
        {
            get;
            set;
        }

        public double AudioItemStartY
        {
            get;
            set;
        }

        public RichCanvas.RichCanvasPanel? Canvas
        {
            get;
            set;
        }

        public ICanvasPosition? Position
        {
            get;
            set;
        }

        public double Row
        {
            get;
            set;
        }
    }
}
