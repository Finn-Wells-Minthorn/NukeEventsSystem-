using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hints;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.CustomHandlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;

namespace MyFirstPlugin.Hints;

internal sealed class HintManager : CustomEventsHandler
{
    private const string HarmonyId = "com.nukeevents.internal-hints";
    private const float PersistentHintDurationSeconds = 999999f;
    private const float EmptyHintDurationSeconds = 0.1f;
    private const float MinimumExternalHintDurationSeconds = 0.05f;
    private const float ExternalHintRestorePaddingSeconds = 0.15f;

    // Native hints can leave the client's shared hint alpha at zero when their
    // fade effect ends. RueI resets it with a constant curve on every owned
    // render so a restored hint is visible instead of merely being resent.
    private static readonly HintEffect[] OwnedHintEffects =
    {
        new AlphaCurveHintEffect(
            UnityEngine.AnimationCurve.Constant(0f, PersistentHintDurationSeconds, 1f)),
    };

    private readonly Harmony _harmony = new(HarmonyId);
    private readonly HintStateRegistry _states = new();
    private readonly Dictionary<uint, PendingRestore> _pendingRestores = new();
    private bool _enabled;
    private bool _eventsRegistered;
    private bool _patchesApplied;
    private int _ownedSendDepth;

    public bool IsEnabled => _enabled;

    public void Enable()
    {
        if (_enabled)
            return;

        try
        {
            HintPatchBridge.Attach(this);
            CustomHandlersManager.RegisterEventsHandler(this);
            _eventsRegistered = true;

            _harmony.PatchAll(typeof(HintDisplayPatch).Assembly);
            _patchesApplied = true;
            VerifyPatchInstalled();

            _enabled = true;
            Logger.Info("[SCPEventSystem] Internal hint framework enabled.");
        }
        catch (Exception exception)
        {
            Logger.Error($"[SCPEventSystem] Internal hint framework failed to initialize: {exception}");
            RollBackInitialization();
            throw;
        }
    }

    public void Disable()
    {
        if (!_enabled && !_eventsRegistered && !_patchesApplied)
            return;

        _enabled = false;

        try
        {
            ClearAllCore(sendClear: true);
        }
        finally
        {
            try
            {
                if (_patchesApplied)
                    _harmony.UnpatchAll(HarmonyId);
            }
            finally
            {
                _patchesApplied = false;

                try
                {
                    if (_eventsRegistered)
                        CustomHandlersManager.UnregisterEventsHandler(this);
                }
                finally
                {
                    _eventsRegistered = false;
                    HintPatchBridge.Detach(this);
                }
            }
        }
    }

    public bool Set(
        Player player,
        HintElementId id,
        string content,
        float verticalPosition,
        HintAlignment alignment = HintAlignment.Center)
    {
        if (!_enabled || !CanReceiveHints(player))
            return false;

        HintElement element = new(id, content, verticalPosition, alignment);
        HintPlayerState state = _states.GetOrCreate(player.NetworkId);
        bool changed = state.Set(element);

        if (!state.IsExternalHintActive && (changed || !state.IsOwnedHintVisible))
            Render(player, state, force: !state.IsOwnedHintVisible);

        return changed;
    }

    public bool Remove(Player player, HintElementId id)
    {
        if (!_states.TryGet(player.NetworkId, out HintPlayerState state) || !state.Remove(id))
            return false;

        if (state.ElementCount == 0)
        {
            RemovePlayerState(player.NetworkId, player, sendClear: true);
            return true;
        }

        if (_enabled && !state.IsExternalHintActive && CanReceiveHints(player))
            Render(player, state, force: false);

        return true;
    }

    public void Clear(Player player) =>
        RemovePlayerState(player.NetworkId, player, sendClear: true);

    public void ClearAll() => ClearAllCore(sendClear: true);

    public void Refresh(Player player)
    {
        if (!_enabled || !CanReceiveHints(player) ||
            !_states.TryGet(player.NetworkId, out HintPlayerState state) ||
            state.IsExternalHintActive || state.ElementCount == 0)
        {
            return;
        }

        Render(player, state, force: true);
    }

    internal string GetDiagnosticStatus(Player player)
    {
        uint networkId = player.NetworkId;
        if (!_states.TryGet(networkId, out HintPlayerState state))
            return "No Nuke Events hint state is tracked for this player.";

        bool hasPendingRestore = _pendingRestores.TryGetValue(networkId, out PendingRestore pending);
        bool pendingHandleValid = hasPendingRestore && pending.Handle.IsValid;

        return $"Hint state: elements={state.ElementCount}, externalActive={state.IsExternalHintActive}, " +
               $"externalGeneration={state.ExternalHintGeneration}, pendingRestore={hasPendingRestore}, " +
               $"pendingHandleValid={pendingHandleValid}, ownedVisible={state.IsOwnedHintVisible}.";
    }

    internal void OnHintShown(Player player, Hint hint)
    {
        if (!_enabled || _ownedSendDepth > 0 || !CanReceiveHints(player))
            return;

        uint networkId = player.NetworkId;
        HintPlayerState state = _states.GetOrCreate(networkId);

        ulong fingerprint = CreateHintFingerprint(hint);
        if (state.IsSameExternalHint(fingerprint) && _pendingRestores.ContainsKey(networkId))
            return;

        CancelPendingRestore(networkId);

        int generation = state.BeginExternalHint(fingerprint);
        float durationSeconds = hint.DurationScalar;

        if (float.IsPositiveInfinity(durationSeconds))
            return;

        float safeDuration = float.IsNaN(durationSeconds) || durationSeconds < MinimumExternalHintDurationSeconds
            ? MinimumExternalHintDurationSeconds
            : durationSeconds;

        // Restoring at the exact advertised duration can race the client's
        // final expiry/clear frame and erase the replacement hint immediately.
        CoroutineHandle handle = Timing.CallDelayed(
            safeDuration + ExternalHintRestorePaddingSeconds,
            () => RestoreAfterExternalHint(networkId, state, generation));

        _pendingRestores[networkId] = new PendingRestore(state, generation, handle);
    }

    public override void OnPlayerLeft(PlayerLeftEventArgs ev) =>
        RemovePlayerState(ev.Player.NetworkId, player: null, sendClear: false);

    public override void OnServerRoundEnded(RoundEndedEventArgs ev) => ClearAllCore(sendClear: true);

    public override void OnServerRoundRestarted() => ClearAllCore(sendClear: true);

    public override void OnServerWaitingForPlayers() => ClearAllCore(sendClear: true);

    public override void OnServerShutdown() => ClearAllCore(sendClear: true);

    private static bool CanReceiveHints(Player? player) =>
        player != null && !player.IsDestroyed && !player.IsHost;

    private static ulong CreateHintFingerprint(Hint hint)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        string typeName = hint.GetType().FullName ?? hint.GetType().Name;

        foreach (char character in typeName)
        {
            hash ^= character;
            hash *= prime;
        }

        using NetworkWriterPooled writer = NetworkWriterPool.Get();
        hint.Serialize(writer);

        ArraySegment<byte> payload = writer.ToArraySegment();
        byte[] buffer = payload.Array ?? Array.Empty<byte>();
        int end = payload.Offset + payload.Count;

        for (int index = payload.Offset; index < end; ++index)
        {
            hash ^= buffer[index];
            hash *= prime;
        }

        return hash;
    }

    private void Render(Player player, HintPlayerState state, bool force)
    {
        string content = HintComposer.Compose(state.Elements);
        if (content.Length == 0)
            return;

        if (!force && state.IsOwnedHintVisible &&
            string.Equals(state.LastRenderedContent, content, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            SendOwnedHint(player, content, PersistentHintDurationSeconds);
            state.LastRenderedContent = content;
            state.IsOwnedHintVisible = true;
        }
        catch (Exception exception)
        {
            state.IsOwnedHintVisible = false;
            Logger.Error($"[SCPEventSystem] Failed to render an internal hint for player {player.PlayerId}: {exception}");
        }
    }

    private void SendOwnedHint(Player player, string content, float durationSeconds)
    {
        ++_ownedSendDepth;
        try
        {
            player.SendHint(content, OwnedHintEffects, durationSeconds);
        }
        finally
        {
            --_ownedSendDepth;
        }
    }

    private void RestoreAfterExternalHint(
        uint networkId,
        HintPlayerState expectedState,
        int generation)
    {
        if (!_enabled ||
            !_states.TryGet(networkId, out HintPlayerState currentState) ||
            !ReferenceEquals(currentState, expectedState) ||
            !_pendingRestores.TryGetValue(networkId, out PendingRestore pending) ||
            !pending.Matches(expectedState, generation))
        {
            return;
        }

        _pendingRestores.Remove(networkId);

        if (!currentState.CompleteExternalHint(generation))
            return;

        if (currentState.ElementCount == 0)
        {
            _states.Remove(networkId);
            return;
        }

        Player? player = Player.Get(networkId);
        if (!CanReceiveHints(player))
        {
            _states.Remove(networkId);
            return;
        }

        Render(player!, currentState, force: true);
    }

    private void RemovePlayerState(uint networkId, Player? player, bool sendClear)
    {
        CancelPendingRestore(networkId);

        if (!_states.TryGet(networkId, out HintPlayerState state))
            return;

        _states.Remove(networkId);
        state.CancelExternalHint();
        state.ClearElements();

        if (!sendClear || !state.IsOwnedHintVisible || !CanReceiveHints(player))
            return;

        TrySendClear(player!);
    }

    private void ClearAllCore(bool sendClear)
    {
        List<KeyValuePair<uint, HintPlayerState>> states = _states.Snapshot();

        foreach (KeyValuePair<uint, HintPlayerState> entry in states)
        {
            Player? player = sendClear ? Player.Get(entry.Key) : null;
            RemovePlayerState(entry.Key, player, sendClear);
        }

        foreach (uint networkId in _pendingRestores.Keys.ToList())
            CancelPendingRestore(networkId);

        _states.Clear();
    }

    private void TrySendClear(Player player)
    {
        try
        {
            SendOwnedHint(player, string.Empty, EmptyHintDurationSeconds);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[SCPEventSystem] Failed to clear an internal hint for player {player.PlayerId}: {exception}");
        }
    }

    private void CancelPendingRestore(uint networkId)
    {
        if (!_pendingRestores.TryGetValue(networkId, out PendingRestore pending))
            return;

        _pendingRestores.Remove(networkId);

        if (!pending.Handle.IsValid)
            return;

        try
        {
            Timing.KillCoroutines(pending.Handle);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[SCPEventSystem] Failed to cancel an internal hint restore callback: {exception}");
        }
    }

    private void VerifyPatchInstalled()
    {
        MethodInfo? target = AccessTools.Method(typeof(HintDisplay), nameof(HintDisplay.Show));
        Patches? patchInfo = target == null ? null : Harmony.GetPatchInfo(target);

        if (patchInfo == null || !patchInfo.Postfixes.Any(patch => patch.owner == HarmonyId))
            throw new InvalidOperationException("The native hint compatibility patch was not installed.");
    }

    private void RollBackInitialization()
    {
        _enabled = false;
        ClearAllCore(sendClear: false);

        try
        {
            _harmony.UnpatchAll(HarmonyId);
        }
        catch (Exception exception)
        {
            Logger.Error($"[SCPEventSystem] Failed to roll back internal hint patches: {exception}");
        }

        _patchesApplied = false;

        if (_eventsRegistered)
        {
            try
            {
                CustomHandlersManager.UnregisterEventsHandler(this);
            }
            catch (Exception exception)
            {
                Logger.Error($"[SCPEventSystem] Failed to roll back internal hint event handlers: {exception}");
            }
        }

        _eventsRegistered = false;
        HintPatchBridge.Detach(this);
    }

    private readonly struct PendingRestore
    {
        public PendingRestore(HintPlayerState state, int generation, CoroutineHandle handle)
        {
            State = state;
            Generation = generation;
            Handle = handle;
        }

        public HintPlayerState State { get; }

        public int Generation { get; }

        public CoroutineHandle Handle { get; }

        public bool Matches(HintPlayerState state, int generation) =>
            ReferenceEquals(State, state) && Generation == generation;
    }
}
