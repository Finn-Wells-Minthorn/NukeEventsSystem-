namespace MyFirstPlugin.Config;

public sealed class EventDisplayConfig
{
    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#FFFFFF";

    public string Description { get; set; } = string.Empty;
}

internal static class DefaultEventDisplayNames
{
    public const string Infection = "Infection";
    public const string JailbirdMayhem = "Jailbird mayhem";
    public const string Escalation = "Escalation";
    public const string SpeedDemon = "Speed demon";
    public const string TimeToGamble = "Time to gamble (development)";
    public const string Blackout = "Blackout event";
}
