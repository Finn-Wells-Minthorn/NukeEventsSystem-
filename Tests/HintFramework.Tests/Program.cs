using System;
using MyFirstPlugin.Config;
using MyFirstPlugin.Events;
using MyFirstPlugin.Hints;

namespace MyFirstPlugin.Tests;

internal static class Program
{
    private static int _passed;
    private static int _run;

    private static int Main()
    {
        Run("add element", AddElement);
        Run("replace same tag", ReplaceSameTag);
        Run("two elements coexist", TwoElementsCoexist);
        Run("remove one keeps the other", RemoveOneKeepsOther);
        Run("clear one player", ClearOnePlayer);
        Run("global cleanup", GlobalCleanup);
        Run("duplicate state registration", DuplicateStateRegistration);
        Run("stale callback generation", StaleCallbackGeneration);
        Run("duplicate external hint detection", DuplicateExternalHintDetection);
        Run("composer positioning and formatting", ComposerPositioningAndFormatting);
        Run("roulette stable tagged replacement", RouletteStableTaggedReplacement);
        Run("roulette elements keep independent positions", RouletteElementsKeepIndependentPositions);
        Run("roulette cleanup", RouletteCleanup);
        Run("event color fallback", EventColorFallback);
        Run("configured event metadata retrieval", ConfiguredEventMetadataRetrieval);
        Run("roulette keeps preselected winner", RouletteKeepsPreselectedWinner);
        Run("roulette staged pacing", RouletteStagedPacing);
        Run("roulette final result formatting", RouletteFinalResultFormatting);
        Run("roulette final-five cutoff", RouletteFinalFiveCutoff);
        Run("bottom cycle ordering", BottomCycleOrdering);
        Run("bottom cycle skips unavailable event", BottomCycleSkipsUnavailableEvent);
        Run("tip rotation", TipRotation);
        Run("tips-disabled provider skipping", TipsDisabledProviderSkipping);
        Run("provider-specific durations", ProviderSpecificDurations);
        Run("bottom cycle lifecycle protection", BottomCycleLifecycleProtection);

        Console.WriteLine($"Hint framework tests passed: {_passed}/{_run}");
        return 0;
    }

    private static void Run(string name, Action test)
    {
        ++_run;

        try
        {
            test();
            ++_passed;
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {name}: {exception.Message}");
            Environment.Exit(1);
        }
    }

    private static void AddElement()
    {
        HintPlayerState state = new();
        Assert(state.Set(Element(HintElementId.LobbyEventHeader, "HEADER", 800f)), "First add should change state.");
        Assert(state.ElementCount == 1, "Expected one element.");
    }

    private static void ReplaceSameTag()
    {
        HintPlayerState state = new();
        Assert(state.Set(Element(HintElementId.LobbyEventName, "BLACKOUT", 740f)),
            "The initial tagged element should change state.");
        Assert(state.Set(Element(HintElementId.LobbyEventName, "ESCALATION", 740f)),
            "Changed content must request a fresh render.");
        Assert(!state.Set(Element(HintElementId.LobbyEventName, "ESCALATION", 740f)),
            "Identical content should not request an unnecessary render.");

        string content = HintComposer.Compose(state.Elements);
        Assert(state.ElementCount == 1, "Replacement must not add another slot.");
        Assert(content.Contains("ESCALATION"), "Replacement content was not rendered.");
        Assert(!content.Contains("BLACKOUT"), "Old replacement content remained.");
    }

    private static void TwoElementsCoexist()
    {
        HintPlayerState state = new();
        state.Set(Element(HintElementId.LobbyEventHeader, "EVENT SELECTING", 800f));
        state.Set(Element(HintElementId.LobbyEventName, "BLACKOUT", 740f));

        string content = HintComposer.Compose(state.Elements);
        Assert(state.ElementCount == 2, "Expected two independent slots.");
        Assert(content.Contains("EVENT SELECTING") && content.Contains("BLACKOUT"), "Both elements must be composed.");
    }

    private static void RemoveOneKeepsOther()
    {
        HintPlayerState state = new();
        state.Set(Element(HintElementId.LobbyEventHeader, "HEADER", 800f));
        state.Set(Element(HintElementId.LobbyEventName, "NAME", 740f));

        Assert(state.Remove(HintElementId.LobbyEventHeader), "Expected the first slot to be removed.");
        string content = HintComposer.Compose(state.Elements);
        Assert(state.ElementCount == 1, "Only one slot should remain.");
        Assert(!content.Contains("HEADER") && content.Contains("NAME"), "Removing one slot changed the wrong content.");
    }

    private static void ClearOnePlayer()
    {
        HintStateRegistry registry = new();
        registry.GetOrCreate(10).Set(Element(HintElementId.Tip, "TIP", 200f));
        registry.GetOrCreate(20).Set(Element(HintElementId.EventInfo, "EVENT", 300f));

        Assert(registry.Remove(10), "Disconnect/player clear should remove the tracked state.");
        Assert(registry.Count == 1 && registry.TryGet(20, out _), "Another player's state must remain.");
    }

    private static void GlobalCleanup()
    {
        HintStateRegistry registry = new();
        registry.GetOrCreate(10).Set(Element(HintElementId.Tip, "TIP", 200f));
        registry.GetOrCreate(20).Set(Element(HintElementId.EventInfo, "EVENT", 300f));

        registry.Clear();
        Assert(registry.Count == 0, "Round restart/plugin disable cleanup must clear every player.");
    }

    private static void DuplicateStateRegistration()
    {
        HintStateRegistry registry = new();
        HintPlayerState first = registry.GetOrCreate(42);
        HintPlayerState second = registry.GetOrCreate(42);

        Assert(ReferenceEquals(first, second), "A player must have only one state object.");
        Assert(registry.Count == 1, "Duplicate registration created extra state.");
    }

    private static void StaleCallbackGeneration()
    {
        HintPlayerState state = new();
        int firstGeneration = state.BeginExternalHint();
        int secondGeneration = state.BeginExternalHint();

        Assert(!state.CompleteExternalHint(firstGeneration), "An old restore callback must not complete.");
        Assert(state.IsExternalHintActive, "The current external hint must remain active.");
        Assert(state.CompleteExternalHint(secondGeneration), "The current restore callback should complete.");
    }

    private static void DuplicateExternalHintDetection()
    {
        HintPlayerState state = new();
        state.BeginExternalHint(100UL);

        Assert(state.IsSameExternalHint(100UL), "An identical active native hint should be detected.");
        Assert(!state.IsSameExternalHint(200UL), "A different native hint must not be treated as a duplicate.");

        state.CancelExternalHint();
        Assert(!state.IsSameExternalHint(100UL), "A completed or cancelled native hint must not suppress a later copy.");
    }

    private static void ComposerPositioningAndFormatting()
    {
        HintPlayerState state = new();
        state.Set(new HintElement(
            HintElementId.ServerInfo,
            "<color=red><size=24>SERVER</size></color>",
            650f,
            HintAlignment.Center));

        string content = HintComposer.Compose(state.Elements);
        Assert(content.Contains("<align=center>"), "Center alignment was not emitted.");
        Assert(content.Contains("<color=red><size=24>"), "Supported rich text was not preserved.");
        Assert(content.Contains("<line-height="), "A stable vertical line-height was not emitted.");
    }

    private static void RouletteStableTaggedReplacement()
    {
        HintPlayerState state = new();
        state.Set(Element(HintElementId.LobbyEventHeader, "Selecting Event...", 250f));
        state.Set(Element(HintElementId.LobbyEventName, "BLACKOUT", 205f));

        Assert(state.Set(Element(HintElementId.LobbyEventName, "ESCALATION", 205f)),
            "Changing the rolling event must request a render.");

        string content = HintComposer.Compose(state.Elements);
        Assert(state.ElementCount == 2, "Roulette updates must retain exactly two stable elements.");
        Assert(content.Contains("Selecting Event...") && content.Contains("ESCALATION"),
            "The header and latest event should coexist.");
        Assert(!content.Contains("BLACKOUT"), "The prior rolling event was not replaced.");
    }

    private static void RouletteCleanup()
    {
        HintPlayerState state = new();
        state.Set(Element(HintElementId.LobbyEventHeader, "Selecting Event...", 250f));
        state.Set(Element(HintElementId.LobbyEventName, "BLACKOUT", 205f));

        Assert(state.Remove(HintElementId.LobbyEventName), "The roulette event element should be removable.");
        Assert(state.Remove(HintElementId.LobbyEventHeader), "The roulette header should be removable.");
        Assert(state.ElementCount == 0, "Round-start cleanup should leave no roulette elements.");
    }

    private static void RouletteElementsKeepIndependentPositions()
    {
        HintPlayerState shortNameState = new();
        shortNameState.Set(Element(HintElementId.LobbyEventHeader, "Selecting Event...", 250f));
        shortNameState.Set(Element(HintElementId.LobbyEventName, "Infection", 205f));

        HintPlayerState formattedNameState = new();
        formattedNameState.Set(Element(HintElementId.LobbyEventHeader, "Selecting Event...", 250f));
        formattedNameState.Set(Element(
            HintElementId.LobbyEventName,
            "<color=#6699FF><b>Blackout Event</b></color>",
            205f));

        string shortNameContent = HintComposer.Compose(shortNameState.Elements);
        string formattedNameContent = HintComposer.Compose(formattedNameState.Elements);
        const string ExpectedInitialOffset = "<line-height=268.15>";

        Assert(shortNameContent.StartsWith(ExpectedInitialOffset, StringComparison.Ordinal),
            "The composer did not include the lower element in its cumulative positioning offset.");
        Assert(formattedNameContent.StartsWith(ExpectedInitialOffset, StringComparison.Ordinal),
            "Event-name formatting changed the independently positioned header offset.");
    }

    private static void EventColorFallback()
    {
        string fallback = HintUiFormatter.FormatEventName("BLACKOUT", null);
        string invalid = HintUiFormatter.FormatEventName("INFECTION", "not-a-color");
        string configured = HintUiFormatter.FormatEventName("ESCALATION", "#FF8C42");

        Assert(fallback.Contains($"<color={HintUiFormatter.DefaultEventColor}>"),
            "Events without a configured color should use the readable default.");
        Assert(invalid.Contains($"<color={HintUiFormatter.DefaultEventColor}>"),
            "Invalid configured colors should use the readable default.");
        Assert(configured.Contains("<color=#FF8C42>"), "Configured event colors should be preserved.");
    }

    private static void ConfiguredEventMetadataRetrieval()
    {
        EventDisplayConfig config = new()
        {
            Name = "Custom Blackout",
            Color = "invalid",
            Description = "A configurable short description."
        };

        EventDisplayMetadata metadata = EventDisplayMetadata.Resolve(config, "Blackout Event");
        Assert(metadata.Name == "Custom Blackout", "The configured event display name was not returned.");
        Assert(metadata.Description == "A configurable short description.",
            "The configured event description was not returned.");
        Assert(metadata.Color == HintUiFormatter.DefaultEventColor,
            "Invalid configured metadata colors should resolve to white.");
    }

    private static void RouletteKeepsPreselectedWinner()
    {
        const string PreselectedWinner = "Blackout Event";
        RouletteAnimationPlan<string> plan = RouletteAnimationPlan<string>.Create(
            PreselectedWinner,
            new[] { "Infection", "Escalation", PreselectedWinner, "Speed Demon" },
            10f);

        Assert(plan.Frames.Count > 0, "The full roulette plan should contain rolling frames.");
        Assert(plan.SelectedWinner == PreselectedWinner,
            "Presentation planning must retain the already selected winner.");
    }

    private static void RouletteFinalResultFormatting()
    {
        string rolling = HintUiFormatter.FormatEventName("Blackout Event", "#6699FF", bold: false);
        string final = HintUiFormatter.FormatEventName("Blackout Event", "#6699FF", bold: true);

        Assert(!rolling.Contains("<b>"), "Rolling entries should not be bold.");
        Assert(final.Contains("<b>Blackout Event</b>"), "The final selected event should be bold.");
        Assert(rolling.StartsWith("<nobr>", StringComparison.Ordinal) && final.EndsWith("</nobr>", StringComparison.Ordinal),
            "Roulette names should not wrap and move the independently positioned header.");
    }

    private static void RouletteStagedPacing()
    {
        RouletteAnimationPlan<string> plan = RouletteAnimationPlan<string>.Create(
            "Winner",
            new[] { "A", "B", "Winner" },
            10f);

        RoulettePacingStage[] stages = new RoulettePacingStage[plan.Frames.Count];
        for (int index = 0; index < plan.Frames.Count; index++)
            stages[index] = plan.Frames[index].Delay.Stage;

        int firstSlowdown = Array.IndexOf(stages, RoulettePacingStage.BriefSlowdown);
        int secondFast = Array.IndexOf(stages, RoulettePacingStage.SecondFast);
        int finalSlowdown = Array.IndexOf(stages, RoulettePacingStage.FinalSlowdown);

        Assert(firstSlowdown > 0 && secondFast > firstSlowdown && finalSlowdown > secondFast,
            "Roulette pacing must progress fast, slow, fast, then into the final slowdown.");

        float priorDelay = 0f;
        for (int index = finalSlowdown; index < plan.Frames.Count; index++)
        {
            float delay = plan.Frames[index].Delay.Seconds;
            Assert(delay > priorDelay, "Final slowdown delays should increase progressively.");
            priorDelay = delay;
        }
    }

    private static void RouletteFinalFiveCutoff()
    {
        const float CountdownSeconds = 10.2f;
        float available = CountdownSeconds -
            RouletteTiming.FinalWindowSeconds -
            RouletteTiming.CountdownSafetyMarginSeconds;

        RouletteAnimationPlan<string> plan = RouletteAnimationPlan<string>.Create(
            "Winner",
            new[] { "A", "B", "Winner" },
            available);

        Assert(plan.DurationSeconds <= available,
            "The selected roulette schedule exceeds the time budget before the final five seconds.");
        Assert(!RouletteTiming.CanWaitBeforeCutoff(6.5f, 0.6f),
            "A rolling frame that could cross the protected final window must be rejected.");

        RouletteAnimationPlan<string> latePlan = RouletteAnimationPlan<string>.Create(
            "Winner",
            new[] { "A", "B", "Winner" },
            0.5f);
        Assert(latePlan.Frames.Count == 0,
            "A late-starting countdown should degrade directly to the preselected final result.");
    }

    private static void BottomCycleOrdering()
    {
        BottomInfoCycle cycle = CreateBottomCycle(new[] { "First tip", "Second tip" });
        BottomInfoContext context = new("Blackout Event", "Facility lights are disabled.", "#6699FF");

        AssertNext(cycle, context, "NUKE EVENTS");
        AssertNext(cycle, context, "Blackout Event: Facility lights are disabled.");
        AssertNext(cycle, context, "TIP: First tip");
        AssertNext(cycle, context, "NUKE EVENTS");
    }

    private static void BottomCycleSkipsUnavailableEvent()
    {
        BottomInfoCycle cycle = CreateBottomCycle(new[] { "Only tip" });
        BottomInfoContext noEvent = default;

        AssertNext(cycle, noEvent, "NUKE EVENTS");
        AssertNext(cycle, noEvent, "TIP: Only tip");
        AssertNext(cycle, noEvent, "NUKE EVENTS");
    }

    private static void TipRotation()
    {
        BottomInfoCycle cycle = CreateBottomCycle(new[] { "First tip", "Second tip" });
        BottomInfoContext noEvent = default;

        AssertNext(cycle, noEvent, "NUKE EVENTS");
        AssertNext(cycle, noEvent, "TIP: First tip");
        AssertNext(cycle, noEvent, "NUKE EVENTS");
        AssertNext(cycle, noEvent, "TIP: Second tip");
        AssertNext(cycle, noEvent, "NUKE EVENTS");
        AssertNext(cycle, noEvent, "TIP: First tip");
    }

    private static void TipsDisabledProviderSkipping()
    {
        BottomInfoCycle cycle = new(new IBottomInfoProvider[]
        {
            new ServerInfoProvider(true, "NUKE EVENTS", null, 60f),
            new EventDetailsProvider(true, 45f),
            new TipProvider(false, new[] { "Hidden tip" }, null, 45f)
        });
        cycle.Reset();

        BottomInfoContext context = new("Infection", "Configured infection description.", "#66FF66");
        AssertNext(cycle, context, "NUKE EVENTS");
        AssertNext(cycle, context, "Infection: Configured infection description.");
        AssertNext(cycle, context, "NUKE EVENTS");
    }

    private static void ProviderSpecificDurations()
    {
        BottomInfoCycle cycle = CreateBottomCycle(new[] { "One tip" });
        BottomInfoContext context = new("Escalation", "Configured escalation description.", "#FF8C42");

        AssertNext(cycle, context, "NUKE EVENTS", 60f);
        AssertNext(cycle, context, "Escalation: Configured escalation description.", 45f);
        AssertNext(cycle, context, "TIP: One tip", 45f);
    }

    private static void BottomCycleLifecycleProtection()
    {
        BottomInfoLoopState state = new();
        Assert(state.TryStart(out int firstGeneration), "The first cycle loop should start.");
        Assert(!state.TryStart(out int duplicateGeneration), "A duplicate cycle loop must be rejected.");
        Assert(firstGeneration == duplicateGeneration, "Duplicate start should retain the active generation.");

        state.Stop();
        Assert(!state.IsCurrent(firstGeneration), "Stopping must invalidate stale callbacks.");
        Assert(state.TryStart(out int secondGeneration), "The cycle should start cleanly next round.");
        Assert(secondGeneration != firstGeneration, "A restarted cycle requires a fresh generation.");
    }

    private static BottomInfoCycle CreateBottomCycle(string[] tips)
    {
        BottomInfoCycle cycle = new(new IBottomInfoProvider[]
        {
            new ServerInfoProvider(true, "NUKE EVENTS", null, 60f),
            new EventDetailsProvider(true, 45f),
            new TipProvider(true, tips, null, 45f)
        });
        cycle.Reset();
        return cycle;
    }

    private static void AssertNext(BottomInfoCycle cycle, BottomInfoContext context, string expected)
    {
        AssertNext(cycle, context, expected, expectedDuration: null);
    }

    private static void AssertNext(
        BottomInfoCycle cycle,
        BottomInfoContext context,
        string expected,
        float? expectedDuration)
    {
        Assert(cycle.TryGetNext(context, out BottomInfoContent content), "Expected another bottom-cycle entry.");
        Assert(content.Text == expected, $"Expected '{expected}' but received '{content.Text}'.");

        if (expectedDuration.HasValue)
        {
            Assert(Math.Abs(content.DurationSeconds - expectedDuration.Value) < 0.001f,
                $"Expected duration '{expectedDuration.Value}' but received '{content.DurationSeconds}'.");
        }
    }

    private static HintElement Element(HintElementId id, string content, float position) =>
        new(id, content, position);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
