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
    private EventBase? _displayedEvent;

    private const string HeaderText = "Selecting Event...";

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
            HintUiFormatter.FormatEventName(_displayedEvent.Name, _displayedEvent.DisplayColor),
            _config.EventNameVerticalPosition);
    }

    public void Start(EventBase selectedEvent, IReadOnlyList<EventBase> enabledEvents, Action<EventBase>? onCompleted)
    {
        if (selectedEvent == null)
            throw new ArgumentNullException(nameof(selectedEvent));

        if (enabledEvents == null)
            throw new ArgumentNullException(nameof(enabledEvents));

        CancelRoll();

        List<EventBase> eventOptions = enabledEvents
            .Where(x => x != null && x.IsEnabled && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (eventOptions.Count == 0)
        {
            _isRunning = false;
            ShowEvent(selectedEvent);
            onCompleted?.Invoke(selectedEvent);
            return;
        }

        if (!eventOptions.Any(x => string.Equals(x.Name, selectedEvent.Name, StringComparison.OrdinalIgnoreCase)))
        {
            eventOptions.Add(selectedEvent);
        }

        _isCancelled = false;
        _isRunning = true;
        _rollHandle = Timing.RunCoroutine(RunRoll(selectedEvent, eventOptions, onCompleted));
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

    private IEnumerator<float> RunRoll(EventBase selectedEvent, List<EventBase> eventOptions, Action<EventBase>? onCompleted)
    {
        try
        {
            if (_isCancelled)
                yield break;

            int winnerIndex = eventOptions.FindIndex(
                x => string.Equals(x.Name, selectedEvent.Name, StringComparison.OrdinalIgnoreCase));
            if (winnerIndex < 0)
                winnerIndex = 0;

            int currentIndex = 0;
            float interval = Math.Max(0.04f, _config.InitialIntervalSeconds);
            int stepCount = Math.Max(10, _config.RollIterationCount);

            for (int i = 0; i < stepCount && !_isCancelled; i++)
            {
                if (i >= Math.Max(5, stepCount - 6))
                {
                    currentIndex = winnerIndex;
                }
                else
                {
                    currentIndex = (currentIndex + 1) % eventOptions.Count;
                }

                ShowEvent(eventOptions[currentIndex]);
                yield return Timing.WaitForSeconds(interval);

                if (i < stepCount / 2)
                {
                    interval = Math.Min(_config.MaxIntervalSeconds, interval + 0.025f);
                }
                else
                {
                    interval = Math.Min(_config.MaxIntervalSeconds, interval + 0.05f);
                }
            }

            if (_isCancelled)
                yield break;

            ShowEvent(selectedEvent);
            yield return Timing.WaitForSeconds(0.2f);

            if (_isCancelled)
                yield break;

            yield return Timing.WaitForSeconds(Math.Max(0.25f, _config.FinalResultDisplaySeconds / 3f));

            if (_isCancelled)
                yield break;

            onCompleted?.Invoke(selectedEvent);
        }
        finally
        {
            _isRunning = false;
            _rollHandle = default;
            _isCancelled = false;
        }
    }

    private void ShowEvent(EventBase eventInstance)
    {
        _isVisible = true;
        _displayedEvent = eventInstance;

        foreach (Player player in Player.List)
            ShowCurrent(player);
    }

    private void ClearDisplay()
    {
        _isVisible = false;
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

    public float InitialIntervalSeconds { get; set; } = 0.06f;

    public float MaxIntervalSeconds { get; set; } = 0.5f;

    public ushort FinalResultDisplaySeconds { get; set; } = 1;

    public int RollIterationCount { get; set; } = 18;
}
