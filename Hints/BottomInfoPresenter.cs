using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using MyFirstPlugin.Config;
using MyFirstPlugin.Events;

namespace MyFirstPlugin.Hints;

internal sealed class BottomInfoPresenter
{
    private readonly BottomInfoConfig _config;
    private readonly BottomWatermarkRenderer _renderer;
    private readonly BottomWatermarkAnimationState _animationState = new();
    private CoroutineHandle _animationHandle;
    private bool _isVisible;
    private float _phase;
    private string? _activeEventName;
    private string? _currentContent;

    public BottomInfoPresenter(BottomInfoConfig? config)
    {
        _config = config ?? new BottomInfoConfig();
        _renderer = new BottomWatermarkRenderer(
            _config.GradientEnabled,
            _config.GradientColors,
            _config.GradientAnimationSpeed,
            _config.GradientRefreshIntervalSeconds,
            _config.ServerInfoColor);
    }

    public bool IsRunning => _animationState.IsRunning;

    public bool ShowServerInfo() => Show(activeEventName: null);

    public bool ShowActiveEvent(EventBase eventInstance) =>
        eventInstance != null
            ? Show(eventInstance.DisplayName)
            : ShowServerInfo();

    public bool ShowCurrentEvent()
    {
        EventBase? currentEvent = EventManager.CurrentEvent;
        return currentEvent != null && currentEvent.IsRunning
            ? ShowActiveEvent(currentEvent)
            : ShowServerInfo();
    }

    public void Stop()
    {
        StopAnimation();
        _isVisible = false;
        _phase = 0f;
        _activeEventName = null;
        _currentContent = null;
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

    private bool Show(string? activeEventName)
    {
        if (!_config.Enabled)
        {
            Stop();
            return false;
        }

        _activeEventName = string.IsNullOrWhiteSpace(activeEventName)
            ? null
            : activeEventName!.Trim();
        _isVisible = true;
        RenderIfChanged();
        EnsureAnimationRunning();
        return true;
    }

    private void EnsureAnimationRunning()
    {
        if (!_renderer.CanAnimate)
        {
            StopAnimation();
            return;
        }

        if (!_animationState.TryStart(out int generation))
            return;

        _animationHandle = Timing.RunCoroutine(RunAnimation(generation));
    }

    private void StopAnimation()
    {
        _animationState.Stop();

        if (_animationHandle.IsValid)
            Timing.KillCoroutines(_animationHandle);

        _animationHandle = default;
    }

    private IEnumerator<float> RunAnimation(int generation)
    {
        try
        {
            while (_animationState.IsCurrent(generation))
            {
                yield return Timing.WaitForSeconds(_renderer.RefreshIntervalSeconds);

                if (!_animationState.IsCurrent(generation))
                    yield break;

                _phase = _renderer.AdvancePhase(_phase);
                RenderIfChanged();
            }
        }
        finally
        {
            if (_animationState.IsCurrent(generation))
            {
                _animationState.Stop();
                _animationHandle = default;
            }
        }
    }

    private void RenderIfChanged()
    {
        string content = _renderer.Format(
            _config.ServerInfoText,
            _activeEventName,
            _config.FontSize,
            _phase);

        if (string.Equals(content, _currentContent, System.StringComparison.Ordinal))
            return;

        _currentContent = content;

        foreach (Player player in Player.ReadyList)
            ShowCurrent(player);
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
