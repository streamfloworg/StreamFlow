using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

namespace StreamFlow.Core.AudioProperties;

[JsonObject]
public class AudioTag : INotifyPropertyChanged, IEquatable<AudioTag?>
{
    private string text = string.Empty;

    public string Text
    {
        get => text;
        set
        {
            text = value; NotifyPropertyChanged();
        }
    }

    public AudioTag(string text)
    {
        Text = text;
    }

    public bool Equals(AudioTag? other)
    {
        return other != null && other.Text == Text;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not AudioTag tag)
        {
            return false;
        }

        return Equals(tag);
    }

    public override string ToString()
    {
        return Text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Text);
    }
}

public class SelectableTag : AudioTag
{
    private bool selected;

    public bool Selected
    {
        get => selected;
        set
        {
            selected = value; NotifyPropertyChanged();
        }
    }

    public SelectableTag(string text, bool selected) : base(text)
    {
        Selected = selected;
    }
}
