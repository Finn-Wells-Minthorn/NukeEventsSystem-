using System;
using System.Collections.Generic;
using System.Linq;
using MyFirstPlugin.Config;

namespace MyFirstPlugin.Events;

public interface IEventSelectionStrategy
{
    EventBase? Select(IEnumerable<EventBase> availableEvents);
}

public sealed class RandomEventSelectionStrategy : IEventSelectionStrategy
{
    private readonly Random _random;

    public RandomEventSelectionStrategy()
        : this(new Random())
    {
    }

    public RandomEventSelectionStrategy(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public EventBase? Select(IEnumerable<EventBase> availableEvents)
    {
        List<EventBase> events = availableEvents
            .Where(x => x != null)
            .ToList();

        if (events.Count == 0)
            return null;

        return events[_random.Next(events.Count)];
    }
}

public sealed class EventSelector
{
    private readonly IEventSelectionStrategy _strategy;
    private readonly NormalRoundConfig _normalRound;
    private readonly Func<double> _nextRoll;

    public EventSelector()
        : this(new NormalRoundConfig())
    {
    }

    public EventSelector(NormalRoundConfig normalRound)
        : this(new RandomEventSelectionStrategy(), normalRound, new Random().NextDouble)
    {
    }

    public EventSelector(
        IEventSelectionStrategy strategy,
        NormalRoundConfig normalRound,
        Func<double>? nextRoll = null)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _normalRound = normalRound ?? throw new ArgumentNullException(nameof(normalRound));
        _nextRoll = nextRoll ?? new Random().NextDouble;
    }

    public IReadOnlyList<EventBase> GetAvailableEvents()
    {
        return EventManager.RegisteredEvents
            .Where(x => x.IsEnabled)
            .ToList();
    }

    public IReadOnlyList<EventSelectionOption> GetRouletteOptions()
    {
        List<EventSelectionOption> options = GetAvailableEvents()
            .Select(EventSelectionOption.ForEvent)
            .ToList();

        if (_normalRound.Enabled)
            options.Add(EventSelectionOption.NormalRound(_normalRound));

        return options;
    }

    public EventSelectionOption? Select()
    {
        IReadOnlyList<EventBase> availableEvents = GetAvailableEvents();

        if (_normalRound.Enabled)
        {
            if (availableEvents.Count == 0)
                return EventSelectionOption.NormalRound(_normalRound);

            double chance = _normalRound.GetClampedChancePercent();
            if (chance >= 100d || (chance > 0d && _nextRoll() * 100d < chance))
                return EventSelectionOption.NormalRound(_normalRound);
        }

        EventBase? selectedEvent = _strategy.Select(availableEvents);
        return selectedEvent == null
            ? null
            : EventSelectionOption.ForEvent(selectedEvent);
    }
}
