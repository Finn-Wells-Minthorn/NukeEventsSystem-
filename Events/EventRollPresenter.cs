
using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using MEC;
using MyFirstPlugin.Hints;

namespace MyFirstPlugin.Events;

public sealed class EventRollPresenter
{
    private readonly EventRollConfig _config;
    private CoroutineHandle _rollHandle;
    private bool _isCancelled;
    private bool _isRunning;
    private bool _isVisible;
    private bool _isFinalResult;
    private EventBase? _displayedEvent;

    private const string HeaderText = "selecting event";

    public EventRollPresenter()
        : this(new EventRollConfig())
    {
    }

    public EventRollPresenter(EventRollConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public bool IsRunning => _isRunning && _rollHandle.IsValid;

    public void ShowHeader()
    {
        _isVisible = true;
        _isFinalResult = false;
        _displayedEvent = null;

        foreach (Player player in Player.List)
            ShowCurrent(player);
    }

    public void ShowCurrent(Player player)
    {
        if (!_isVisible)
            return;

        HintManager? manager = global::MyFirstPlugin.MyFirstPlugin.Hints;
        if (manager == null)
            return;

        manager.Set(
            player,
            HintElementId.LobbyEventHeader,
            HeaderText,
            _config.HeaderVerticalPosition);

        if (_displayedEvent == null)
        {
            manager.Remove(player, HintElementId.LobbyEventName);
            return;
        }

        manager.Set(
            player,
            HintElementId.LobbyEventName,
            HintUiFormatter.FormatEventName(
                _displayedEvent.DisplayName,
                _displayedEvent.DisplayColor,
                _isFinalResult),
            _config.EventNameVerticalPosition);
    }

    public void Start(
        EventBase selectedEvent,
        IReadOnlyList<EventBase> enabledEvents,
        Func<float> getRemainingCountdownSeconds,
        Action<EventBase>? onCompleted)
    {
        if (selectedEvent == null)
            throw new ArgumentNullException(nameof(selectedEvent));

        if (enabledEvents == null)
            throw new ArgumentNullException(nameof(enabledEvents));

        if (getRemainingCountdownSeconds == null)
            throw new ArgumentNullException(nameof(getRemainingCountdownSeconds));

        CancelRoll();

        List<EventBase> eventOptions = enabledEvents
            .Where(x => x != null && x.IsEnabled && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (eventOptions.Count == 0)
        {
            _isRunning = false;
            ShowEvent(selectedEvent, isFinalResult: true);
            onCompleted?.Invoke(selectedEvent);
            return;
        }

        if (!eventOptions.Any(x => string.Equals(x.Name, selectedEvent.Name, StringComparison.OrdinalIgnoreCase)))
        {
            eventOptions.Add(selectedEvent);
        }

        _isCancelled = false;
        _isRunning = true;
        _rollHandle = Timing.RunCoroutine(
            RunRoll(selectedEvent, eventOptions, getRemainingCountdownSeconds, onCompleted));
    }

    public void Cancel()
    {
        CancelRoll();
        ClearDisplay();
    }

    private void CancelRoll()
    {
        _isCancelled = true;

        if (_rollHandle.IsValid)
            Timing.KillCoroutines(_rollHandle);

        _rollHandle = default;
        _isRunning = false;
    }

    private IEnumerator<float> RunRoll(
        EventBase selectedEvent,
        List<EventBase> eventOptions,
        Func<float> getRemainingCountdownSeconds,
        Action<EventBase>? onCompleted)
    {
        try
        {
            if (_isCancelled)
                yield break;

            float remainingCountdownSeconds = Math.Max(0f, getRemainingCountdownSeconds());
            float availableAnimationSeconds = Math.Max(
                0f,
                remainingCountdownSeconds -
                RouletteTiming.FinalWindowSeconds -
                RouletteTiming.CountdownSafetyMarginSeconds);

            RouletteAnimationPlan<EventBase> plan = RouletteAnimationPlan<EventBase>.Create(
                selectedEvent,
                eventOptions,
                availableAnimationSeconds);

            foreach (RouletteFrame<EventBase> frame in plan.Frames)
            {
                if (_isCancelled ||
                    !RouletteTiming.CanWaitBeforeCutoff(
                        getRemainingCountdownSeconds(),
                        frame.Delay.Seconds))
                {
                    break;
                }

                ShowEvent(frame.Value, isFinalResult: false);
                yield return Timing.WaitForSeconds(frame.Delay.Seconds);
            }

            if (_isCancelled)
                yield break;

            ShowEvent(plan.SelectedWinner, isFinalResult: true);
            onCompleted?.Invoke(plan.SelectedWinner);
        }
        finally
        {
            _isRunning = false;
            _rollHandle = default;
            _isCancelled = false;
        }
    }

    private void ShowEvent(EventBase eventInstance, bool isFinalResult)
    {
        _isVisible = true;
        _isFinalResult = isFinalResult;
        _displayedEvent = eventInstance;

        foreach (Player player in Player.List)
            ShowCurrent(player);
    }

    private void ClearDisplay()
    {
        _isVisible = false;
        _isFinalResult = false;
        _displayedEvent = null;

        HintManager? manager = global::MyFirstPlugin.MyFirstPlugin.Hints;
        if (manager == null)
            return;

        foreach (Player player in Player.List)
        {
            manager.Remove(player, HintElementId.LobbyEventName);
            manager.Remove(player, HintElementId.LobbyEventHeader);
        }
    }
}

public class EventRollConfig
{
    public float HeaderVerticalPosition { get; set; } = 250f;

    public float EventNameVerticalPosition { get; set; } = 205f;
}
