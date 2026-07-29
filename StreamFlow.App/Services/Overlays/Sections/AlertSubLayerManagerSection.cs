using System.Windows.Input;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;
using StreamFlow.Plugin.SDK.Overlays.Sections;

namespace StreamFlow.App.Services.Overlays.Sections;

public sealed class AlertSubLayerManagerSection : IOverlayPropertySection
{
    public AlertOverlayContent AlertContent { get; }
    public ICommand AddSubLayerCommand { get; }
    public ICommand RemoveSubLayerCommand { get; }

    public AlertSubLayerManagerSection(AlertOverlayContent alertContent, ICommand addSubLayerCommand, ICommand removeSubLayerCommand)
    {
        AlertContent = alertContent;
        AddSubLayerCommand = addSubLayerCommand;
        RemoveSubLayerCommand = removeSubLayerCommand;
    }
}
