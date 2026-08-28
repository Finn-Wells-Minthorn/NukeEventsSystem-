using System;

namespace MyFirstPlugin.Hints;

internal static class HintUiFormatter
{
    public const string DefaultEventColor = "#FFFFFF";
    public const string DefaultTextColor = "#D9F2FF";

    public static string FormatEventName(string eventName, string? color)
    {
        string readableName = string.IsNullOrWhiteSpace(eventName) ? "Unknown Event" : eventName.Trim();
        return $"<color={ResolveColor(color, DefaultEventColor)}><b>{readableName}</b></color>";
    }

    public static string FormatBottomText(string text, string? color, string? defaultColor, int fontSize)
    {
        string readableText = string.IsNullOrWhiteSpace(text) ? " " : text.Trim();
        string readableColor = ResolveColor(color, ResolveColor(defaultColor, DefaultTextColor));
        int readableSize = Math.Max(1, fontSize);
        return $"<size={readableSize}><color={readableColor}>{readableText}</color></size>";
    }

    public static string ResolveColor(string? color, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(color))
            return color!.Trim();

        return string.IsNullOrWhiteSpace(fallback) ? DefaultEventColor : fallback.Trim();
    }
}
