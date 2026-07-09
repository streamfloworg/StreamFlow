
using System.Collections;
using System.Globalization;
using System.Windows.Data;

using Binding = System.Windows.Data.Binding;

namespace StreamFlow.App.Converter;
internal sealed class EnumerableHasAnyConverter : IValueConverter
{
    /// <summary>
    /// If true, the result is inverted (e.g., empty -> true).
    /// </summary>
    public bool Invert
    {
        get; set;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Optional: minimum count, defaults to 1
        var min = 1;
        if (parameter is string s && int.TryParse(s, out var parsed) && parsed > 0)
        {
            min = parsed;
        }

        var hasItems = HasAtLeast(value, min);
        if (Invert)
        {
            hasItems = !hasItems;
        }

        // If target expects Visibility, return Visible/Collapsed
        if (targetType == typeof(Visibility))
        {
            return hasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        // Otherwise return bool
        return hasItems;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static bool HasAtLeast(object value, int min)
    {
        if (min <= 0)
        {
            return true;
        }

        if (value is null)
        {
            return false;
        }

        // Strings: Length check
        if (value is string str)
        {
            return str.Length >= min;
        }

        // ICollection (fast path)
        if (value is ICollection coll)
        {
            return coll.Count >= min;
        }

        // ICollectionView (fast path for 1; otherwise enumerate)
        if (value is System.ComponentModel.ICollectionView view)
        {
            if (min == 1)
            {
                return !view.IsEmpty;
            }

            var c = 0;
            foreach (var _ in view)
            {
                if (++c >= min)
                {
                    return true;
                }
            }

            return false;
        }

        // Fallback: enumerate up to 'min' items
        if (value is IEnumerable en)
        {
            var c = 0;
            var e = en.GetEnumerator();
            try
            {
                while (e.MoveNext())
                {
                    if (++c >= min)
                    {
                        return true;
                    }
                }
            }
            finally
            {
                (e as IDisposable)?.Dispose();
            }
        }

        return false;
    }
}
