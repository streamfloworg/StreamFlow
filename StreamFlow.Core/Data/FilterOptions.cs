using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Sorting;

using static StreamFlow.Core.AudioProperties.AudioType;

namespace StreamFlow.Core.Data;

public class FilterOptions : INotifyPropertyChanged, ICloneable
{
    private string searchTerm = "";

    /// <summary>
    /// String used by the searchResultAudioList to filter the audio results
    /// </summary>
    public string SearchTerm
    {
        get => searchTerm;
        set
        {
            searchTerm = value; NotifyPropertyChanged();
        }
    }

    private ListSortDirection sortDirection;

    public ListSortDirection SortDirection
    {
        get => sortDirection;
        set
        {
            sortDirection = value; NotifyPropertyChanged();
        }
    }


    private bool includeAudioTracks = true;

    public bool IncludeAudioTracks
    {
        get => includeAudioTracks;
        set
        {
            includeAudioTracks = value; NotifyPropertyChanged();
        }
    }

    private SortType sortType;

    public SortType SortType
    {
        get => sortType;
        set
        {
            sortType = value; NotifyPropertyChanged();
        }
    }

    private bool includeSoundEffects = true;

    public bool IncludeSoundEffects
    {
        get => includeSoundEffects;
        set
        {
            includeSoundEffects = value; NotifyPropertyChanged();
        }
    }

    private ObservableCollection<SelectableTag> selectedTags = [];

    public ObservableCollection<SelectableTag> SelectedTags
    {
        get => selectedTags;
        set
        {
            selectedTags = value; NotifyPropertyChanged();
        }
    }

    public void UpdateTags()
    {
        var updatedSelectedTags = new ObservableCollection<SelectableTag>();

        foreach (var tag in AppModel.Instance.Tags)
        {
            var isSelected = false;
            foreach (var selectedTag in selectedTags)
            {
                if (tag.Text == selectedTag.Text)
                {
                    isSelected = selectedTag.Selected;
                    break;
                }
            }

            updatedSelectedTags.Add(new SelectableTag(tag.Text, isSelected));
        }

        SelectedTags = new ObservableCollection<SelectableTag>(updatedSelectedTags.OrderBy(x => x.Text).ToList());
    }


    public event PropertyChangedEventHandler ?PropertyChanged;
    public void NotifyPropertyChanged([CallerMemberName] string ?name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public object Clone()
    {
        return MemberwiseClone();
    }
}
