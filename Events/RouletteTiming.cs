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
    public const float DefaultDurationSeconds = 4.05f;
    public const float FinalWindowSeconds = 5f;
    public const float CountdownSafetyMarginSeconds = 1f;
    private const float MinimumSequenceDurationSeconds = 0.5f;

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

    public static IReadOnlyList<RouletteDelay> CreateSchedule(
        float configuredDurationSeconds,
        float availableAnimationSeconds)
    {
        if (float.IsNaN(availableAnimationSeconds) || availableAnimationSeconds <= 0f)
            return Array.Empty<RouletteDelay>();

        float requestedDurationSeconds = ResolveConfiguredDuration(configuredDurationSeconds);
        float targetDurationSeconds = Math.Min(requestedDurationSeconds, availableAnimationSeconds);
        if (targetDurationSeconds < MinimumSequenceDurationSeconds)
            return Array.Empty<RouletteDelay>();

        IReadOnlyList<RouletteDelay> template =
            targetDurationSeconds >= DefaultDurationSeconds
                ? FullSchedule
                : CompactSchedule;

        return ScaleSchedule(template, targetDurationSeconds);
    }

    public static float GetDuration(IEnumerable<RouletteDelay> schedule) =>
        schedule?.Sum(step => step.Seconds) ?? 0f;

    public static float GetAvailableAnimationSeconds(float remainingCountdownSeconds)
    {
        if (float.IsNaN(remainingCountdownSeconds))
            return 0f;

        return Math.Max(
            0f,
            remainingCountdownSeconds - FinalWindowSeconds - CountdownSafetyMarginSeconds);
    }

    public static bool CanWaitBeforeCutoff(
        float remainingCountdownSeconds,
        float nextDelaySeconds)
    {
        float protectedWindow = FinalWindowSeconds + CountdownSafetyMarginSeconds;
        return remainingCountdownSeconds - Math.Max(0f, nextDelaySeconds) > protectedWindow;
    }

    private static float ResolveConfiguredDuration(float configuredDurationSeconds)
    {
        if (float.IsNaN(configuredDurationSeconds) || float.IsInfinity(configuredDurationSeconds))
            return DefaultDurationSeconds;

        return Math.Max(0f, configuredDurationSeconds);
    }

    private static IReadOnlyList<RouletteDelay> ScaleSchedule(
        IReadOnlyList<RouletteDelay> template,
        float targetDurationSeconds)
    {
        float templateDurationSeconds = GetDuration(template);
        if (templateDurationSeconds <= 0f)
            return Array.Empty<RouletteDelay>();

        float scale = targetDurationSeconds / templateDurationSeconds;
        List<RouletteDelay> schedule = new(template.Count);

        for (int index = 0; index < template.Count; index++)
        {
            RouletteDelay delay = template[index];
            schedule.Add(new RouletteDelay(delay.Stage, delay.Seconds * scale));
        }

        return schedule;
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
        float configuredDurationSeconds,
        float availableAnimationSeconds)
    {
        if (rollingOptions == null)
            throw new ArgumentNullException(nameof(rollingOptions));

        IReadOnlyList<RouletteDelay> schedule = RouletteTiming.CreateSchedule(
            configuredDurationSeconds,
            availableAnimationSeconds);
        List<RouletteFrame<T>> frames = new(schedule.Count);

        for (int index = 0; index < schedule.Count && rollingOptions.Count > 0; index++)
        {
            T value = rollingOptions[index % rollingOptions.Count];
            frames.Add(new RouletteFrame<T>(value, schedule[index]));
        }

        return new RouletteAnimationPlan<T>(selectedWinner, frames);
    }
}
