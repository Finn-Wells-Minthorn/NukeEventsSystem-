using System.Collections.Generic;

namespace MyFirstPlugin.Config;

public sealed class BottomInfoConfig
{
    public bool Enabled { get; set; } = true;

    public float VerticalPosition { get; set; } = 2f;

    public int FontSize { get; set; } = 18;

    public string ServerInfoText { get; set; } = "NUKE EVENTS";

    public string ServerInfoColor { get; set; } = "#D9F2FF";

    public bool GradientEnabled { get; set; } = true;

    public float GradientAnimationSpeed { get; set; } = 0.15f;

    public float GradientRefreshIntervalSeconds { get; set; } = 0.5f;

    public List<string> GradientColors { get; set; } = new()
    {
        "#FF0000",
        "#FF8C00",
        "#FFFF00",
        "#00FF00",
        "#00BFFF",
        "#8A2BE2"
    };
}
