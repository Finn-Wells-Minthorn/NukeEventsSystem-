using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MyFirstPlugin.Hints;

internal static class HintComposer
{
    // RueI's empirically tested 0..1000-to-hint-baseline conversion is reused
    // here as a small implementation detail, without taking a RueI dependency:
    // https://github.com/pawslee/RueI/blob/master/RueI/Utils/PositionUtils.cs
    private const float BaselineAddend = 755f;
    private const float BaselineMultiplier = -2.14f;

    public static string Compose(IEnumerable<HintElement> elements)
    {
        List<HintElement> orderedElements = elements
            .OrderByDescending(element => element.VerticalPosition)
            .ThenBy(element => element.Id)
            .ToList();

        if (orderedElements.Count == 0)
            return string.Empty;

        float[] baselines = orderedElements
            .Select(element => ToBaseline(element.VerticalPosition))
            .ToArray();
        float[] lineAdvances = new float[Math.Max(0, orderedElements.Count - 1)];
        float initialOffset = baselines[0];

        for (int index = 1; index < orderedElements.Count; index++)
        {
            float lineAdvance = (baselines[index - 1] - baselines[index]) / -2f;
            lineAdvances[index - 1] = lineAdvance;
            initialOffset += lineAdvance;
        }

        // Hint text is vertically centered as one native TMP block. Prepending
        // the cumulative offset compensates for every following element, so a
        // lower element cannot push an earlier element away from its position.
        StringBuilder builder = new();
        AppendLineAdvance(builder, initialOffset);

        for (int index = 0; index < orderedElements.Count; index++)
        {
            if (index > 0)
                AppendLineAdvance(builder, lineAdvances[index - 1]);

            HintElement element = orderedElements[index];
            builder.Append("<align=")
                .Append(ToAlignmentValue(element.Alignment))
                .Append('>')
                .Append(element.Content)
                .Append("</align>");
        }

        // The invisible trailing glyph makes the client apply a final line break
        // without adding a visible element to the composed hint.
        builder.Append("<line-height=0>\n<alpha=#00><scale=0>.");
        return builder.ToString();
    }

    internal static float ToBaseline(float verticalPosition) =>
        (verticalPosition * BaselineMultiplier) + BaselineAddend;

    private static void AppendLineAdvance(StringBuilder builder, float lineHeight)
    {
        builder.Append("<line-height=")
            .Append(lineHeight.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(">\n<line-height=0>");
    }

    private static string ToAlignmentValue(HintAlignment alignment)
    {
        switch (alignment)
        {
            case HintAlignment.Left:
                return "left";
            case HintAlignment.Right:
                return "right";
            default:
                return "center";
        }
    }
}
