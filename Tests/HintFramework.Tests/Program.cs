using System;
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
        Run("roulette cleanup", RouletteCleanup);
        Run("event color fallback", EventColorFallback);
        Run("bottom cycle ordering", BottomCycleOrdering);
        Run("bottom cycle skips unavailable event", BottomCycleSkipsUnavailableEvent);
        Run("tip rotation", TipRotation);
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

    private static void EventColorFallback()
    {
        string fallback = HintUiFormatter.FormatEventName("BLACKOUT", null);
        string configured = HintUiFormatter.FormatEventName("ESCALATION", "#FF8C42");

        Assert(fallback.Contains($"<color={HintUiFormatter.DefaultEventColor}>"),
            "Events without a configured color should use the readable default.");
        Assert(configured.Contains("<color=#FF8C42>"), "Configured event colors should be preserved.");
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
            new ServerInfoProvider(true, "NUKE EVENTS", null),
            new EventDetailsProvider(true),
            new TipProvider(true, tips, null)
        });
        cycle.Reset();
        return cycle;
    }

    private static void AssertNext(BottomInfoCycle cycle, BottomInfoContext context, string expected)
    {
        Assert(cycle.TryGetNext(context, out BottomInfoContent content), "Expected another bottom-cycle entry.");
        Assert(content.Text == expected, $"Expected '{expected}' but received '{content.Text}'.");
    }

    private static HintElement Element(HintElementId id, string content, float position) =>
        new(id, content, position);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
