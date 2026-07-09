using System.ComponentModel;

namespace StreamFlow.Core.Data.UserOptions;

public enum UserOptionCategory
{
    [Description("Audio Settings")]
    Audio,
    [Description("Scene Settings")]
    Scene,
    [Description("Output Device")]
    OutputDevice,
    [Description("UI")]
    UI,
    [Description("Others")]
    Others
}
