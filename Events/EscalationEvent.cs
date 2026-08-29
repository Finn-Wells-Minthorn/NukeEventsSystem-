using System;
using System.Collections.Generic;
using CustomPlayerEffects;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using MyFirstPlugin.Config;
using PlayerRoles;

namespace MyFirstPlugin.Events;

public sealed class EscalationEvent : EventBase
{
    private readonly EscalationEventConfig _config;
    private readonly Dictionary<uint, PlayerLifeState> _playerLives = new();
    private readonly Dictionary<uint, PendingCatchUp> _pendingCatchUps = new();
    private readonly List<Pickup> _overflowPickups = new();
    private CoroutineHandle _stageHandle;
    private int _currentStage;
    private bool _subscribed;

    public EscalationEvent(EscalationEventConfig? config = null)
    {
        _config = config ?? new EscalationEventConfig();

        if (!_config.Enabled)
            Disable();
    }

    public override string Name => "Escalation";

    protected override EventDisplayConfig? DisplayConfig => _config.Display;

    protected override void OnStart()
    {
        _currentStage = 0;
        Subscribe();

        foreach (Player player in Player.List)
            ApplyCurrentProgressionSafely(player, true);

        _stageHandle = Timing.RunCoroutine(RunStages());

        Console.WriteLine(
            $"[SCPEventSystem] Escalation activated: scpHealthMultiplier='{_config.ScpMaxHealthMultiplier}', " +
            $"scpDamageReductionIntensity='{_config.ScpDamageReductionIntensity}', " +
            $"stageTimes='{_config.StageOneTimeSeconds}/{_config.StageTwoTimeSeconds}/{_config.StageThreeTimeSeconds}/{_config.StageFourTimeSeconds}'."
        );
    }

    protected override void OnStop()
    {
        Unsubscribe();

        if (_stageHandle.IsValid)
            Timing.KillCoroutines(_stageHandle);

        _stageHandle = default;
        CancelAllPendingCatchUps();

        foreach (Player player in Player.List)
        {
            if (player == null || player.IsDestroyed)
                continue;

            if (!_playerLives.TryGetValue(player.NetworkId, out PlayerLifeState? state) ||
                state.LifeId != player.LifeId)
            {
                continue;
            }

            try
            {
                RestorePlayer(player, state);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[SCPEventSystem] Failed to restore Escalation state for '{player.Nickname}': {ex.Message}"
                );
            }
        }

        foreach (Pickup pickup in _overflowPickups)
        {
            try
            {
                if (pickup != null && !pickup.IsDestroyed)
                    pickup.Destroy();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SCPEventSystem] Failed to clean up an Escalation overflow pickup: {ex.Message}");
            }
        }

        _playerLives.Clear();
        _overflowPickups.Clear();
        _currentStage = 0;
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        PlayerEvents.Joined += OnPlayerJoined;
        PlayerEvents.Left += OnPlayerLeft;
        PlayerEvents.Spawned += OnPlayerSpawned;
        PlayerEvents.ChangedRole += OnPlayerChangedRole;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        PlayerEvents.Joined -= OnPlayerJoined;
        PlayerEvents.Left -= OnPlayerLeft;
        PlayerEvents.Spawned -= OnPlayerSpawned;
        PlayerEvents.ChangedRole -= OnPlayerChangedRole;
        _subscribed = false;
    }

    private void OnPlayerJoined(PlayerJoinedEventArgs args)
    {
        CancelPendingCatchUp(args.Player.NetworkId);
        _playerLives.Remove(args.Player.NetworkId);
    }

    private void OnPlayerLeft(PlayerLeftEventArgs args)
    {
        CancelPendingCatchUp(args.Player.NetworkId);
        _playerLives.Remove(args.Player.NetworkId);
    }

    private void OnPlayerSpawned(PlayerSpawnedEventArgs args)
    {
        ScheduleDelayedCatchUp(args.Player);
    }

    private void OnPlayerChangedRole(PlayerChangedRoleEventArgs args)
    {
        // Any callback queued for the previous role/life must not touch the new
        // role while its vanilla inventory and movement controller initialize.
        CancelPendingCatchUp(args.Player.NetworkId);
    }

    private IEnumerator<float> RunStages()
    {
        float stageOneTime = Math.Max(0f, _config.StageOneTimeSeconds);
        float stageTwoTime = Math.Max(stageOneTime, _config.StageTwoTimeSeconds);
        float stageThreeTime = Math.Max(stageTwoTime, _config.StageThreeTimeSeconds);
        float stageFourTime = Math.Max(stageThreeTime, _config.StageFourTimeSeconds);

        yield return Timing.WaitForSeconds(stageOneTime);
        AdvanceToStage(1, _config.StageOneAnnouncement);

        yield return Timing.WaitForSeconds(stageTwoTime - stageOneTime);
        AdvanceToStage(2, _config.StageTwoAnnouncement);

        yield return Timing.WaitForSeconds(stageThreeTime - stageTwoTime);
        AdvanceToStage(3, _config.StageThreeAnnouncement);

        yield return Timing.WaitForSeconds(stageFourTime - stageThreeTime);
        AdvanceToStage(4, _config.StageFourAnnouncement);

        _stageHandle = default;
    }

    private void AdvanceToStage(int stage, string announcement)
    {
        if (!IsRunning || stage <= _currentStage)
            return;

        _currentStage = stage;
        SendAnnouncement(announcement, _config.StageAnnouncementDurationSeconds);

        foreach (Player player in Player.List)
        {
            if (!HasPendingCatchUp(player))
                ApplyCurrentProgressionSafely(player, true);
        }

        Console.WriteLine($"[SCPEventSystem] Escalation advanced to stage {stage}.");
    }

    private void ApplyCurrentProgressionSafely(Player? player, bool grantSupplies)
    {
        try
        {
            ApplyCurrentProgression(player, grantSupplies);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCPEventSystem] Failed to apply Escalation progression to '{player?.Nickname ?? "unknown"}': {ex.Message}"
            );
        }
    }

    private void ApplyCurrentProgression(Player? player, bool grantSupplies)
    {
        if (player == null || player.IsDestroyed || !player.IsAlive)
            return;

        PlayerLifeState state = GetCurrentLifeState(player);

        // SCP-079 has no conventional health-based combat loop, so version one
        // deliberately leaves it completely unchanged.
        if (player.Role == RoleTypeId.Scp079)
            return;

        if (player.IsSCP)
        {
            ApplyMaximumHealth(player, state, Math.Max(1f, _config.ScpMaxHealthMultiplier));

            if (_config.ScpDamageReductionIntensity > 0 && !state.DamageReductionApplied)
            {
                state.DamageReduction = CaptureEffect<DamageReduction>(player);
                byte intensity = Math.Max(
                    state.DamageReduction.WasEnabled ? state.DamageReduction.Intensity : (byte)0,
                    _config.ScpDamageReductionIntensity
                );
                state.DamageReductionApplied = true;
                player.EnableEffect<DamageReduction>(intensity, 0f, false);
            }

            return;
        }

        if (!player.IsHuman)
            return;

        if (_currentStage >= 2)
        {
            ApplyMaximumHealth(
                player,
                state,
                Math.Max(1f, _config.HumanStageTwoMaxHealthMultiplier)
            );
        }

        if (_currentStage >= 4 &&
            _config.StageFourMovementBoostIntensity > 0 &&
            !state.MovementBoostApplied)
        {
            state.MovementBoost = CaptureEffect<MovementBoost>(player);
            byte intensity = Math.Max(
                state.MovementBoost.WasEnabled ? state.MovementBoost.Intensity : (byte)0,
                _config.StageFourMovementBoostIntensity
            );
            state.MovementBoostApplied = true;
            player.EnableEffect<MovementBoost>(intensity, 0f, false);
        }

        if (!grantSupplies)
            return;

        if (_currentStage >= 1 && !state.StageOneGranted)
        {
            GrantItems(player, _config.StageOneItems);
            state.StageOneGranted = true;
        }

        if (_currentStage >= 3 && !state.StageThreeGranted)
        {
            GrantItems(player, _config.StageThreeItems);

            if (_config.StageThreeAmmoType != ItemType.None && _config.StageThreeAmmoAmount > 0)
            {
                try
                {
                    player.AddAmmo(_config.StageThreeAmmoType, _config.StageThreeAmmoAmount);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[SCPEventSystem] Failed to grant Escalation ammunition to '{player.Nickname}': {ex.Message}"
                    );
                }
            }

            state.StageThreeGranted = true;
        }
    }

    private void ScheduleDelayedCatchUp(Player? player)
    {
        if (!IsRunning || player == null || player.IsDestroyed || !player.IsAlive)
            return;

        uint networkId = player.NetworkId;
        int lifeId = player.LifeId;

        if (_playerLives.TryGetValue(networkId, out PlayerLifeState? state) &&
            state.LifeId == lifeId &&
            state.CatchUpCompleted)
        {
            return;
        }

        if (_pendingCatchUps.TryGetValue(networkId, out PendingCatchUp pending))
        {
            if (pending.LifeId == lifeId)
                return;

            CancelPendingCatchUp(networkId);
        }

        float delaySeconds = Math.Max(0.1f, _config.RespawnCatchUpDelaySeconds);
        CoroutineHandle handle = Timing.CallDelayed(
            delaySeconds,
            () => CompleteDelayedCatchUp(networkId, lifeId)
        );

        _pendingCatchUps[networkId] = new PendingCatchUp(lifeId, handle);
    }

    private void CompleteDelayedCatchUp(uint networkId, int lifeId)
    {
        if (!_pendingCatchUps.TryGetValue(networkId, out PendingCatchUp pending) ||
            pending.LifeId != lifeId)
        {
            return;
        }

        _pendingCatchUps.Remove(networkId);

        if (!IsRunning)
            return;

        Player? player = FindPlayer(networkId);
        if (player == null || player.IsDestroyed || !player.IsAlive || player.LifeId != lifeId)
            return;

        PlayerLifeState state = GetCurrentLifeState(player);
        if (state.CatchUpCompleted)
            return;

        ApplyCurrentProgressionSafely(player, true);
        state.CatchUpCompleted = true;
    }

    private bool HasPendingCatchUp(Player? player)
    {
        return player != null &&
            _pendingCatchUps.TryGetValue(player.NetworkId, out PendingCatchUp pending) &&
            pending.LifeId == player.LifeId;
    }

    private void CancelPendingCatchUp(uint networkId)
    {
        if (!_pendingCatchUps.TryGetValue(networkId, out PendingCatchUp pending))
            return;

        _pendingCatchUps.Remove(networkId);

        if (pending.Handle.IsValid)
            Timing.KillCoroutines(pending.Handle);
    }

    private void CancelAllPendingCatchUps()
    {
        foreach (PendingCatchUp pending in _pendingCatchUps.Values)
        {
            if (pending.Handle.IsValid)
                Timing.KillCoroutines(pending.Handle);
        }

        _pendingCatchUps.Clear();
    }

    private static Player? FindPlayer(uint networkId)
    {
        foreach (Player player in Player.List)
        {
            if (player != null && player.NetworkId == networkId)
                return player;
        }

        return null;
    }

    private PlayerLifeState GetCurrentLifeState(Player player)
    {
        if (_playerLives.TryGetValue(player.NetworkId, out PlayerLifeState? state) &&
            state.LifeId == player.LifeId)
        {
            return state;
        }

        state = new PlayerLifeState(player.LifeId);
        _playerLives[player.NetworkId] = state;
        return state;
    }

    private static void ApplyMaximumHealth(Player player, PlayerLifeState state, float multiplier)
    {
        if (state.MaximumHealthApplied)
            return;

        float originalMaximumHealth = player.MaxHealth;
        if (originalMaximumHealth <= 0f)
            return;

        float newMaximumHealth = originalMaximumHealth * multiplier;
        float addedMaximumHealth = Math.Max(0f, newMaximumHealth - originalMaximumHealth);

        state.OriginalMaximumHealth = originalMaximumHealth;
        state.MaximumHealthApplied = true;

        player.MaxHealth = newMaximumHealth;
        player.Health = Math.Min(newMaximumHealth, player.Health + addedMaximumHealth);
    }

    private void GrantItems(Player player, IEnumerable<ItemType>? items)
    {
        if (items == null)
            return;

        foreach (ItemType itemType in items)
        {
            if (itemType == ItemType.None)
                continue;

            try
            {
                if (!player.IsInventoryFull && player.AddItem(itemType) != null)
                    continue;

                Pickup? pickup = Pickup.Create(itemType, player.Position);
                if (pickup == null || !pickup.IsSpawned)
                {
                    if (pickup != null && !pickup.IsDestroyed)
                        pickup.Destroy();

                    Console.WriteLine(
                        $"[SCPEventSystem] Failed to grant Escalation item '{itemType}' to '{player.Nickname}'."
                    );
                    continue;
                }

                _overflowPickups.Add(pickup);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[SCPEventSystem] Failed to grant Escalation item '{itemType}' to '{player.Nickname}': {ex.Message}"
                );
            }
        }
    }

    private static EffectState CaptureEffect<T>(Player player) where T : StatusEffectBase
    {
        T? effect = player.GetEffect<T>();
        bool wasEnabled = effect != null && effect.IsEnabled;

        return new EffectState(
            wasEnabled,
            wasEnabled ? effect!.Intensity : (byte)0,
            wasEnabled ? effect!.TimeLeft : 0f,
            wasEnabled && effect!.Duration <= 0f,
            DateTime.UtcNow
        );
    }

    private static void RestorePlayer(Player player, PlayerLifeState state)
    {
        if (state.MaximumHealthApplied)
        {
            player.MaxHealth = state.OriginalMaximumHealth;
            player.Health = Math.Min(player.Health, state.OriginalMaximumHealth);
        }

        if (state.DamageReductionApplied)
            RestoreEffect<DamageReduction>(player, state.DamageReduction);

        if (state.MovementBoostApplied)
            RestoreEffect<MovementBoost>(player, state.MovementBoost);
    }

    private static void RestoreEffect<T>(Player player, EffectState state) where T : StatusEffectBase
    {
        if (!state.WasEnabled)
        {
            player.DisableEffect<T>();
            return;
        }

        if (state.WasIndefinite)
        {
            player.EnableEffect<T>(state.Intensity, 0f, false);
            return;
        }

        float elapsedSeconds = (float)(DateTime.UtcNow - state.CapturedAtUtc).TotalSeconds;
        float remainingDuration = Math.Max(0f, state.TimeLeft - elapsedSeconds);
        if (remainingDuration > 0f)
            player.EnableEffect<T>(state.Intensity, remainingDuration, false);
        else
            player.DisableEffect<T>();
    }

    private static void SendAnnouncement(string announcement, ushort durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(announcement) || durationSeconds == 0)
            return;

        Server.SendBroadcast(announcement, durationSeconds);
    }

    private sealed class PlayerLifeState
    {
        public PlayerLifeState(int lifeId)
        {
            LifeId = lifeId;
        }

        public int LifeId { get; }

        public bool MaximumHealthApplied { get; set; }

        public float OriginalMaximumHealth { get; set; }

        public bool DamageReductionApplied { get; set; }

        public EffectState DamageReduction { get; set; }

        public bool MovementBoostApplied { get; set; }

        public EffectState MovementBoost { get; set; }

        public bool StageOneGranted { get; set; }

        public bool StageThreeGranted { get; set; }

        public bool CatchUpCompleted { get; set; }
    }

    private readonly struct PendingCatchUp
    {
        public PendingCatchUp(int lifeId, CoroutineHandle handle)
        {
            LifeId = lifeId;
            Handle = handle;
        }

        public int LifeId { get; }

        public CoroutineHandle Handle { get; }
    }

    private readonly struct EffectState
    {
        public EffectState(
            bool wasEnabled,
            byte intensity,
            float timeLeft,
            bool wasIndefinite,
            DateTime capturedAtUtc
        )
        {
            WasEnabled = wasEnabled;
            Intensity = intensity;
            TimeLeft = timeLeft;
            WasIndefinite = wasIndefinite;
            CapturedAtUtc = capturedAtUtc;
        }

        public bool WasEnabled { get; }

        public byte Intensity { get; }

        public float TimeLeft { get; }

        public bool WasIndefinite { get; }

        public DateTime CapturedAtUtc { get; }
    }
}
