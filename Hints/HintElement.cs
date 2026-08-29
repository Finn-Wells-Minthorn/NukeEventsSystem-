using System;

namespace MyFirstPlugin.Hints;

internal enum HintElementId
{
    LobbyEventHeader,
    LobbyEventName,
    ServerInfo,
    EventInfo,
    Tip,
    BottomInfo,
    ManualTestPrimary,
    ManualTestSecondary
}

internal enum HintAlignment
{
    Left,
    Center,
    Right
}

internal sealed class HintElement : IEquatable<HintElement>
{
    public HintElement(
        HintElementId id,
        string content,
        float verticalPosition,
        HintAlignment alignment = HintAlignment.Center)
    {
        if (string.IsNullOrEmpty(content))
            throw new ArgumentException("Hint content cannot be empty.", nameof(content));

        if (float.IsNaN(verticalPosition) || float.IsInfinity(verticalPosition) ||
            verticalPosition < 0f || verticalPosition > 1000f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verticalPosition),
                verticalPosition,
                "Hint vertical positions must be between 0 and 1000.");
        }

        Id = id;
        Content = content;
        VerticalPosition = verticalPosition;
        Alignment = alignment;
    }

    public HintElementId Id { get; }

    public string Content { get; }

    public float VerticalPosition { get; }

    public HintAlignment Alignment { get; }

    public bool Equals(HintElement? other)
    {
        return other != null &&
               Id == other.Id &&
               string.Equals(Content, other.Content, StringComparison.Ordinal) &&
               VerticalPosition.Equals(other.VerticalPosition) &&
               Alignment == other.Alignment;
    }

    public override bool Equals(object? obj) => Equals(obj as HintElement);

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = (int)Id;
            hashCode = (hashCode * 397) ^ Content.GetHashCode();
            hashCode = (hashCode * 397) ^ VerticalPosition.GetHashCode();
            hashCode = (hashCode * 397) ^ (int)Alignment;
            return hashCode;
        }
    }
}
