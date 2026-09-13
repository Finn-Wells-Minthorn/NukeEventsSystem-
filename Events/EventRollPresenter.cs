using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using MEC;
using MyFirstPlugin.Config;
using MyFirstPlugin.Hints;

namespace MyFirstPlugin.Events;

public sealed class EventRollPresenter
{
    private readonly EventRollConfig _config;
    private CoroutineHandle _rollHandle;
    private bool _isCancelled;
    private bool _isRunning;
    private bool _isVisible;
    private EventSelectionOption? _displayedOption;

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
        _displayedOption = null;

        foreach (Player player in Player.ReadyList)
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

        if (_displayedOption == null)
        {
            manager.Remove(player, HintElementId.LobbyEventName);
            return;
        }

        manager.Set(
            player,
            HintElementId.LobbyEventName,
            HintUiFormatter.FormatEventName(
                _displayedOption.DisplayName,
                _displayedOption.DisplayColor,
                bold: true),
            _config.EventNameVerticalPosition);
    }

    public void Start(
        EventSelectionOption selectedOption,
        IReadOnlyList<EventSelectionOption> availableOptions,
        Func<float> getRemainingCountdownSeconds,
        Action<EventSelectionOption>? onCompleted)
    {
        if (selectedOption == null)
            throw new ArgumentNullException(nameof(selectedOption));

        if (availableOptions == null)
            throw new ArgumentNullException(nameof(availableOptions));

        if (getRemainingCountdownSeconds == null)
            throw new ArgumentNullException(nameof(getRemainingCountdownSeconds));

        CancelRoll();

        List<EventSelectionOption> options = availableOptions
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Identity))
            .GroupBy(x => x.Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (options.Count == 0)
        {
            _isRunning = false;
            ShowOption(selectedOption);
            onCompleted?.Invoke(selectedOption);
            return;
        }

        if (!options.Any(x => OptionIdentityComparer.Instance.Equals(x, selectedOption)))
            options.Add(selectedOption);

        _isCancelled = false;
        _isRunning = true;
        _rollHandle = Timing.RunCoroutine(
            RunRoll(selectedOption, options, getRemainingCountdownSeconds, onCompleted));
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
        EventSelectionOption selectedOption,
        List<EventSelectionOption> options,
        Func<float> getRemainingCountdownSeconds,
        Action<EventSelectionOption>? onCompleted)
    {
        try
        {
            if (_isCancelled)
                yield break;

            float remainingCountdownSeconds = Math.Max(0f, getRemainingCountdownSeconds());
            float availableAnimationSeconds =
                RouletteTiming.GetAvailableAnimationSeconds(remainingCountdownSeconds);

            RouletteAnimationPlan<EventSelectionOption> plan =
                RouletteAnimationPlan<EventSelectionOption>.Create(
                    selectedOption,
                    options,
                    _config.TotalDurationSeconds,
                    availableAnimationSeconds,
                    OptionIdentityComparer.Instance);

            foreach (RouletteFrame<EventSelectionOption> frame in plan.Frames)
            {
                if (_isCancelled ||
                    !RouletteTiming.CanWaitBeforeCutoff(
                        getRemainingCountdownSeconds(),
                        frame.Delay.Seconds))
                {
                    break;
                }

                ShowOption(frame.Value);
                yield return Timing.WaitForSeconds(frame.Delay.Seconds);
            }

            if (_isCancelled)
                yield break;

            ShowOption(plan.SelectedWinner);
            onCompleted?.Invoke(plan.SelectedWinner);
        }
        finally
        {
            _isRunning = false;
            _rollHandle = default;
            _isCancelled = false;
        }
    }

    private void ShowOption(EventSelectionOption option)
    {
        _isVisible = true;
        _displayedOption = option;

        foreach (Player player in Player.ReadyList)
            ShowCurrent(player);
    }

    private void ClearDisplay()
    {
        _isVisible = false;
        _displayedOption = null;

        HintManager? manager = global::MyFirstPlugin.MyFirstPlugin.Hints;
        if (manager == null)
            return;

        foreach (Player player in Player.List)
        {
            manager.Remove(player, HintElementId.LobbyEventName);
            manager.Remove(player, HintElementId.LobbyEventHeader);
        }
    }

    private sealed class OptionIdentityComparer : IEqualityComparer<EventSelectionOption>
    {
        public static readonly OptionIdentityComparer Instance = new();

        public bool Equals(EventSelectionOption? first, EventSelectionOption? second)
        {
            if (ReferenceEquals(first, second))
                return true;

            if (first == null || second == null)
                return false;

            return string.Equals(first.Identity, second.Identity, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(EventSelectionOption option) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(option.Identity);
    }
}
