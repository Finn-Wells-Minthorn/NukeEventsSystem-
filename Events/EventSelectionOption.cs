using System;
using MyFirstPlugin.Config;

namespace MyFirstPlugin.Events;

public sealed class EventSelectionOption
{
    private readonly string _displayName;
    private readonly string _displayColor;
    private readonly string _description;

    private EventSelectionOption(
        EventBase? eventInstance,
        bool isNormalRound,
        string displayName,
        string displayColor,
        string description)
    {
        Event = eventInstance;
        IsNormalRound = isNormalRound;
        _displayName = displayName;
        _displayColor = displayColor;
        _description = description;
    }

    public EventBase? Event { get; }

    public bool IsNormalRound { get; }

    public string Identity => IsNormalRound
        ? "normal-round"
        : Event?.Name ?? string.Empty;

    public string DisplayName => IsNormalRound
        ? _displayName
        : Event?.DisplayName ?? _displayName;

    public string DisplayColor => IsNormalRound
        ? _displayColor
        : Event?.DisplayColor ?? _displayColor;

    public string Description => IsNormalRound
        ? _description
        : Event?.Description ?? _description;

    public static EventSelectionOption ForEvent(EventBase eventInstance)
    {
        if (eventInstance == null)
            throw new ArgumentNullException(nameof(eventInstance));

        return new EventSelectionOption(
            eventInstance,
            isNormalRound: false,
            eventInstance.DisplayName,
            eventInstance.DisplayColor,
            eventInstance.Description);
    }

    public static EventSelectionOption NormalRound(NormalRoundConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        EventDisplayMetadata metadata = EventDisplayMetadata.Resolve(config.Display, "normal round");
        return new EventSelectionOption(
            eventInstance: null,
            isNormalRound: true,
            metadata.Name,
            metadata.Color,
            metadata.Description);
    }
}
