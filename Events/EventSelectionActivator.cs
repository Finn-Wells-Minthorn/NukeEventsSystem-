using System;

namespace MyFirstPlugin.Events;

public static class EventSelectionActivator
{
    public static EventBase? Start(EventSelectionOption selection)
    {
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));

        if (selection.IsNormalRound)
            return null;

        EventBase? eventInstance = selection.Event;
        return eventInstance == null
            ? null
            : EventManager.StartEvent(eventInstance);
    }
}
