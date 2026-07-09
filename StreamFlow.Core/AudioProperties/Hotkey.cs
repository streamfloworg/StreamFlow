using System.Text;
using System.Windows.Input;

using Newtonsoft.Json;

namespace StreamFlow.Core.AudioProperties;

[JsonObject]
public class Hotkey(Key key, ModifierKeys modifiers)
{
    public Key Key
    {
        get;
    } = key;

    public ModifierKeys Modifiers
    {
        get;
    } = modifiers;

    public override string ToString()
    {
        var str = new StringBuilder();

        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            str.Append("Ctrl + ");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            str.Append("Shift + ");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            str.Append("Alt + ");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            str.Append("Win + ");
        }

        str.Append(Key);

        return str.ToString();
    }
}

