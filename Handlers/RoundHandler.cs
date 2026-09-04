using System;
using System.Collections.Generic;
using GameCore;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.CustomHandlers;
using LabApi.Features.Console;
using MEC;
using MyFirstPlugin.Config;
using MyFirstPlugin.Events;
using MyFirstPlugin.Hints;

namespace MyFirstPlugin.Handlers;

public class RoundHandler : CustomEventsHandler
{
    private EventSelector? _eventSelector;
    private BottomInfoPresenter? _bottomInfoPresenter;
    private EventRollPresenter? _eventRollPresenter;
    private CoroutineHandle _serverInfoRestoreHandle;
    private int _serverInfoRestoreGeneration;
    private CoroutineHandle _countdownWatcherHandle;
    private EventSelectionOption? _pendingSelection;
    private bool _isActive;

    private EventSelector EventSelector =>
        _eventSelector ??= new EventSelector(
            global::MyFirstPlugin.MyFirstPlugin.Instance?.Config?.NormalRound ?? new NormalRoundConfig());

    private EventRollPresenter EventRollPresenter =>
        _eventRollPresenter ??= new EventRollPresenter(
            global::MyFirstPlugin.MyFirstPlugin.Instance?.Config?.EventRoll ?? new EventRollConfig());

    private BottomInfoPresenter BottomInfoPresenter =>
        _bottomInfoPresenter ??= new BottomInfoPresenter(
            global::MyFirstPlugin.MyFirstPlugin.Instance?.Config?.BottomInfo ?? new BottomInfoConfig());

    public void Activate()
    {
        CancelServerInfoRestore();
        CancelPendingSelection();
        _eventSelector = new EventSelector(
            global::MyFirstPlugin.MyFirstPlugin.Instance?.Config?.NormalRound ?? new NormalRoundConfig());
        _bottomInfoPresenter?.Stop();
        EventManager.EventStarting -= OnEventStarting;
        EventManager.EventStarting += OnEventStarting;
        EventManager.EventStarted -= OnEventStarted;
        EventManager.EventStarted += OnEventStarted;
        EventManager.EventStopped -= OnEventStopped;
        EventManager.EventStopped += OnEventStopped;
        _isActive = true;
    }

    public void Deactivate()
    {
        _isActive = false;
        EventManager.EventStarting -= OnEventStarting;
        EventManager.EventStarted -= OnEventStarted;
        EventManager.EventStopped -= OnEventStopped;
        CancelServerInfoRestore();
        CancelPendingSelection();
        _bottomInfoPresenter?.Stop();
        _eventSelector = null;
    }

    private void CancelPendingSelection()
    {
        CancelCountdownWatcher();
        _eventRollPresenter?.Cancel();
        _pendingSelection = null;
    }

    private void CancelCountdownWatcher()
    {
        if (_countdownWatcherHandle.IsValid)
            Timing.KillCoroutines(_countdownWatcherHandle);

        _countdownWatcherHandle = default;
    }

    private void ScheduleServerInfoRestore()
    {
        CancelServerInfoRestore();
        int generation = ++_serverInfoRestoreGeneration;
        _serverInfoRestoreHandle = Timing.RunCoroutine(
            RestoreServerInfoAfterLifecycleCleanup(generation));
    }

    private void CancelServerInfoRestore()
    {
        ++_serverInfoRestoreGeneration;

        if (_serverInfoRestoreHandle.IsValid)
            Timing.KillCoroutines(_serverInfoRestoreHandle);

        _serverInfoRestoreHandle = default;
    }

    private IEnumerator<float> RestoreServerInfoAfterLifecycleCleanup(int generation)
    {
        try
        {
            // HintManager clears all owned elements on round/lobby lifecycle
            // events. Restore the persistent server entry after that cleanup.
            yield return Timing.WaitForSeconds(0.1f);

            if (_isActive && generation == _serverInfoRestoreGeneration)
                BottomInfoPresenter.ShowServerInfo();
        }
        finally
        {
            if (generation == _serverInfoRestoreGeneration)
                _serverInfoRestoreHandle = default;
        }
    }

    private void OnEventStarting(EventBase eventInstance)
    {
        if (!_isActive)
            return;

        CancelPendingSelection();
    }

    private void OnEventStarted(EventBase eventInstance)
    {
        if (_isActive)
            BottomInfoPresenter.ShowActiveEvent(eventInstance);
    }

    private void OnEventStopped(EventBase eventInstance)
    {
        if (_isActive)
            BottomInfoPresenter.ShowServerInfo();
    }

    public override void OnServerWaitingForPlayers()
    {
        if (!_isActive)
            return;

        Logger.Info("[SCPEventSystem] Waiting for players.");
        _bottomInfoPresenter?.Stop();
        ScheduleServerInfoRestore();
        CancelPendingSelection();
        _countdownWatcherHandle = Timing.RunCoroutine(WatchForCountdown());
    }

    private IEnumerator<float> WatchForCountdown()
    {
        try
        {
            // WaitingForPlayers is raised immediately before the game's lobby
            // coroutine initializes its networked timer. Let that initialization
            // run before interpreting a non-negative timer as the countdown.
            yield return Timing.WaitForSeconds(0.1f);

            while (_isActive)
            {
                RoundStart? roundStart = RoundStart.singleton;
                if (roundStart != null && roundStart.Timer >= 0)
                {
                    SelectPendingEvent(showPresentation: true);
                    yield break;
                }

                yield return Timing.WaitForSeconds(0.1f);
            }
        }
        finally
        {
            _countdownWatcherHandle = default;
        }
    }

    private EventSelectionOption? SelectPendingEvent(bool showPresentation)
    {
        if (!_isActive || !global::MyFirstPlugin.MyFirstPlugin.AutomaticEventsEnabled)
            return null;

        if (EventManager.CurrentEvent != null)
            return null;

        if (_pendingSelection != null &&
            (_pendingSelection.IsNormalRound || _pendingSelection.Event?.IsEnabled == true))
        {
            return _pendingSelection;
        }

        _pendingSelection = null;

        EventSelectionOption? selectedOption = EventSelector.Select();
        if (selectedOption == null)
        {
            Logger.Warn("[SCPEventSystem] No enabled events are currently available.");
            return null;
        }

        _pendingSelection = selectedOption;
        Logger.Info($"[SCPEventSystem] Selection chosen for roll: {selectedOption.DisplayName}");

        if (!showPresentation)
            return selectedOption;

        IReadOnlyList<EventSelectionOption> availableOptions = EventSelector.GetRouletteOptions();
        if (availableOptions.Count == 0)
        {
            Logger.Warn("[SCPEventSystem] No enabled events are currently available for the roll.");
            return selectedOption;
        }

        EventRollPresenter.ShowHeader();
        EventRollPresenter.Start(
            selectedOption,
            availableOptions,
            GetRemainingPreRoundSeconds,
            presentedOption =>
            {
                if (!_isActive || _pendingSelection != presentedOption || EventManager.CurrentEvent != null)
                    return;

                Logger.Info($"[SCPEventSystem] Event roll completed: {presentedOption.DisplayName}");
            });

        return selectedOption;
    }

    public override void OnServerRoundStarting(RoundStartingEventArgs ev)
    {
        if (!_isActive)
            return;

        CancelServerInfoRestore();
        CancelCountdownWatcher();

        if (!global::MyFirstPlugin.MyFirstPlugin.AutomaticEventsEnabled || EventManager.CurrentEvent != null)
        {
            CancelPendingSelection();
            return;
        }

        // A forced or immediately-started round can skip the normal countdown.
        // Select synchronously so RoundStarted still has a predetermined winner.
        SelectPendingEvent(showPresentation: false);
    }

    public override void OnServerRoundStarted()
    {
        if (!_isActive)
            return;

        Logger.Info("[SCPEventSystem] Round started.");

        CancelServerInfoRestore();
        CancelCountdownWatcher();
        _eventRollPresenter?.Cancel();
        BottomInfoPresenter.ShowCurrentEvent();

        if (!global::MyFirstPlugin.MyFirstPlugin.AutomaticEventsEnabled)
        {
            _pendingSelection = null;
            Logger.Info("[SCPEventSystem] Automatic events are disabled; skipping auto-selection.");
            return;
        }

        if (EventManager.CurrentEvent != null)
        {
            _pendingSelection = null;
            Logger.Info("[SCPEventSystem] An event is already active for this round; skipping auto-selection.");
            return;
        }

        EventSelectionOption? selectedOption = _pendingSelection;
        _pendingSelection = null;

        if (selectedOption == null)
        {
            Logger.Warn("[SCPEventSystem] No pending selection was available when the round started.");
            return;
        }

        if (selectedOption.IsNormalRound)
        {
            BottomInfoPresenter.ShowServerInfo();
            Logger.Info("[SCPEventSystem] Normal round selected; no event will be started.");
            return;
        }

        EventBase? selectedEvent = selectedOption.Event;
        if (selectedEvent == null)
        {
            BottomInfoPresenter.ShowCurrentEvent();
            Logger.Warn("[SCPEventSystem] Selected outcome did not contain an event.");
            return;
        }

        EventBase? launchedEvent = EventManager.StartEvent(selectedEvent);
        if (launchedEvent == null)
        {
            BottomInfoPresenter.ShowCurrentEvent();
            Logger.Warn($"[SCPEventSystem] Failed to start selected event: {selectedEvent.Name}");
            return;
        }

        Logger.Info($"[SCPEventSystem] Selected event: {launchedEvent.Name} - {launchedEvent.Description}");
    }

    public override void OnServerRoundEnded(RoundEndedEventArgs ev)
    {
        Logger.Info("[SCPEventSystem] Round ended.");
        _bottomInfoPresenter?.Stop();
        CancelPendingSelection();
        EventManager.StopCurrentEvent();
        ScheduleServerInfoRestore();
    }

    public override void OnServerRoundRestarted()
    {
        Logger.Info("[SCPEventSystem] Round restarting.");
        _bottomInfoPresenter?.Stop();
        CancelPendingSelection();
        EventManager.StopCurrentEvent();
        ScheduleServerInfoRestore();
    }

    public override void OnPlayerJoined(PlayerJoinedEventArgs ev)
    {
        if (!_isActive)
            return;

        _eventRollPresenter?.ShowCurrent(ev.Player);
        _bottomInfoPresenter?.ShowCurrent(ev.Player);
    }

    private static float GetRemainingPreRoundSeconds()
    {
        RoundStart? roundStart = RoundStart.singleton;
        return roundStart == null ? 0f : Math.Max(0f, roundStart.Timer);
    }
}
