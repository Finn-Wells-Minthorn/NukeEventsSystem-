namespace MyFirstPlugin.Config;

public sealed class NormalRoundConfig
{
    public bool Enabled { get; set; } = true;

    public float ChancePercent { get; set; } = 50f;

    public EventDisplayConfig Display { get; set; } = new()
    {
        Name = "normal round",
        Color = "#D9F2FF",
        Description = "No special event will run this round."
    };

    public float GetClampedChancePercent()
    {
        if (ChancePercent < 0f)
            return 0f;

        if (ChancePercent > 100f)
            return 100f;

        return ChancePercent;
    }
}
