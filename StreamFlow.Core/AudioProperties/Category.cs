
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Media;

using Newtonsoft.Json;

using Color = System.Windows.Media.Color;

namespace StreamFlow.Core.AudioProperties;

[DebuggerDisplay("Name: {Name} - Color: {Color}")]
[JsonObject]
public class Category : INotifyPropertyChanged, IEquatable<Category?>
{
    private static Color DefaultColor = Colors.DarkGray;
    public static Category Default => new("None") { color = DefaultColor };

    private string name = string.Empty;
    public string Name
    {
        get => name;
        set
        {
            name = value;
            NotifyPropertyChanged();
        }
    }

    private Color color = DefaultColor;
    public Color Color
    {
        get => color;
        set
        {
            if (IsDefault())
            {
                return;
            }

            color = value;
            NotifyPropertyChanged();
        }
    }

    public Category(string name)
    {
        Name = name;
    }

    public bool IsDefault()
    {
        return Equals(Default);
    }

    public bool Equals(Category? other)
    {
        return other != null && other.Name == Name;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Category cat)
        {
            return false;
        }

        return Equals(cat);
    }

    public override string ToString()
    {
        return Name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name);
    }
}
