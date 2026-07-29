using System.Windows.Input;
using StreamFlow.Plugin.SDK.Overlays.Sections;

namespace StreamFlow.App.Services.Overlays.Sections;

public sealed record CommandEntry(string Label, ICommand Command, object? Parameter = null);

public sealed class CommandGroupSection : IOverlayPropertySection
{
    public string? Header { get; }
    public IReadOnlyList<CommandEntry> Commands { get; }

    public CommandGroupSection(string? header, IReadOnlyList<CommandEntry> commands)
    {
        Header = header;
        Commands = commands;
    }
}
