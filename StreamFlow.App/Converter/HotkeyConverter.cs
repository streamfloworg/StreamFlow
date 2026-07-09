using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;

namespace StreamFlow.App.Converter;

public class HotkeyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Core.AudioProperties.Hotkey hotkey)
        {
            var str = new StringBuilder();

            if (hotkey.Modifiers.HasFlag(ModifierKeys.Control))
            {
                str.Append("Ctrl + ");
            }

            if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                str.Append("Shift + ");
            }

            if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                str.Append("Alt + ");
            }

            if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows))
            {
                str.Append("Win + ");
            }

            str.Append(hotkey.Key);

            return str.ToString();
        }
        return "none";
    }

    private static readonly string[] separator = [" + "];

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            var parts = str.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            var modifiers = ModifierKeys.None;
            var key = Key.None;
            foreach (var part in parts)
            {
                switch (part)
                {
                    case "Ctrl":
                        modifiers |= ModifierKeys.Control;
                        break;
                    case "Shift":
                        modifiers |= ModifierKeys.Shift;
                        break;
                    case "Alt":
                        modifiers |= ModifierKeys.Alt;
                        break;
                    case "Win":
                        modifiers |= ModifierKeys.Windows;
                        break;
                    default:
                        if (Enum.TryParse(part, out Key parsedKey))
                        {
                            key = parsedKey;
                        }
                        break;
                }
            }
            return new Core.AudioProperties.Hotkey(key, modifiers);
        }
        return null;
    }
}
