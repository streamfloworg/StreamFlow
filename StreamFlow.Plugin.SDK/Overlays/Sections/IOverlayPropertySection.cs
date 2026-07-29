using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamFlow.Plugin.SDK.Overlays.Sections;

/// <summary>
/// Marker interface for overlay property sections used in the modular inspector model.
/// </summary>
public interface IOverlayPropertySection { }

/// <summary>
/// A numeric slider inspector section.
/// </summary>
public sealed partial class SliderSection : ObservableObject, IOverlayPropertySection
{
    public string Label { get; }
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public string? Format { get; }

    private readonly Func<double> _get;
    private readonly Action<double> _set;

    public double Value
    {
        get => _get();
        set
        {
            if (Math.Abs(_get() - value) > 1e-6)
            {
                _set(value);
                OnPropertyChanged();
            }
        }
    }

    public SliderSection(string label, Func<double> get, Action<double> set, double min, double max, double step = 0, string? format = null, INotifyPropertyChanged? source = null, string? propName = null)
    {
        Label = label;
        _get = get;
        _set = set;
        Min = min;
        Max = max;
        Step = step;
        Format = format;

        if (source != null && propName != null)
        {
            source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == propName)
                    OnPropertyChanged(nameof(Value));
            };
        }
    }
}

/// <summary>
/// A text box inspector section.
/// </summary>
public sealed partial class TextBoxSection : ObservableObject, IOverlayPropertySection
{
    public string Label { get; }
    public bool IsMultiLine { get; }

    private readonly Func<string?> _get;
    private readonly Action<string?> _set;

    public string? Value
    {
        get => _get();
        set
        {
            if (_get() != value)
            {
                _set(value);
                OnPropertyChanged();
            }
        }
    }

    public TextBoxSection(string label, Func<string?> get, Action<string?> set, bool isMultiLine = false, INotifyPropertyChanged? source = null, string? propName = null)
    {
        Label = label;
        _get = get;
        _set = set;
        IsMultiLine = isMultiLine;

        if (source != null && propName != null)
        {
            source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == propName)
                    OnPropertyChanged(nameof(Value));
            };
        }
    }
}

/// <summary>
/// A toggle/checkbox inspector section.
/// </summary>
public sealed partial class ToggleSection : ObservableObject, IOverlayPropertySection
{
    public string Label { get; }

    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    public bool Value
    {
        get => _get();
        set
        {
            if (_get() != value)
            {
                _set(value);
                OnPropertyChanged();
            }
        }
    }

    public ToggleSection(string label, Func<bool> get, Action<bool> set, INotifyPropertyChanged? source = null, string? propName = null)
    {
        Label = label;
        _get = get;
        _set = set;

        if (source != null && propName != null)
        {
            source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == propName)
                    OnPropertyChanged(nameof(Value));
            };
        }
    }
}

/// <summary>
/// A color picker inspector section.
/// </summary>
public sealed partial class ColorSection : ObservableObject, IOverlayPropertySection
{
    public string Label { get; }

    private readonly Func<System.Windows.Media.Color> _get;
    private readonly Action<System.Windows.Media.Color> _set;

    public System.Windows.Media.Color Value
    {
        get => _get();
        set
        {
            if (_get() != value)
            {
                _set(value);
                OnPropertyChanged();
            }
        }
    }

    public ColorSection(string label, Func<System.Windows.Media.Color> get, Action<System.Windows.Media.Color> set, INotifyPropertyChanged? source = null, string? propName = null)
    {
        Label = label;
        _get = get;
        _set = set;

        if (source != null && propName != null)
        {
            source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == propName)
                    OnPropertyChanged(nameof(Value));
            };
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void PickColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        var current = Value;
        dialog.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            Value = System.Windows.Media.Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
        }
    }
}

/// <summary>
/// A file picker inspector section.
/// </summary>
public sealed partial class FilePickerSection : ObservableObject, IOverlayPropertySection
{
    public string Label { get; }
    public string Filter { get; }

    private readonly Func<string?> _get;
    private readonly Action<string?> _set;

    public string? Value
    {
        get => _get();
        set
        {
            if (_get() != value)
            {
                _set(value);
                OnPropertyChanged();
            }
        }
    }

    public FilePickerSection(string label, Func<string?> get, Action<string?> set, string filter = "All files|*.*", INotifyPropertyChanged? source = null, string? propName = null)
    {
        Label = label;
        _get = get;
        _set = set;
        Filter = filter;

        if (source != null && propName != null)
        {
            source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == propName)
                    OnPropertyChanged(nameof(Value));
            };
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Filter
        };
        if (!string.IsNullOrWhiteSpace(Value) && System.IO.File.Exists(Value))
        {
            dialog.FileName = Value;
        }
        if (dialog.ShowDialog() == true)
        {
            Value = dialog.FileName;
        }
    }
}

/// <summary>
/// A drop-down combo box inspector section.
/// </summary>
public sealed partial class ComboSection : ObservableObject, IOverlayPropertySection
{
    public string Label { get; }
    public IEnumerable Options { get; }

    private readonly Func<object?> _get;
    private readonly Action<object?> _set;

    public object? Value
    {
        get => _get();
        set
        {
            if (!Equals(_get(), value))
            {
                _set(value);
                OnPropertyChanged();
            }
        }
    }

    public ComboSection(string label, IEnumerable options, Func<object?> get, Action<object?> set, INotifyPropertyChanged? source = null, string? propName = null)
    {
        Label = label;
        Options = options;
        _get = get;
        _set = set;

        if (source != null && propName != null)
        {
            source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == propName)
                    OnPropertyChanged(nameof(Value));
            };
        }
    }
}

/// <summary>
/// A read-only informational label section.
/// </summary>
public sealed class InfoSection : IOverlayPropertySection
{
    public string Text { get; }
    public InfoSection(string text) => Text = text;
}

/// <summary>
/// A visual separator line section.
/// </summary>
public sealed class SeparatorSection : IOverlayPropertySection { }
