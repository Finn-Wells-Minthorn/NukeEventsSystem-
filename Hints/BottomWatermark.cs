using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MyFirstPlugin.Hints;

internal sealed class BottomWatermarkRenderer
{
    public const float DefaultAnimationSpeed = 0.15f;
    public const float DefaultRefreshIntervalSeconds = 0.5f;
    public const float MinimumRefreshIntervalSeconds = 0.25f;

    private static readonly string[] DefaultGradientColors =
    {
        "#FF0000",
        "#FF8C00",
        "#FFFF00",
        "#00FF00",
        "#00BFFF",
        "#8A2BE2"
    };

    private readonly IReadOnlyList<RgbColor> _gradientColors;
    private readonly string _staticColor;

    public BottomWatermarkRenderer(
        bool gradientEnabled,
        IEnumerable<string>? gradientColors,
        float animationSpeed,
        float refreshIntervalSeconds,
        string? staticColor)
    {
        GradientEnabled = gradientEnabled;
        _gradientColors = ResolveGradientColors(gradientColors, out bool usedDefaultGradient);
        UsedDefaultGradient = usedDefaultGradient;
        AnimationSpeed = ResolveNonNegativeFinite(
            animationSpeed,
            DefaultAnimationSpeed);
        RefreshIntervalSeconds = Math.Max(
            MinimumRefreshIntervalSeconds,
            ResolveNonNegativeFinite(
                refreshIntervalSeconds,
                DefaultRefreshIntervalSeconds));
        _staticColor = HintUiFormatter.ResolveColor(
            staticColor,
            HintUiFormatter.DefaultTextColor);
    }

    public bool GradientEnabled { get; }

    public bool UsedDefaultGradient { get; }

    public bool CanAnimate =>
        GradientEnabled && _gradientColors.Count > 1 && AnimationSpeed > 0f;

    public float AnimationSpeed { get; }

    public float RefreshIntervalSeconds { get; }

    public string Format(
        string? serverText,
        string? activeEventName,
        int fontSize,
        float phase)
    {
        string readableServerText = string.IsNullOrWhiteSpace(serverText)
            ? "NUKE EVENTS"
            : serverText!.Trim();
        int readableFontSize = Math.Max(1, fontSize);
        StringBuilder builder = new();

        builder.Append("<size=")
            .Append(readableFontSize)
            .Append("><nobr><b>");

        if (GradientEnabled)
            AppendGradientText(builder, readableServerText, NormalizePhase(phase));
        else
            AppendColoredText(builder, readableServerText, _staticColor);

        builder.Append("</b>");

        if (!string.IsNullOrWhiteSpace(activeEventName))
        {
            builder.Append(" <color=")
                .Append(HintUiFormatter.DefaultEventColor)
                .Append('>');
            AppendEscapedText(builder, activeEventName!.Trim());
            builder.Append("</color>");
        }

        return builder.Append("</nobr></size>").ToString();
    }

    public float AdvancePhase(float phase) =>
        NormalizePhase(phase + (AnimationSpeed * RefreshIntervalSeconds));

    private void AppendGradientText(StringBuilder builder, string text, float phase)
    {
        int visibleCharacterCount = text.Count(character => !char.IsWhiteSpace(character));
        int visibleCharacterIndex = 0;

        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                builder.Append(character);
                continue;
            }

            float characterPosition = visibleCharacterCount <= 1
                ? phase
                : phase + (visibleCharacterIndex / (float)visibleCharacterCount);
            string color = SampleGradient(characterPosition);

            builder.Append("<color=")
                .Append(color)
                .Append('>');
            AppendEscapedCharacter(builder, character);
            builder.Append("</color>");
            ++visibleCharacterIndex;
        }
    }

    private string SampleGradient(float position)
    {
        float scaledPosition = NormalizePhase(position) * _gradientColors.Count;
        int firstIndex = (int)Math.Floor(scaledPosition) % _gradientColors.Count;
        int secondIndex = (firstIndex + 1) % _gradientColors.Count;
        float amount = scaledPosition - (float)Math.Floor(scaledPosition);
        return RgbColor.Lerp(_gradientColors[firstIndex], _gradientColors[secondIndex], amount)
            .ToHex();
    }

    private static void AppendColoredText(StringBuilder builder, string text, string color)
    {
        builder.Append("<color=")
            .Append(color)
            .Append('>');
        AppendEscapedText(builder, text);
        builder.Append("</color>");
    }

    private static void AppendEscapedText(StringBuilder builder, string text)
    {
        foreach (char character in text)
            AppendEscapedCharacter(builder, character);
    }

    private static void AppendEscapedCharacter(StringBuilder builder, char character)
    {
        switch (character)
        {
            case '&':
                builder.Append("&amp;");
                break;
            case '<':
                builder.Append("&lt;");
                break;
            case '>':
                builder.Append("&gt;");
                break;
            default:
                builder.Append(character);
                break;
        }
    }

    private static IReadOnlyList<RgbColor> ResolveGradientColors(
        IEnumerable<string>? configuredColors,
        out bool usedDefaultGradient)
    {
        List<RgbColor> colors = configuredColors?
            .Select(color => RgbColor.TryParse(color, out RgbColor parsed)
                ? (RgbColor?)parsed
                : null)
            .Where(color => color.HasValue)
            .Select(color => color!.Value)
            .ToList() ?? new List<RgbColor>();

        usedDefaultGradient = colors.Count < 2;
        if (!usedDefaultGradient)
            return colors;

        return DefaultGradientColors
            .Select(color =>
            {
                RgbColor.TryParse(color, out RgbColor parsed);
                return parsed;
            })
            .ToList();
    }

    private static float ResolveNonNegativeFinite(float value, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return fallback;

        return Math.Max(0f, value);
    }

    private static float NormalizePhase(float phase)
    {
        if (float.IsNaN(phase) || float.IsInfinity(phase))
            return 0f;

        phase %= 1f;
        return phase < 0f ? phase + 1f : phase;
    }

    private readonly struct RgbColor
    {
        private RgbColor(byte red, byte green, byte blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        private byte Red { get; }

        private byte Green { get; }

        private byte Blue { get; }

        public static bool TryParse(string? color, out RgbColor parsed)
        {
            parsed = default;
            if (!HintUiFormatter.TryNormalizeHexColor(color, out string normalized))
                return false;

            string red;
            string green;
            string blue;

            if (normalized.Length == 4 || normalized.Length == 5)
            {
                red = new string(normalized[1], 2);
                green = new string(normalized[2], 2);
                blue = new string(normalized[3], 2);
            }
            else
            {
                red = normalized.Substring(1, 2);
                green = normalized.Substring(3, 2);
                blue = normalized.Substring(5, 2);
            }

            parsed = new RgbColor(
                byte.Parse(red, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(green, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(blue, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return true;
        }

        public static RgbColor Lerp(RgbColor first, RgbColor second, float amount)
        {
            float clampedAmount = Math.Max(0f, Math.Min(1f, amount));
            return new RgbColor(
                LerpChannel(first.Red, second.Red, clampedAmount),
                LerpChannel(first.Green, second.Green, clampedAmount),
                LerpChannel(first.Blue, second.Blue, clampedAmount));
        }

        public string ToHex() =>
            $"#{Red:X2}{Green:X2}{Blue:X2}";

        private static byte LerpChannel(byte first, byte second, float amount) =>
            (byte)Math.Round(first + ((second - first) * amount));
    }
}

internal sealed class BottomWatermarkAnimationState
{
    private int _generation;

    public bool IsRunning { get; private set; }

    public bool TryStart(out int generation)
    {
        if (IsRunning)
        {
            generation = _generation;
            return false;
        }

        IsRunning = true;
        generation = ++_generation;
        return true;
    }

    public void Stop()
    {
        IsRunning = false;
        ++_generation;
    }

    public bool IsCurrent(int generation) =>
        IsRunning && generation == _generation;
}
