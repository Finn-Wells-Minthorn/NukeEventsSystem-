using System.Collections.Generic;

namespace MyFirstPlugin.Config;

public sealed class BottomInfoConfig
{
    public bool Enabled { get; set; } = true;

    public float VerticalPosition { get; set; } = 2f;

    public int FontSize { get; set; } = 18;

    public string TextColor { get; set; } = "#D9F2FF";

    public bool ShowServerInfo { get; set; } = true;

    public string ServerInfoText { get; set; } = "NUKE EVENTS";

    public string ServerInfoColor { get; set; } = "";

    public float ServerInfoDurationSeconds { get; set; } = 40f;

    public bool ShowEventDetails { get; set; } = true;

    public float EventDetailsDurationSeconds { get; set; } = 10f;

    public bool TipsEnabled { get; set; } = false;

    public string TipColor { get; set; } = "#FFE6A3";

    public float TipDurationSeconds { get; set; } = 45f;

    public List<string> Tips { get; set; } = new()
    {
        "Special events are selected before each round.",
        "Adapt your strategy to the active event.",
        "Work with your team and watch for event-specific changes."
    };
}
