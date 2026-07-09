using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace StreamFlow.Core.AudioProperties;

[DebuggerDisplay("AudioType: {Name}")]
public class AudioType : INotifyPropertyChanged, IEquatable<AudioType?>
{
    public AudioTypes Type { get; set; } = AudioTypes.Unknown;

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public override string ToString() => Type.ToString();

    public override int GetHashCode()
    {
        return HashCode.Combine(Type);
    }

    public bool Equals(AudioType? audioType)
    {
        if (audioType is not AudioType otherType)
        {
            return false;
        }

        return Equals(otherType);
    }

    public override bool Equals(object? obj) => Equals(obj as AudioType);

    public class SelectableAudioType : AudioType
    {
        private bool isSelected = false;
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                isSelected = value; NotifyPropertyChanged();
            }
        }
        public SelectableAudioType(AudioTypes audioType, bool selected = false)
        {
            Type = audioType;
            IsSelected = selected;
        }
    }
}
