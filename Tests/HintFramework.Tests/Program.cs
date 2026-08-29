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
        Run("default event display-name casing", DefaultEventDisplayNameCasing);
        Run("persistent bottom watermark config", PersistentBottomWatermarkConfig);
        Run("gradient formatter produces rich text", GradientFormatterProducesRichText);
        Run("disabled gradient uses static fallback", DisabledGradientUsesStaticFallback);
        Run("invalid gradient falls back safely", InvalidGradientFallsBackSafely);
        Run("active event watermark stays white", ActiveEventWatermarkStaysWhite);
        Run("bottom watermark switches presentation", BottomWatermarkSwitchesPresentation);
        Run("custom event display-name casing is preserved", CustomEventDisplayNameCasingIsPreserved);
        Run("roulette shares configured event color", RouletteSharesConfiguredEventColor);
        Run("roulette keeps preselected winner", RouletteKeepsPreselectedWinner);
        Run("configurable roulette total duration", ConfigurableRouletteTotalDuration);
        Run("roulette staged pacing", RouletteStagedPacing);
        Run("roulette final result formatting", RouletteFinalResultFormatting);
        Run("roulette prevents consecutive duplicates", RoulettePreventsConsecutiveDuplicates);
        Run("single-event roulette is safe", SingleEventRouletteIsSafe);
        Run("roulette final-five cutoff", RouletteFinalFiveCutoff);
        Run("watermark animation lifecycle protection", WatermarkAnimationLifecycleProtection);

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

    private static void DefaultEventDisplayNameCasing()
    {
        Assert(DefaultEventDisplayNames.JailbirdMayhem == "jailbird mayhem",
            "Jailbird mayhem should use the requested lowercase default.");
        Assert(DefaultEventDisplayNames.SpeedDemon == "speed demon",
            "Speed demon should use the requested lowercase default.");
        Assert(DefaultEventDisplayNames.TimeToGamble == "time to gamble",
            "Time to gamble should use the requested lowercase default.");
        Assert(DefaultEventDisplayNames.Blackout == "blackout event",
            "Blackout event should use the requested lowercase default.");
        Assert(DefaultEventDisplayNames.Infection == "infection",
            "Infection should use the requested lowercase default.");
        Assert(DefaultEventDisplayNames.Escalation == "escalation",
            "Escalation should use the requested lowercase default.");
    }

    private static void PersistentBottomWatermarkConfig()
    {
        BottomInfoConfig config = new();

        Assert(Math.Abs(config.VerticalPosition - 2f) < 0.001f,
            "The configured live-test bottom position should remain the default.");
        Assert(config.FontSize == 18, "The bottom watermark font size should remain configurable.");
        Assert(config.ServerInfoText == "NUKE EVENTS",
            "The persistent watermark should use the configured server text.");
        Assert(config.GradientEnabled, "The moving watermark gradient should be enabled by default.");
        Assert(Math.Abs(config.GradientAnimationSpeed - 0.15f) < 0.001f,
            "The default gradient speed should be 0.15 cycles per second.");
        Assert(Math.Abs(config.GradientRefreshIntervalSeconds - 0.5f) < 0.001f,
            "The default gradient refresh interval should be conservative.");
        Assert(config.GradientColors.Count == 6,
            "The default moving gradient should contain six colors.");

        foreach (System.Reflection.PropertyInfo property in typeof(BottomInfoConfig).GetProperties())
        {
            string propertyName = property.Name;
            Assert(propertyName.IndexOf("Tip", StringComparison.OrdinalIgnoreCase) < 0 &&
                   propertyName.IndexOf("Duration", StringComparison.OrdinalIgnoreCase) < 0 &&
                   propertyName.IndexOf("EventDetails", StringComparison.OrdinalIgnoreCase) < 0,
                $"Obsolete bottom-cycle config remains: {propertyName}.");
        }
    }

    private static void GradientFormatterProducesRichText()
    {
        BottomWatermarkRenderer renderer = new(
            gradientEnabled: true,
            new[] { "#FF0000", "#0000FF" },
            animationSpeed: 0.25f,
            refreshIntervalSeconds: 0.5f,
            staticColor: "#FFFFFF");

        string firstFrame = renderer.Format("NUKE EVENTS", null, 18, phase: 0f);
        string secondFrame = renderer.Format("NUKE EVENTS", null, 18, phase: 0.25f);

        Assert(firstFrame.StartsWith("<size=18><nobr><b>", StringComparison.Ordinal),
            "The watermark did not emit its expected size, no-wrap, and bold tags.");
        Assert(CountOccurrences(firstFrame, "<color=#") == 10,
            "The gradient should distribute colors across all ten non-space letters.");
        Assert(CountOccurrences(firstFrame, "<color=") == CountOccurrences(firstFrame, "</color>"),
            "The gradient emitted unbalanced TMP color tags.");
        Assert(firstFrame.EndsWith("</b></nobr></size>", StringComparison.Ordinal),
            "The gradient formatter did not close its TMP tags.");
        Assert(firstFrame != secondFrame,
            "Changing gradient phase should produce a visibly different moving-gradient frame.");
    }

    private static void DisabledGradientUsesStaticFallback()
    {
        BottomWatermarkRenderer renderer = new(
            gradientEnabled: false,
            new[] { "#FF0000", "#0000FF" },
            animationSpeed: 0.25f,
            refreshIntervalSeconds: 0.5f,
            staticColor: "#12ab34");
        string content = renderer.Format("NUKE EVENTS", null, 18, phase: 0.5f);

        Assert(!renderer.CanAnimate, "A disabled gradient must not start an animation loop.");
        Assert(content.Contains("<b><color=#12AB34>NUKE EVENTS</color></b>"),
            "A disabled gradient did not use the configured static fallback color.");
    }

    private static void InvalidGradientFallsBackSafely()
    {
        BottomWatermarkRenderer renderer = new(
            gradientEnabled: true,
            new[] { "invalid", "", "#GGGGGG" },
            animationSpeed: float.NaN,
            refreshIntervalSeconds: 0.01f,
            staticColor: "invalid");
        string content = renderer.Format("NUKE EVENTS", null, 18, phase: float.NaN);

        Assert(renderer.UsedDefaultGradient,
            "An invalid gradient should fall back to the built-in readable palette.");
        Assert(renderer.CanAnimate,
            "The built-in fallback gradient should remain safely animatable.");
        Assert(renderer.RefreshIntervalSeconds >= BottomWatermarkRenderer.MinimumRefreshIntervalSeconds,
            "Unsafe refresh rates should be clamped.");
        Assert(CountOccurrences(content, "<color=#") == 10,
            "The fallback gradient should still format each visible letter.");
    }

    private static void ActiveEventWatermarkStaysWhite()
    {
        BottomWatermarkRenderer renderer = new(
            gradientEnabled: true,
            new[] { "#FF0000", "#0000FF" },
            animationSpeed: 0.25f,
            refreshIntervalSeconds: 0.5f,
            staticColor: "#FFFFFF");
        string content = renderer.Format("NUKE EVENTS", "jailbird mayhem", 18, phase: 0f);

        Assert(content.Contains("</b> <color=#FFFFFF>jailbird mayhem</color>"),
            "The active event name should be normal, white, and outside the bold server text.");
        Assert(!content.Contains("<b>jailbird mayhem</b>"),
            "The active event name must not inherit bold formatting.");
    }

    private static void BottomWatermarkSwitchesPresentation()
    {
        BottomWatermarkRenderer renderer = new(
            gradientEnabled: false,
            gradientColors: null,
            animationSpeed: 0f,
            refreshIntervalSeconds: 0.5f,
            staticColor: "#D9F2FF");

        string lobby = renderer.Format("NUKE EVENTS", null, 18, phase: 0f);
        string activeRound = renderer.Format("NUKE EVENTS", "blackout event", 18, phase: 0f);

        Assert(!lobby.Contains("#FFFFFF>blackout event"),
            "The waiting-lobby watermark should contain only server information.");
        Assert(activeRound.Contains("</b> <color=#FFFFFF>blackout event</color>"),
            "The active-round watermark did not append the configured event display name.");
        Assert(!activeRound.Contains(":"),
            "The persistent watermark should not insert a colon before the event name.");
    }

    private static void CustomEventDisplayNameCasingIsPreserved()
    {
        EventDisplayConfig config = new()
        {
            Name = "Custom SCP Event NAME",
            Color = "#123ABC",
            Description = "Custom description."
        };

        EventDisplayMetadata metadata = EventDisplayMetadata.Resolve(config, "Fallback event");
        string rouletteText = HintUiFormatter.FormatEventName(metadata.Name, metadata.Color);

        Assert(metadata.Name == "Custom SCP Event NAME",
            "Configured display-name capitalization must be preserved exactly.");
        Assert(rouletteText.Contains("Custom SCP Event NAME"),
            "Roulette formatting altered the configured display-name capitalization.");
    }

    private static void RouletteSharesConfiguredEventColor()
    {
        EventDisplayConfig config = new()
        {
            Name = "Configured event",
            Color = "#12ab34",
            Description = "Configured description."
        };
        EventDisplayMetadata metadata = EventDisplayMetadata.Resolve(config, "Fallback event");

        string rouletteText = HintUiFormatter.FormatEventName(metadata.Name, metadata.Color);

        Assert(metadata.Color == "#12AB34" && rouletteText.Contains("<color=#12AB34>"),
            "Roulette did not use the color resolved from shared event display metadata.");
        Assert(metadata.Description == "Configured description.",
            "Removing bottom event descriptions must not remove configured event metadata.");

        foreach (System.Reflection.PropertyInfo property in typeof(EventRollConfig).GetProperties())
        {
            Assert(property.Name.IndexOf("Color", StringComparison.OrdinalIgnoreCase) < 0,
                "EventRollConfig must not introduce a duplicate roulette-specific event color setting.");
        }
    }

    private static void RouletteKeepsPreselectedWinner()
    {
        const string PreselectedWinner = "Blackout Event";
        RouletteAnimationPlan<string> plan = RouletteAnimationPlan<string>.Create(
            PreselectedWinner,
            new[] { "Infection", "Escalation", PreselectedWinner, "Speed Demon" },
            RouletteTiming.DefaultDurationSeconds,
            10f);

        Assert(plan.Frames.Count > 0, "The full roulette plan should contain rolling frames.");
        Assert(plan.SelectedWinner == PreselectedWinner,
            "Presentation planning must retain the already selected winner.");
    }

    private static void ConfigurableRouletteTotalDuration()
    {
        EventRollConfig config = new();
        Assert(Math.Abs(config.TotalDurationSeconds - 4.05f) < 0.001f,
            "The default total roulette duration should match the current 4.05-second presentation.");

        RouletteAnimationPlan<string> shortPlan = RouletteAnimationPlan<string>.Create(
            "Winner",
            new[] { "A", "B", "Winner" },
            2.25f,
            20f);
        RouletteAnimationPlan<string> defaultPlan = RouletteAnimationPlan<string>.Create(
            "Winner",
            new[] { "A", "B", "Winner" },
            RouletteTiming.DefaultDurationSeconds,
            20f);
        RouletteAnimationPlan<string> doubledPlan = RouletteAnimationPlan<string>.Create(
            "Winner",
            new[] { "A", "B", "Winner" },
            RouletteTiming.DefaultDurationSeconds * 2f,
            20f);

        Assert(Math.Abs(shortPlan.DurationSeconds - 2.25f) < 0.001f,
            "A shorter configured roulette duration was not applied to the whole sequence.");
        Assert(Math.Abs(doubledPlan.DurationSeconds - (RouletteTiming.DefaultDurationSeconds * 2f)) < 0.001f,
            "A longer configured roulette duration was not applied to the whole sequence.");
        Assert(defaultPlan.Frames.Count == doubledPlan.Frames.Count,
            "Scaling the full roulette duration should retain the established pacing stages.");

        for (int index = 0; index < defaultPlan.Frames.Count; index++)
        {
            Assert(defaultPlan.Frames[index].Delay.Stage == doubledPlan.Frames[index].Delay.Stage,
                "Scaling changed a roulette pacing stage.");
            Assert(Math.Abs(
                    doubledPlan.Frames[index].Delay.Seconds -
                    (defaultPlan.Frames[index].Delay.Seconds * 2f)) < 0.001f,
                "Roulette stage delays were not scaled proportionally.");
        }
    }

    private static void RouletteFinalResultFormatting()
    {
        string rolling = HintUiFormatter.FormatEventName("Blackout Event", "#6699FF", bold: true);
        string final = HintUiFormatter.FormatEventName("Blackout Event", "#6699FF", bold: true);

        Assert(rolling.Contains("<b>Blackout Event</b>"), "Rolling event names should be bold.");
        Assert(final.Contains("<b>Blackout Event</b>"), "The final selected event should be bold.");
        Assert(rolling.Contains("<color=#6699FF>") && final.Contains("<color=#6699FF>"),
            "Rolling and final event names should retain the configured event color.");
        Assert(rolling.StartsWith("<nobr>", StringComparison.Ordinal) && final.EndsWith("</nobr>", StringComparison.Ordinal),
            "Roulette names should not wrap and move the independently positioned header.");
    }

    private static void RoulettePreventsConsecutiveDuplicates()
    {
        const string PreselectedWinner = "blackout event";
        RouletteAnimationPlan<string> plan = RouletteAnimationPlan<string>.Create(
            PreselectedWinner,
            new[]
            {
                "blackout event",
                "blackout event",
                "infection",
                "speed demon",
                "infection"
            },
            RouletteTiming.DefaultDurationSeconds,
            10f,
            StringComparer.OrdinalIgnoreCase);

        for (int index = 1; index < plan.Frames.Count; index++)
        {
            Assert(!string.Equals(
                    plan.Frames[index - 1].Value,
                    plan.Frames[index].Value,
                    StringComparison.OrdinalIgnoreCase),
                "Two consecutive rolling frames displayed the same event.");
        }

        Assert(plan.Frames.Count == 0 ||
               !string.Equals(
                   plan.Frames[plan.Frames.Count - 1].Value,
                   plan.SelectedWinner,
                   StringComparison.OrdinalIgnoreCase),
            "The last rolling frame should differ from the predetermined result when alternatives exist.");
        Assert(plan.SelectedWinner == PreselectedWinner,
            "Duplicate suppression must never replace the predetermined winner.");
    }

    private static void SingleEventRouletteIsSafe()
    {
        const string OnlyEvent = "infection";
        RouletteAnimationPlan<string> plan = RouletteAnimationPlan<string>.Create(
            OnlyEvent,
            new[] { OnlyEvent, OnlyEvent },
            RouletteTiming.DefaultDurationSeconds,
            10f,
            StringComparer.OrdinalIgnoreCase);

        Assert(plan.Frames.Count > 0, "A single eligible event should still produce a safe roll plan.");
        foreach (RouletteFrame<string> frame in plan.Frames)
            Assert(frame.Value == OnlyEvent, "A single-event roll introduced an unknown presentation value.");
        Assert(plan.SelectedWinner == OnlyEvent,
            "The single eligible event must remain the predetermined winner.");
    }

    private static void RouletteStagedPacing()
    {
        RouletteAnimationPlan<string> plan = RouletteAnimationPlan<string>.Create(
            "Winner",
            new[] { "A", "B", "Winner" },
            RouletteTiming.DefaultDurationSeconds,
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
        float available = RouletteTiming.GetAvailableAnimationSeconds(CountdownSeconds);

        RouletteAnimationPlan<string> plan = RouletteAnimationPlan<string>.Create(
            "Winner",
            new[] { "A", "B", "Winner" },
            20f,
            available);

        Assert(plan.DurationSeconds <= available,
            "The configured roulette duration was not clamped to the pre-round time budget.");
        Assert(Math.Abs(plan.DurationSeconds - available) < 0.001f,
            "The clamped roulette sequence should use the available presentation time.");
        Assert(!RouletteTiming.CanWaitBeforeCutoff(6.5f, 0.6f),
            "A rolling frame that could cross the protected final window must be rejected.");

        RouletteAnimationPlan<string> latePlan = RouletteAnimationPlan<string>.Create(
            "Winner",
            new[] { "A", "B", "Winner" },
            RouletteTiming.DefaultDurationSeconds,
            0.4f);
        Assert(latePlan.Frames.Count == 0,
            "A late-starting countdown should degrade directly to the preselected final result.");
    }

    private static void WatermarkAnimationLifecycleProtection()
    {
        BottomWatermarkAnimationState state = new();
        Assert(state.TryStart(out int firstGeneration), "The first gradient loop should start.");
        Assert(!state.TryStart(out int duplicateGeneration), "A duplicate gradient loop must be rejected.");
        Assert(firstGeneration == duplicateGeneration, "Duplicate start should retain the active generation.");

        state.Stop();
        Assert(!state.IsCurrent(firstGeneration), "Stopping must invalidate stale callbacks.");
        Assert(state.TryStart(out int secondGeneration), "The gradient should start cleanly next lifecycle.");
        Assert(secondGeneration != firstGeneration, "A restarted gradient requires a fresh generation.");
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            ++count;
            index += token.Length;
        }

        return count;
    }

    private static HintElement Element(HintElementId id, string content, float position) =>
        new(id, content, position);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
