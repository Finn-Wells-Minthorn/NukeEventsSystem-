using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using MyFirstPlugin.Config;
using MyFirstPlugin.Events;

namespace MyFirstPlugin.Hints;

internal sealed class BottomInfoPresenter
{
    private const float MinimumCycleIntervalSeconds = 1f;

    private readonly BottomInfoConfig _config;
    private readonly ServerInfoProvider _serverInfoProvider;
    private readonly BottomInfoCycle _cycle;
    private readonly BottomInfoLoopState _loopState = new();
    private CoroutineHandle _cycleHandle;
    private bool _isVisible;
    private string? _currentContent;
    private float _currentDurationSeconds;

    public BottomInfoPresenter(BottomInfoConfig? config)
    {
        _config = config ?? new BottomInfoConfig();
        _serverInfoProvider = new ServerInfoProvider(
            _config.ShowServerInfo,
            _config.ServerInfoText,
            _config.ServerInfoColor,
            _config.ServerInfoDurationSeconds);
        _cycle = new BottomInfoCycle(new IBottomInfoProvider[]
        {
            _serverInfoProvider,
            new EventDetailsProvider(
                _config.ShowEventDetails,
                _config.EventDetailsDurationSeconds),
            new TipProvider(
                _config.TipsEnabled,
                _config.Tips,
                _config.TipColor,
                _config.TipDurationSeconds)
        });
    }

    public bool IsRunning => _loopState.IsRunning;

    public bool Start()
    {
        if (!_config.Enabled || !_loopState.TryStart(out int generation))
            return false;

        _cycle.Reset();
        if (!ShowNext())
        {
            _loopState.Stop();
            return false;
        }

        _cycleHandle = Timing.RunCoroutine(RunCycle(generation));
        return true;
    }

    public bool ShowServerInfo()
    {
        StopCycle();

        if (!_config.Enabled ||
            !_serverInfoProvider.TryGetContent(default, out BottomInfoContent content))
        {
            ClearDisplay();
            return false;
        }

        ShowContent(content);
        return true;
    }

    public void Stop()
    {
        StopCycle();
        ClearDisplay();
    }

    private void StopCycle()
    {
        _loopState.Stop();

        if (_cycleHandle.IsValid)
            Timing.KillCoroutines(_cycleHandle);

        _cycleHandle = default;
    }

    private void ClearDisplay()
    {
        _isVisible = false;
        _currentContent = null;
        _currentDurationSeconds = 0f;
        RemoveFromAllPlayers();
    }

    public void ShowCurrent(Player player)
    {
        if (!_isVisible || string.IsNullOrEmpty(_currentContent))
            return;

        HintManager? manager = global::MyFirstPlugin.MyFirstPlugin.Hints;
        manager?.Set(
            player,
            HintElementId.BottomInfo,
            _currentContent!,
            _config.VerticalPosition);
    }

    private IEnumerator<float> RunCycle(int generation)
    {
        try
        {
            while (_loopState.IsCurrent(generation))
            {
                yield return Timing.WaitForSeconds(_currentDurationSeconds);

                if (!_loopState.IsCurrent(generation))
                    yield break;

                if (!ShowNext())
                    yield break;
            }
        }
        finally
        {
            if (_loopState.IsCurrent(generation))
            {
                _loopState.Stop();
                _cycleHandle = default;
            }
        }
    }

    private bool ShowNext()
    {
        if (!_cycle.TryGetNext(CreateContext(), out BottomInfoContent entry))
        {
            ClearDisplay();
            return false;
        }

        ShowContent(entry);
        return true;
    }

    private void ShowContent(BottomInfoContent content)
    {
        _currentContent = HintUiFormatter.FormatBottomText(
            content.Text,
            content.Color,
            _config.TextColor,
            _config.FontSize);
        _currentDurationSeconds = Math.Max(
            MinimumCycleIntervalSeconds,
            content.DurationSeconds);
        _isVisible = true;

        foreach (Player player in Player.List)
            ShowCurrent(player);
    }

    private static BottomInfoContext CreateContext()
    {
        EventBase? currentEvent = EventManager.CurrentEvent;
        if (currentEvent == null || !currentEvent.IsRunning)
            return default;

        return new BottomInfoContext(
            currentEvent.DisplayName,
            currentEvent.DisplayDescription,
            currentEvent.DisplayColor);
    }

    private static void RemoveFromAllPlayers()
    {
        HintManager? manager = global::MyFirstPlugin.MyFirstPlugin.Hints;
        if (manager == null)
            return;

        foreach (Player player in Player.List)
            manager.Remove(player, HintElementId.BottomInfo);
    }
}
