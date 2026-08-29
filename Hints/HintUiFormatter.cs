using System;
using System.Globalization;

namespace MyFirstPlugin.Hints;

internal static class HintUiFormatter
{
    public const string DefaultEventColor = "#FFFFFF";
    public const string DefaultTextColor = "#D9F2FF";

    public static string FormatEventName(string eventName, string? color, bool bold = true)
    {
        string readableName = string.IsNullOrWhiteSpace(eventName) ? "Unknown Event" : eventName.Trim();
        string formattedName = bold ? $"<b>{readableName}</b>" : readableName;
        return $"<nobr><color={ResolveColor(color, DefaultEventColor)}>{formattedName}</color></nobr>";
    }

    public static string ResolveColor(string? color, string fallback)
    {
        if (TryNormalizeHexColor(color, out string normalizedColor))
            return normalizedColor;

        if (TryNormalizeHexColor(fallback, out string normalizedFallback))
            return normalizedFallback;

        return DefaultEventColor;
    }

    internal static bool TryNormalizeHexColor(string? color, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(color))
            return false;

        string candidate = color!.Trim();
        if (candidate.Length != 4 && candidate.Length != 5 &&
            candidate.Length != 7 && candidate.Length != 9)
        {
            return false;
        }

        if (candidate[0] != '#')
            return false;

        for (int index = 1; index < candidate.Length; index++)
        {
            if (!byte.TryParse(
                    candidate[index].ToString(),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return false;
            }
        }

        normalized = candidate.ToUpperInvariant();
        return true;
    }
}
