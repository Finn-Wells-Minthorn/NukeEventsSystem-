using MyFirstPlugin.Events;

namespace MyFirstPlugin.Config;

public sealed class EventRollConfig
{
    public float HeaderVerticalPosition { get; set; } = 250f;

    public float EventNameVerticalPosition { get; set; } = 205f;

    public float TotalDurationSeconds { get; set; } = RouletteTiming.DefaultDurationSeconds;
}
