using MyFirstPlugin.Config;
using MyFirstPlugin.Hints;

namespace MyFirstPlugin.Events;

internal readonly struct EventDisplayMetadata
{
    private EventDisplayMetadata(string name, string color, string description)
    {
        Name = name;
        Color = color;
        Description = description;
    }

    public string Name { get; }

    public string Color { get; }

    public string Description { get; }

    public static EventDisplayMetadata Resolve(EventDisplayConfig? config, string fallbackName)
    {
        string name = string.IsNullOrWhiteSpace(config?.Name)
            ? fallbackName
            : config!.Name.Trim();

        string description = string.IsNullOrWhiteSpace(config?.Description)
            ? string.Empty
            : config!.Description.Trim();

        return new EventDisplayMetadata(
            name,
            HintUiFormatter.ResolveColor(config?.Color, HintUiFormatter.DefaultEventColor),
            description);
    }
}
