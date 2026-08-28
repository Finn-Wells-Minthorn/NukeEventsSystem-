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

        StringBuilder builder = new("<line-height=0>");
        float previousBaseline = 0f;

        for (int index = 0; index < orderedElements.Count; index++)
        {
            HintElement element = orderedElements[index];
            float baseline = ToBaseline(element.VerticalPosition);
            float lineHeight = index == 0
                ? baseline
                : (previousBaseline - baseline) / -2f;

            AppendLineAdvance(builder, lineHeight);
            builder.Append("<align=")
                .Append(ToAlignmentValue(element.Alignment))
                .Append('>')
                .Append(element.Content)
                .Append("</align>");

            previousBaseline = baseline;
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
