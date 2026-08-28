using System;
using System.Collections.Generic;
using System.Linq;

namespace MyFirstPlugin.Events;

internal enum RoulettePacingStage
{
    Fast,
    BriefSlowdown,
    SecondFast,
    FinalSlowdown
}

internal readonly struct RouletteDelay
{
    public RouletteDelay(RoulettePacingStage stage, float seconds)
    {
        Stage = stage;
        Seconds = seconds;
    }

    public RoulettePacingStage Stage { get; }

    public float Seconds { get; }
}

internal static class RouletteTiming
{
    public const float FinalWindowSeconds = 5f;
    public const float CountdownSafetyMarginSeconds = 1f;

    private static readonly IReadOnlyList<RouletteDelay> FullSchedule = new[]
    {
        new RouletteDelay(RoulettePacingStage.Fast, 0.08f),
        new RouletteDelay(RoulettePacingStage.Fast, 0.08f),
        new RouletteDelay(RoulettePacingStage.Fast, 0.08f),
        new RouletteDelay(RoulettePacingStage.Fast, 0.08f),
        new RouletteDelay(RoulettePacingStage.Fast, 0.08f),
        new RouletteDelay(RoulettePacingStage.Fast, 0.08f),
        new RouletteDelay(RoulettePacingStage.BriefSlowdown, 0.16f),
        new RouletteDelay(RoulettePacingStage.BriefSlowdown, 0.24f),
        new RouletteDelay(RoulettePacingStage.BriefSlowdown, 0.32f),
        new RouletteDelay(RoulettePacingStage.SecondFast, 0.08f),
        new RouletteDelay(RoulettePacingStage.SecondFast, 0.08f),
        new RouletteDelay(RoulettePacingStage.SecondFast, 0.08f),
        new RouletteDelay(RoulettePacingStage.SecondFast, 0.08f),
        new RouletteDelay(RoulettePacingStage.SecondFast, 0.08f),
        new RouletteDelay(RoulettePacingStage.FinalSlowdown, 0.14f),
        new RouletteDelay(RoulettePacingStage.FinalSlowdown, 0.22f),
        new RouletteDelay(RoulettePacingStage.FinalSlowdown, 0.32f),
        new RouletteDelay(RoulettePacingStage.FinalSlowdown, 0.44f),
        new RouletteDelay(RoulettePacingStage.FinalSlowdown, 0.58f),
        new RouletteDelay(RoulettePacingStage.FinalSlowdown, 0.75f)
    };

    private static readonly IReadOnlyList<RouletteDelay> CompactSchedule = new[]
    {
        new RouletteDelay(RoulettePacingStage.Fast, 0.08f),
        new RouletteDelay(RoulettePacingStage.Fast, 0.08f),
        new RouletteDelay(RoulettePacingStage.BriefSlowdown, 0.18f),
        new RouletteDelay(RoulettePacingStage.SecondFast, 0.08f),
        new RouletteDelay(RoulettePacingStage.SecondFast, 0.08f),
        new RouletteDelay(RoulettePacingStage.FinalSlowdown, 0.18f),
        new RouletteDelay(RoulettePacingStage.FinalSlowdown, 0.32f),
        new RouletteDelay(RoulettePacingStage.FinalSlowdown, 0.50f)
    };

    public static IReadOnlyList<RouletteDelay> CreateSchedule(float availableAnimationSeconds)
    {
        if (float.IsNaN(availableAnimationSeconds) || availableAnimationSeconds <= 0f)
            return Array.Empty<RouletteDelay>();

        if (GetDuration(FullSchedule) <= availableAnimationSeconds)
            return FullSchedule;

        if (GetDuration(CompactSchedule) <= availableAnimationSeconds)
            return CompactSchedule;

        return Array.Empty<RouletteDelay>();
    }

    public static float GetDuration(IEnumerable<RouletteDelay> schedule) =>
        schedule?.Sum(step => step.Seconds) ?? 0f;

    public static bool CanWaitBeforeCutoff(
        float remainingCountdownSeconds,
        float nextDelaySeconds)
    {
        float protectedWindow = FinalWindowSeconds + CountdownSafetyMarginSeconds;
        return remainingCountdownSeconds - Math.Max(0f, nextDelaySeconds) > protectedWindow;
    }
}

internal readonly struct RouletteFrame<T>
{
    public RouletteFrame(T value, RouletteDelay delay)
    {
        Value = value;
        Delay = delay;
    }

    public T Value { get; }

    public RouletteDelay Delay { get; }
}

internal sealed class RouletteAnimationPlan<T>
{
    private RouletteAnimationPlan(T selectedWinner, IReadOnlyList<RouletteFrame<T>> frames)
    {
        SelectedWinner = selectedWinner;
        Frames = frames;
    }

    public T SelectedWinner { get; }

    public IReadOnlyList<RouletteFrame<T>> Frames { get; }

    public float DurationSeconds => Frames.Sum(frame => frame.Delay.Seconds);

    public static RouletteAnimationPlan<T> Create(
        T selectedWinner,
        IReadOnlyList<T> rollingOptions,
        float availableAnimationSeconds)
    {
        if (rollingOptions == null)
            throw new ArgumentNullException(nameof(rollingOptions));

        IReadOnlyList<RouletteDelay> schedule = RouletteTiming.CreateSchedule(availableAnimationSeconds);
        List<RouletteFrame<T>> frames = new(schedule.Count);

        for (int index = 0; index < schedule.Count && rollingOptions.Count > 0; index++)
        {
            T value = rollingOptions[index % rollingOptions.Count];
            frames.Add(new RouletteFrame<T>(value, schedule[index]));
        }

        return new RouletteAnimationPlan<T>(selectedWinner, frames);
    }
}
