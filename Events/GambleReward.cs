using System;

namespace MyFirstPlugin.Events;

public sealed class GambleReward
{
    public GambleReward()
    {
        DisplayName = string.Empty;
    }

    public GambleReward(
        ItemType itemType,
        string displayName,
        double weight)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Reward display name cannot be empty.", nameof(displayName));

        ItemType = itemType;
        DisplayName = displayName;
        Weight = weight;
    }

    public ItemType ItemType { get; set; }

    public string DisplayName { get; set; }

    public double Weight { get; set; }
}
