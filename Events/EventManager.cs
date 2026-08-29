using System;
using System.Collections.Generic;
using System.Linq;

namespace MyFirstPlugin.Events;

public static class EventManager
{
    private static readonly Dictionary<string, EventBase> Registered = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Random Random = new();

    public static event Action<EventBase>? EventStarting;

    public static event Action<EventBase>? EventStarted;

    public static event Action<EventBase>? EventStopped;

    public static EventBase? CurrentEvent { get; private set; }

    public static IReadOnlyCollection<EventBase> RegisteredEvents => Registered.Values;

    public static bool IsEventRunning(EventBase? eventInstance)
    {
        if (eventInstance == null)
            return false;

        return eventInstance.IsRunning;
    }

    public static bool IsEventRunning(string eventName)
    {
        EventBase? eventInstance = GetEvent(eventName);
        return IsEventRunning(eventInstance);
    }

    public static bool IsEventEnabled(EventBase? eventInstance)
    {
        if (eventInstance == null)
            return false;

        return eventInstance.IsEnabled;
    }

    public static bool IsEventEnabled(string eventName)
    {
        EventBase? eventInstance = GetEvent(eventName);
        return IsEventEnabled(eventInstance);
    }

    public static void Register(EventBase eventInstance)
    {
        if (eventInstance == null)
            throw new ArgumentNullException(nameof(eventInstance));

        if (string.IsNullOrWhiteSpace(eventInstance.Name))
            throw new InvalidOperationException("Event name cannot be empty.");

        if (Registered.ContainsKey(eventInstance.Name))
            throw new InvalidOperationException($"An event with the name '{eventInstance.Name}' is already registered.");

        Registered[eventInstance.Name] = eventInstance;
    }

    public static bool Unregister(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return false;

        if (CurrentEvent != null && string.Equals(CurrentEvent.Name, eventName, StringComparison.OrdinalIgnoreCase))
            StopCurrentEvent();

        return Registered.Remove(eventName);
    }

    public static bool Unregister(EventBase eventInstance)
    {
        if (eventInstance == null)
            return false;

        return Unregister(eventInstance.Name);
    }

    public static EventBase? GetEvent(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return null;

        Registered.TryGetValue(eventName, out EventBase? eventInstance);
        return eventInstance;
    }

    public static EventBase? StartEvent(string eventName)
    {
        EventBase? eventInstance = GetEvent(eventName);
        if (eventInstance == null)
            return null;

        return StartEvent(eventInstance);
    }

    public static EventBase? StartEvent(EventBase eventInstance)
    {
        if (eventInstance == null)
            return null;

        if (!eventInstance.IsEnabled)
            return null;

        EventStarting?.Invoke(eventInstance);

        if (eventInstance.IsRunning)
            return eventInstance;

        if (CurrentEvent != null && CurrentEvent != eventInstance)
            StopCurrentEvent();

        CurrentEvent = eventInstance;

        try
        {
            eventInstance.Start();
        }
        catch
        {
            if (CurrentEvent == eventInstance)
                CurrentEvent = null;

            throw;
        }

        if (!eventInstance.IsRunning)
        {
            if (CurrentEvent == eventInstance)
                CurrentEvent = null;

            return null;
        }

        EventStarted?.Invoke(eventInstance);

        return CurrentEvent;
    }

    public static EventBase? StopCurrentEvent()
    {
        if (CurrentEvent == null)
            return null;

        EventBase current = CurrentEvent;
        CurrentEvent = null;

        try
        {
            current.Stop();
        }
        finally
        {
            EventStopped?.Invoke(current);
        }

        return current;
    }

    public static void Reset()
    {
        try
        {
            StopCurrentEvent();
        }
        finally
        {
            Registered.Clear();
            CurrentEvent = null;
            EventStarting = null;
            EventStarted = null;
            EventStopped = null;
        }
    }

    public static EventBase? SelectRandomEvent()
    {
        List<EventBase> available = Registered.Values
            .Where(x => x.IsEnabled)
            .ToList();

        if (available.Count == 0)
            return null;

        return available[Random.Next(available.Count)];
    }

    public static EventBase? StartRandomEvent()
    {
        EventBase? selectedEvent = SelectRandomEvent();
        if (selectedEvent == null)
            return null;

        return StartEvent(selectedEvent);
    }

    public static bool EnableEvent(string eventName)
    {
        EventBase? eventInstance = GetEvent(eventName);
        return EnableEvent(eventInstance);
    }

    public static bool EnableEvent(EventBase? eventInstance)
    {
        if (eventInstance == null)
            return false;

        eventInstance.Enable();
        return true;
    }

    public static bool DisableEvent(string eventName)
    {
        EventBase? eventInstance = GetEvent(eventName);
        return DisableEvent(eventInstance);
    }

    public static bool DisableEvent(EventBase? eventInstance)
    {
        if (eventInstance == null)
            return false;

        // Stop first if the target is running, then disable it.
        if (CurrentEvent == eventInstance)
        {
            StopCurrentEvent();
        }
        else if (eventInstance.IsRunning)
        {
            eventInstance.Stop();
        }

        eventInstance.Disable();
        return true;
    }
}
