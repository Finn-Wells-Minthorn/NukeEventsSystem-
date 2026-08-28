namespace MyFirstPlugin.Config;

public sealed class EventDisplayConfig
{
    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#FFFFFF";

    public string Description { get; set; } = string.Empty;
}

internal static class DefaultEventDisplayNames
{
    public const string Infection = "infection";
    public const string JailbirdMayhem = "jailbird mayhem";
    public const string Escalation = "escalation";
    public const string SpeedDemon = "speed demon";
    public const string TimeToGamble = "time to gamble";
    public const string Blackout = "blackout event";
}
