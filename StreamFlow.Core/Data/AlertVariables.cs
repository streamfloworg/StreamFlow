using System;
using System.Collections.Generic;

namespace StreamFlow.Core.Data;

public sealed record AlertVariable(string Key, string Description, string ExampleValue);

public static class AlertVariableRegistry
{
    private static readonly Dictionary<StreamAlertType, List<AlertVariable>> _variables = new()
    {
        {
            StreamAlertType.TwitchFollower, new()
            {
                new("followerName", "The name of the user who followed", "TwitchFollower"),
                new("username", "Alias for followerName", "TwitchFollower")
            }
        },
        {
            StreamAlertType.TwitchSubscriber, new()
            {
                new("subscriberName", "The name of the user who subscribed", "TwitchSubscriber"),
                new("username", "Alias for subscriberName", "TwitchSubscriber"),
                new("tier", "The tier of the subscription (e.g. Prime, Tier 1)", "Tier 1")
            }
        },
        {
            StreamAlertType.TwitchBits, new()
            {
                new("username", "The name of the user who cheered bits", "TwitchViewer"),
                new("amount", "The number of bits cheered", "500")
            }
        },
        {
            StreamAlertType.TwitchRaid, new()
            {
                new("raiderName", "The name of the raiding channel", "RaidHost"),
                new("username", "Alias for raiderName", "RaidHost"),
                new("viewerCount", "The number of viewers in the raid", "42"),
                new("viewers", "Alias for viewerCount", "42")
            }
        },
        {
            StreamAlertType.YouTubeSubscriber, new()
            {
                new("subscriberName", "The name of the user who subscribed", "YouTubeSubscriber"),
                new("username", "Alias for subscriberName", "YouTubeSubscriber")
            }
        },
        {
            StreamAlertType.YouTubeMember, new()
            {
                new("memberName", "The name of the user who joined", "YouTubeMember"),
                new("username", "Alias for memberName", "YouTubeMember"),
                new("level", "The membership level joined", "Level 2")
            }
        },
        {
            StreamAlertType.YouTubeSuperChat, new()
            {
                new("username", "The name of the user who sent the Super Chat", "YouTubeViewer"),
                new("amount", "The amount sent (formatted)", "$10.00"),
                new("currency", "The currency symbol/code", "USD")
            }
        },
        {
            StreamAlertType.GeneralDonation, new()
            {
                new("donorName", "The name of the donor", "GenerousDonor"),
                new("username", "Alias for donorName", "GenerousDonor"),
                new("amount", "The amount donated (formatted)", "$25.00"),
                new("currency", "The currency symbol/code", "USD")
            }
        }
    };

    public static IReadOnlyList<AlertVariable> GetVariablesForType(StreamAlertType type)
    {
        if (_variables.TryGetValue(type, out var list))
        {
            return list;
        }
        return Array.Empty<AlertVariable>();
    }
}

public sealed class AlertTriggerContext
{
    public StreamAlertType AlertType { get; }
    public string FallbackMessage { get; }
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public AlertTriggerContext(StreamAlertType alertType, string fallbackMessage)
    {
        AlertType = alertType;
        FallbackMessage = fallbackMessage;
    }

    public void AddValue(string variableKey, string value)
    {
        _values[variableKey] = value;
    }

    public string ReplaceVariables(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return FallbackMessage;
        }

        var result = template;
        var replacedAny = false;

        foreach (var (key, val) in _values)
        {
            var placeholder = $"%{key}%";
            if (result.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                result = result.Replace(placeholder, val, StringComparison.OrdinalIgnoreCase);
                replacedAny = true;
            }
        }

        if (template.Equals("Text Overlay", StringComparison.OrdinalIgnoreCase) && !replacedAny)
        {
            return FallbackMessage;
        }

        return result;
    }
}
