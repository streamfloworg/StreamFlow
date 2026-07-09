using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using StreamFlow.Core.Helpers;

namespace StreamFlow.App.Controls;

/// <summary>
/// A specialized range slider for audio selection with two independent thumbs
/// </summary>
[TemplatePart(Name = "PART_StartThumb", Type = typeof(Thumb))]
[TemplatePart(Name = "PART_EndThumb", Type = typeof(Thumb))]
[TemplatePart(Name = "PART_SelectionRange", Type = typeof(FrameworkElement))]
public class SelectionRangeSlider : System.Windows.Controls.Control
{
    private Thumb? _startThumb;
    private Thumb? _endThumb;
    private FrameworkElement? _selectionRange;
    private bool _isDraggingStart;
    private bool _isDraggingEnd;
    private bool _isCreatingSelection;

    /// <summary>
    /// Event raised when a selection is completed (mouse up after creating/editing)
    /// </summary>
    public event EventHandler? SelectionCompleted;

    static SelectionRangeSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectionRangeSlider),
            new FrameworkPropertyMetadata(typeof(SelectionRangeSlider)));
    }

    #region Dependency Properties

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SelectionRangeSlider),
            new PropertyMetadata(0.0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SelectionRangeSlider),
            new PropertyMetadata(100.0, OnRangeChanged));

    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.Register(nameof(SelectionStart), typeof(double), typeof(SelectionRangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionStartChanged, CoerceSelectionStart));

    public static readonly DependencyProperty SelectionEndProperty =
        DependencyProperty.Register(nameof(SelectionEnd), typeof(double), typeof(SelectionRangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionEndChanged, CoerceSelectionEnd));

    public static readonly DependencyProperty TrackWidthProperty =
        DependencyProperty.Register(nameof(TrackWidth), typeof(double), typeof(SelectionRangeSlider),
            new PropertyMetadata(0.0, OnRangeChanged));

    public static readonly DependencyProperty HasSelectionProperty =
        DependencyProperty.Register(nameof(HasSelection), typeof(bool), typeof(SelectionRangeSlider),
            new PropertyMetadata(false));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double SelectionStart
    {
        get => (double)GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public double SelectionEnd
    {
        get => (double)GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    public double TrackWidth
    {
        get => (double)GetValue(TrackWidthProperty);
        set => SetValue(TrackWidthProperty, value);
    }

    public bool HasSelection
    {
        get => (bool)GetValue(HasSelectionProperty);
        private set => SetValue(HasSelectionProperty, value);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SelectionRangeSlider slider)
        {
            slider.CoerceValue(SelectionStartProperty);
            slider.CoerceValue(SelectionEndProperty);
            slider.UpdateThumbPositions();
        }
    }

    private static void OnSelectionStartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SelectionRangeSlider slider)
        {
            slider.UpdateThumbPositions();
            LoggerService.DebugLog(slider.GetType(), $"SelectionStart changed: {e.NewValue}");
        }
    }

    private static void OnSelectionEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SelectionRangeSlider slider)
        {
            slider.UpdateThumbPositions();
            LoggerService.DebugLog(slider.GetType(), $"SelectionEnd changed: {e.NewValue}");
        }
    }

    private static object CoerceSelectionStart(DependencyObject d, object value)
    {
        if (d is SelectionRangeSlider slider)
        {
            var newValue = (double)value;
            newValue = Math.Max(slider.Minimum, Math.Min(slider.Maximum, newValue));
            
            // Ensure start doesn't exceed end
            if (newValue > slider.SelectionEnd && slider.SelectionEnd > slider.Minimum)
            {
                newValue = slider.SelectionEnd;
            }
            
            return newValue;
        }
        return value;
    }

    private static object CoerceSelectionEnd(DependencyObject d, object value)
    {
        if (d is SelectionRangeSlider slider)
        {
            var newValue = (double)value;
            newValue = Math.Max(slider.Minimum, Math.Min(slider.Maximum, newValue));
            
            // Ensure end doesn't go below start
            if (newValue < slider.SelectionStart)
            {
                newValue = slider.SelectionStart;
            }
            
            return newValue;
        }
        return value;
    }

    #endregion

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Detach old event handlers
        if (_startThumb != null)
        {
            _startThumb.DragStarted -= StartThumb_DragStarted;
            _startThumb.DragDelta -= StartThumb_DragDelta;
            _startThumb.DragCompleted -= StartThumb_DragCompleted;
        }

        if (_endThumb != null)
        {
            _endThumb.DragStarted -= EndThumb_DragStarted;
            _endThumb.DragDelta -= EndThumb_DragDelta;
            _endThumb.DragCompleted -= EndThumb_DragCompleted;
        }

        // Get template parts
        _startThumb = GetTemplateChild("PART_StartThumb") as Thumb;
        _endThumb = GetTemplateChild("PART_EndThumb") as Thumb;
        _selectionRange = GetTemplateChild("PART_SelectionRange") as FrameworkElement;

        LoggerService.DebugLog(typeof(SelectionRangeSlider), 
            $"Template applied - StartThumb: {(_startThumb != null)}, EndThumb: {(_endThumb != null)}, Range: {(_selectionRange != null)}");

        // Attach new event handlers
        if (_startThumb != null)
        {
            _startThumb.DragStarted += StartThumb_DragStarted;
            _startThumb.DragDelta += StartThumb_DragDelta;
            _startThumb.DragCompleted += StartThumb_DragCompleted;
        }

        if (_endThumb != null)
        {
            _endThumb.DragStarted += EndThumb_DragStarted;
            _endThumb.DragDelta += EndThumb_DragDelta;
            _endThumb.DragCompleted += EndThumb_DragCompleted;
        }

        UpdateThumbPositions();
    }

    #region Thumb Event Handlers

    private void StartThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isDraggingStart = true;
        LoggerService.DebugLog(GetType(), "Start thumb drag started");
    }

    private void StartThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_isDraggingStart || TrackWidth <= 0) return;

        var deltaValue = (e.HorizontalChange / TrackWidth) * (Maximum - Minimum);
        var newStart = SelectionStart + deltaValue;

        SelectionStart = Math.Max(Minimum, Math.Min(SelectionEnd, newStart));
    }

    private void StartThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDraggingStart = false;
        LoggerService.DebugLog(GetType(), $"Start thumb drag completed at {SelectionStart:F2}");

        // Raise the SelectionCompleted event
        SelectionCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void EndThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isDraggingEnd = true;
        LoggerService.DebugLog(GetType(), "End thumb drag started");
    }

    private void EndThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_isDraggingEnd || TrackWidth <= 0) return; 

        var deltaValue = (e.HorizontalChange / TrackWidth) * (Maximum - Minimum);
        var newEnd = SelectionEnd + deltaValue;

        SelectionEnd = Math.Max(SelectionStart, Math.Min(Maximum, newEnd));
    }

    private void EndThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDraggingEnd = false;
        LoggerService.DebugLog(GetType(), $"End thumb drag completed at {SelectionEnd:F2}");

        // Raise the SelectionCompleted event
        SelectionCompleted?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (TrackWidth <= 0 || Maximum <= Minimum) return;

        // Get click position
        var clickPoint = e.GetPosition(this);
        var clickValue = Minimum + (clickPoint.X / TrackWidth) * (Maximum - Minimum);

        // Start new selection from click point
        SelectionStart = clickValue;
        SelectionEnd = clickValue;
        _isCreatingSelection = true;

        // Capture mouse to receive events even outside the control
        CaptureMouse();

        LoggerService.DebugLog(typeof(SelectionRangeSlider), $"New selection started at {clickValue:F2}s");

        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_isCreatingSelection || TrackWidth <= 0 || Maximum <= Minimum) return;

        // Get current mouse position
        var mousePoint = e.GetPosition(this);
        var mouseValue = Minimum + (mousePoint.X / TrackWidth) * (Maximum - Minimum);

        // Clamp to valid range
        mouseValue = Math.Max(Minimum, Math.Min(Maximum, mouseValue));

        // Update SelectionEnd to current mouse position
        SelectionEnd = mouseValue;

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_isCreatingSelection)
        {
            _isCreatingSelection = false;
            ReleaseMouseCapture();

            LoggerService.DebugLog(typeof(SelectionRangeSlider), 
                $"Selection completed: {SelectionStart:F2}s - {SelectionEnd:F2}s");

            // Raise the SelectionCompleted event
            SelectionCompleted?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    private void UpdateThumbPositions()
    {
        if (_startThumb == null || _endThumb == null || _selectionRange == null) return;
        if (TrackWidth <= 0 || Maximum <= Minimum) return;

        var range = Maximum - Minimum;

        // Calculate positions
        var startPosition = ((SelectionStart - Minimum) / range) * TrackWidth;
        var endPosition = ((SelectionEnd - Minimum) / range) * TrackWidth;

        // Update thumb positions
        Canvas.SetLeft(_startThumb, startPosition);
        Canvas.SetLeft(_endThumb, endPosition);

        // Update selection range rectangle
        Canvas.SetLeft(_selectionRange, startPosition);
        _selectionRange.Width = Math.Max(0, endPosition - startPosition);

        // Update HasSelection state
        HasSelection = SelectionEnd > SelectionStart;
    }
}
