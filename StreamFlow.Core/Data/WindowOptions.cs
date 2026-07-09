using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace StreamFlow.Core.Data;

public class WindowOptions : INotifyPropertyChanged
{
    private double height = 500;
    public double Height
    {
        get => height;
        set
        {
            height = value; NotifyPropertyChanged();
        }
    }

    private double width = 700;
    public double Width
    {
        get => width;
        set
        {
            width = value; NotifyPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
