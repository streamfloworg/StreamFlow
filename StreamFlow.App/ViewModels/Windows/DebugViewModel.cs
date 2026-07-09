using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

// iNKORE usings removed

using StreamFlow.App.ViewModels.Pages;

using StreamFlow.App.Views.Windows;
using StreamFlow.Core.Data;

using WinRT;

namespace StreamFlow.App.ViewModels.Windows;

public partial class DebugViewModel : ViewModel
{
    internal MainWindowViewModel? MWVM { get; }
    internal AudioViewModel? AVM { get; }
    internal SettingsViewModel? SVM { get; }
    internal ComposeViewModel? CVM { get; }
    internal MainWindow? MW { get; }

    [ObservableProperty]
    private ViewModel? _currentViewModel;

    public ObservableCollection<PropertyInfoWrapper> Items { get; set; } = [];

    public CollectionViewSource ItemsViewSource { get; set; } = new CollectionViewSource();

    private DispatcherTimer? UpdateTimer { get; set; }

    /// <summary>Tracks whichever ViewModel DebugWindow_ViewModelDataChanged is currently
    /// subscribed to, so OnCurrentViewModelChanged can unsubscribe from the *previous* one
    /// before attaching to the new one. Without this, every page navigation left a stale
    /// handler on the old (DI-singleton, so permanently alive) page ViewModel — an
    /// unbounded-over-session leak, since DebugViewModel is itself a singleton that's never
    /// recreated once the Debug Window is opened once.</summary>
    private ViewModel? _subscribedViewModel;

    /// <summary>Whether DebugWindow is actually visible — see OnWindowVisibilityChanged. The
    /// window is Hide()/Show()'d rather than closed (MainWindowViewModel.OpenDebugWindow), so
    /// without this the 500ms refresh timer would keep ticking indefinitely while hidden.</summary>
    private bool _isWindowVisible = true;

    public string CurrentPageName { get; private set; } = "Shut the fuck up";

    public DebugViewModel()
    {
        MWVM = App.Services.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
        AVM = App.Services.GetService(typeof(AudioViewModel)) as AudioViewModel;
        SVM = App.Services.GetService(typeof(SettingsViewModel)) as SettingsViewModel;
        CVM = App.Services.GetService(typeof(ComposeViewModel)) as ComposeViewModel;
        MW = MainWindow.Current;
        if (MW is not null)
        {
            CurrentPageName = MW.CurrentPage ?? "";
            MW.PropertyChanged += Current_MainWindow_PropertyChanged;
            CurrentViewModel = ((Page)MW.ContentFrame.Content).DataContext as ViewModel ?? null;
        }
    }

    private void Current_MainWindow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MW.CurrentPage))
        {
            CurrentViewModel = (MW?.CurrentPage) switch
            {
                "Audio" => AVM,
                "Settings" => SVM,
                "Compose" => CVM,
                _ => MWVM,
            };
            CurrentPageName = MW.CurrentPage;
            OnPropertyChanged(nameof(CurrentPageName));
        }
    }

    private void UpdateItem(PropertyInfo property) => UpdateItem("", property);
    private void UpdateItem(string PropertyName) => UpdateItem(PropertyName, null);

    private void UpdateItem(string PropertyName = "", PropertyInfo? property = null)
    {
        if (!string.IsNullOrEmpty(PropertyName) && property == null)
        {
            if (Items.Where(x => x.PropertyInfo?.Name.Equals(PropertyName, StringComparison.Ordinal) == true).Any())
            {
                // Update the corresponding item in the Items collection
                var item = Items.FirstOrDefault(x => x.PropertyInfo?.Name == PropertyName);
                if (item != null)
                {
                    item.Value = CurrentViewModel.GetType().GetProperty(PropertyName)?.GetValue(CurrentViewModel);

                    if (IsCountableEnumerable(item.PropertyInfo.PropertyType))
                    {
                        item.Value = item.PropertyInfo.GetValue(CurrentViewModel).As<IEnumerable<object>>()?.Count() ?? 0;
                    }
                    else if (item.PropertyInfo.PropertyType.IsArray)
                    {
                        item.Value = (item.PropertyInfo.GetValue(CurrentViewModel).As<Array>())?.Length ?? 0;
                    }
                    else
                    {
                        item.Value = item.PropertyInfo.GetValue(CurrentViewModel) ?? "";
                    }
                }
            }
        }
        else
        {
            if (property != null)
            {
                var desc = (TypeDescriptor.GetProperties(CurrentViewModel)[property.Name]?.Attributes[typeof(DescriptionAttribute)] as DescriptionAttribute)?.Description ?? "";
                object propValue = 0;
                if (!property.PropertyType.Name.Contains("Command"))
                {
                    if (IsCountableEnumerable(property.PropertyType))
                    {
                        propValue = (property.GetValue(CurrentViewModel).As<IEnumerable<object>>())?.Count() ?? 0;
                    }
                    else if (property.PropertyType.IsArray)
                    {
                        propValue = (property.GetValue(CurrentViewModel).As<Array>())?.Length ?? 0;
                    }
                    else
                    {
                        propValue = property.GetValue(CurrentViewModel) ?? "";
                    }
                    Items.Add(new PropertyInfoWrapper(property, propValue, desc));
                }
            }
        }
    }



    /// <summary>True for a countable, non-string enumerable (so collection-typed properties
    /// show a count in the grid instead of a raw/unhelpful ToString() dump).</summary>
    private static bool IsCountableEnumerable(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    partial void OnCurrentViewModelChanged(ViewModel? value)
    {
        // Unsubscribe from whichever ViewModel the *previous* value was subscribed to — every
        // page ViewModel here is a DI singleton, so leaving this attached on navigation away
        // means it never gets garbage collected or detached, and re-navigating back re-adds a
        // second handler on top rather than replacing it. Over a long session of switching
        // pages, this silently multiplies UpdateItem calls per property change.
        if (_subscribedViewModel is not null)
            _subscribedViewModel.PropertyChanged -= DebugWindow_ViewModelDataChanged;
        _subscribedViewModel = null;

        UpdateTimer?.Stop();
        Items.Clear();
        if (CurrentViewModel is not null)
        {
            foreach (var property in CurrentViewModel.GetType().GetProperties() ?? Array.Empty<PropertyInfo>())
            {
                UpdateItem(property);
            }
            if (Items.Count > 0)
            {
                UpdateTimer ??= new(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (s, e) => CollectionViewSource.GetDefaultView(ItemsViewSource.Source).Refresh(), MainWindow.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                CurrentViewModel!.PropertyChanged += DebugWindow_ViewModelDataChanged;
                _subscribedViewModel = CurrentViewModel;
                if (_isWindowVisible) UpdateTimer.Start();
            }
        }
        ItemsViewSource.Source ??= Items;
    }

    /// <summary>Called from DebugWindow's IsVisibleChanged — the window is Hide()/Show()'d
    /// rather than closed (see MainWindowViewModel.OpenDebugWindow), and this DebugViewModel is
    /// itself a DI singleton that outlives any single Hide/Show cycle, so without this the 500ms
    /// refresh timer would keep ticking (and repainting the grid) indefinitely while hidden.</summary>
    public void OnWindowVisibilityChanged(bool isVisible)
    {
        _isWindowVisible = isVisible;
        if (isVisible)
        {
            if (UpdateTimer is not null && Items.Count > 0) UpdateTimer.Start();
        }
        else
        {
            UpdateTimer?.Stop();
        }
    }

    private void DebugWindow_ViewModelDataChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == null || CurrentViewModel == null) { return; }
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => { UpdateItem(e.PropertyName); }));
    }

    //public void TreeViewItemCollapsed(object sender, RoutedEventArgs e)
    //{
    //    if (sender is TreeViewItem treeitem)
    //    {
    //        if (Items.Any(x => (bool)x.PropertyInfo?.Name.Equals(treeitem.Header.ToString())))
    //        {
    //            var item = Items.FirstOrDefault(x => x.PropertyInfo?.Name == treeitem.Header.ToString());
    //            item.ExpansionState = false;
    //        }
    //    }
    //}

    //public void TreeViewItemExpanded(object sender, RoutedEventArgs e)
    //{
    //    if (sender is TreeViewItem treeitem)
    //    {
    //        if (Items.Any(x => (bool)x.PropertyInfo?.Name.Equals(treeitem.Header.ToString())))
    //        {
    //            var item = Items.FirstOrDefault(x => x.PropertyInfo?.Name == treeitem.Header.ToString());
    //            item.ExpansionState = true;
    //        }
    //    }
    //}
}
