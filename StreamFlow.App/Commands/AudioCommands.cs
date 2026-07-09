using System.Windows.Input;

using StreamFlow.Core.AudioHandling;

namespace StreamFlow.App.Commands;

public class PlayAudioCommand() : AudioCommandBase("StreamFlow.PlayAudio")
{

    public override bool CanExecute(object? parameter)
    {
        return (AudioVM.TrackLoaded || !AudioVM.IsPlaying);
    }

    public override void Execute(object? parameter)
    {
        if (AudioVM.TrackLoaded && AudioVM.IsPaused)
        {
            AudioVM.PlayAudioCommand.Execute(parameter);
        }
        else if (!AudioVM.TrackLoaded)
        {
            AudioVM.StopAudioCommand.Execute(null);
        }
    }
}

public class QueueAudioCommand() : AudioCommandBase("StreamFlow.QueueAudio")
{
    public override bool CanExecute(object? parameter)
    {
        return true;
    }

    public override void Execute(object? parameter)
    {
        AudioVM.QueueAudioItemCommand.Execute(parameter);
    }
}

public class UnloadAudioCommand() : AudioCommandBase("StreamFlow.UnloadAudio")
{
    public override bool CanExecute(object? parameter)
    {
        return AudioVM.TrackLoaded;
    }

    public override void Execute(object? parameter)
    {
        AudioVM.TrackLoaded = false;
    }
}

public class DeleteAudioCommand() : AudioCommandBase("StreamFlow.DeleteAudio")
{
    public override bool CanExecute(object? parameter)
    {
        return parameter is Audio;
    }

    public override void Execute(object? parameter)
    {
        AudioVM.RemoveAudioCommand.Execute(parameter);
    }
}

public class StopAudioCommand() : AudioCommandBase("StreamFlow.StopAudio")
{
    public override bool CanExecute(object? parameter)
    {
        return AudioVM.IsPlaying;
    }

    public override void Execute(object? parameter)
    {
        AudioVM.StopAudioCommand.Execute(parameter);
    }
}

public class StopAllAudioCommand() : AudioCommandBase("StreamFlow.StopAllAudio")
{
    public override bool CanExecute(object? parameter)
    {
        return AudioVM.IsPlaying || AudioVM.PlayingSoundEffects.Count > 0;
    }

    public override void Execute(object? parameter)
    {
        AudioVM.StopAudioCommand.Execute(true);
    }
}

public class AudioPropertiesCommand() : AudioCommandBase("StreamFlow.AudioProperties")
{
    public override bool CanExecute(object? parameter)
    {
        return parameter is Audio;
    }

    public override void Execute(object? parameter)
    {
        AudioVM.EditAudioPropertiesCommand.Execute(parameter);
    }
}

public static class AudioCommands
{
    public static readonly CommandBinding PlayAudio = new(new PlayAudioCommand());

    public static readonly CommandBinding QueueAudio = new(new QueueAudioCommand());

    public static readonly CommandBinding UnloadAudio = new(new UnloadAudioCommand());

    public static readonly CommandBinding DeleteAudio = new(new DeleteAudioCommand());

    public static readonly CommandBinding StopAudio = new(new StopAudioCommand());

    public static readonly CommandBinding StopAllAudio = new(new StopAllAudioCommand());

    public static readonly CommandBinding AudioProperties = new(new AudioPropertiesCommand());
}
