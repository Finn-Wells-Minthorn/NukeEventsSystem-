using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using MyFirstPlugin.Config;
using PlayerRoles;
using PlayerRoles.RoleAssign;
using PlayerStatsSystem;
using UnityEngine;

namespace MyFirstPlugin.Events;

public sealed class InfectionEvent : EventBase
{
    private const float InitialNormalizationFallbackDelaySeconds = 0.1f;
    private const float MinimumSafeConversionDelaySeconds = 0.1f;
    private const float RoleStatApplicationDelaySeconds = 0.1f;
    private const int HardMaximumStartingDoctors = 3;

    private readonly InfectionEventConfig _config;
    private readonly Dictionary<uint, PendingConversion> _pendingConversions = new();
    private readonly Dictionary<uint, HashSet<int>> _handledDeathLives = new();
    private readonly Dictionary<uint, PendingRoleStatApplication> _pendingRoleStatApplications = new();
    private readonly Dictionary<uint, ModifiedRoleStats> _modifiedRoleStats = new();
    private CoroutineHandle _initialNormalizationHandle;
    private int _initialNormalizationGeneration;
    private int _nextConversionId;
    private bool _initialNormalizationCompleted;
    private bool _roleAssignmentSubscribed;
    private bool _subscribed;

    public InfectionEvent(InfectionEventConfig? config = null)
    {
        _config = config ?? new InfectionEventConfig();

        if (!_config.Enabled)
            Disable();
    }

    public override string Name => "Infection";

    public override string Description =>
        "Plague Doctors lead an SCP-049-2 horde whose kills convert human survivors into new zombies.";

    protected override void OnStart()
    {
        Subscribe();
        BeginInitialRoleNormalization();
        SendAnnouncement(_config.StartAnnouncement, _config.StartAnnouncementDurationSeconds);
    }

    protected override void OnStop()
    {
        CancelInitialRoleNormalization();
        Unsubscribe();
        CancelAllPendingConversions();
        CancelAllPendingRoleStatApplications();
        RestoreAllModifiedRoleStats();
        _handledDeathLives.Clear();
    }

    internal static int CalculateStartingDoctorCount(
        int playerCount,
        int twoDoctorMinimumPlayers,
        int threeDoctorMinimumPlayers,
        int maximumStartingDoctors)
    {
        if (playerCount <= 0)
            return 0;

        int twoDoctorThreshold = Math.Max(1, twoDoctorMinimumPlayers);
        int threeDoctorThreshold = Math.Max(twoDoctorThreshold + 1, threeDoctorMinimumPlayers);
        int configuredMaximum = Math.Max(1, Math.Min(HardMaximumStartingDoctors, maximumStartingDoctors));

        int desiredDoctors = playerCount >= threeDoctorThreshold
            ? 3
            : playerCount >= twoDoctorThreshold
                ? 2
                : 1;

        return Math.Min(playerCount, Math.Min(configuredMaximum, desiredDoctors));
    }

    private void BeginInitialRoleNormalization()
    {
        CancelInitialRoleNormalization();
        _initialNormalizationCompleted = false;
        int generation = _initialNormalizationGeneration;

        RoleAssigner.OnPlayersSpawned += OnStartingPlayersSpawned;
        _roleAssignmentSubscribed = true;

        // RoleAssigner.OnPlayersSpawned is the authoritative automatic-round hook.
        // The short next-frame fallback handles a manual start after that one-time
        // hook has already fired, without delaying normal automatic startup.
        _initialNormalizationHandle = Timing.CallDelayed(
            InitialNormalizationFallbackDelaySeconds,
            () => OnInitialNormalizationFallback(generation)
        );
    }

    private void OnStartingPlayersSpawned()
    {
        TryConfigureStartingRoles(_initialNormalizationGeneration);
    }

    private void OnInitialNormalizationFallback(int generation)
    {
        if (generation == _initialNormalizationGeneration)
            _initialNormalizationHandle = default;

        TryConfigureStartingRoles(generation);
    }

    private void TryConfigureStartingRoles(int generation)
    {
        if (generation != _initialNormalizationGeneration ||
            _initialNormalizationCompleted ||
            !IsRunning ||
            !Round.IsRoundInProgress)
        {
            return;
        }

        List<Player> participants = Player.List
            .Where(IsRoundParticipant)
            .ToList();

        if (participants.Count == 0)
            return;

        StopWaitingForInitialRoles();

        try
        {
            ConfigureStartingRoles(participants);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCPEventSystem] Infection initial role normalization failed: {ex.Message}"
            );
        }
        finally
        {
            _initialNormalizationCompleted = true;
        }
    }

    private void CancelInitialRoleNormalization()
    {
        _initialNormalizationGeneration++;
        _initialNormalizationCompleted = false;
        StopWaitingForInitialRoles();
    }

    private void StopWaitingForInitialRoles()
    {
        if (_roleAssignmentSubscribed)
        {
            RoleAssigner.OnPlayersSpawned -= OnStartingPlayersSpawned;
            _roleAssignmentSubscribed = false;
        }

        if (_initialNormalizationHandle.IsValid)
            CancelHandle(_initialNormalizationHandle, "Infection initial-role fallback");

        _initialNormalizationHandle = default;
    }

    private void ConfigureStartingRoles(IReadOnlyList<Player> participants)
    {
        int targetDoctorCount = CalculateStartingDoctorCount(
            participants.Count,
            _config.TwoDoctorMinimumPlayers,
            _config.ThreeDoctorMinimumPlayers,
            _config.MaximumStartingDoctors
        );

        List<Player> startingScpSlots = participants
            .Where(player => player.Team == Team.SCPs)
            .ToList();
        List<Player> existingDoctors = startingScpSlots
            .Where(player => player.Role == RoleTypeId.Scp049)
            .ToList();
        List<Player> otherScps = startingScpSlots
            .Where(player => player.Role != RoleTypeId.Scp049)
            .ToList();
        List<Player> humans = participants
            .Where(IsPlayableHuman)
            .ToList();

        List<Player> selectedDoctors = existingDoctors
            .Take(targetDoctorCount)
            .ToList();

        int additionalDoctorsNeeded = targetDoctorCount - selectedDoctors.Count;
        selectedDoctors.AddRange(otherScps.Take(Math.Max(0, additionalDoctorsNeeded)));

        additionalDoctorsNeeded = targetDoctorCount - selectedDoctors.Count;
        selectedDoctors.AddRange(humans.Take(Math.Max(0, additionalDoctorsNeeded)));

        HashSet<uint> selectedDoctorIds = new(selectedDoctors.Select(player => player.NetworkId));
        List<Player> surplusScps = startingScpSlots
            .Where(player => !selectedDoctorIds.Contains(player.NetworkId))
            .ToList();

        foreach (Player doctor in selectedDoctors)
            SetStartingRole(doctor, RoleTypeId.Scp049);

        // Existing valid human roles remain untouched. Only surplus vanilla SCP
        // assignments receive the simple human fallback required by Infection.
        foreach (Player surplusScp in surplusScps)
            SetStartingRole(surplusScp, RoleTypeId.ClassD);

        List<Player> finalParticipants = Player.List
            .Where(IsRoundParticipant)
            .ToList();

        foreach (Player player in finalParticipants)
            ScheduleRoleStatApplication(player);
    }

    private static void SetStartingRole(Player player, RoleTypeId role)
    {
        RoleTypeId previousRole = player.Role;
        if (previousRole == role)
            return;

        try
        {
            player.SetRole(role, RoleChangeReason.RoundStart, RoleSpawnFlags.All);

            if (player.Role != role)
            {
                Console.WriteLine(
                    $"[SCPEventSystem] Infection could not assign starting role '{role}' " +
                    $"to '{player.Nickname}'."
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCPEventSystem] Infection failed to assign starting role '{role}' " +
                $"to '{player.Nickname}': {ex.Message}"
            );
        }
    }

    private void ScheduleRoleStatApplication(Player? player)
    {
        if (!IsRunning ||
            player == null ||
            player.IsDestroyed ||
            !player.IsAlive ||
            !TryGetRoleStatMultipliers(player.Role, out float healthMultiplier, out float shieldMultiplier) ||
            (IsDefaultMultiplier(healthMultiplier) && IsDefaultMultiplier(shieldMultiplier)))
        {
            return;
        }

        uint networkId = player.NetworkId;
        int lifeId = player.LifeId;
        RoleTypeId role = player.Role;

        if (_modifiedRoleStats.TryGetValue(networkId, out ModifiedRoleStats? state) &&
            state.LifeId == lifeId &&
            state.Role == role)
        {
            return;
        }

        if (_pendingRoleStatApplications.TryGetValue(
                networkId,
                out PendingRoleStatApplication pending))
        {
            if (pending.LifeId == lifeId && pending.Role == role)
                return;

            CancelPendingRoleStatApplication(networkId);
        }

        CoroutineHandle handle = Timing.CallDelayed(
            RoleStatApplicationDelaySeconds,
            () => CompleteRoleStatApplication(networkId, lifeId, role)
        );

        _pendingRoleStatApplications[networkId] = new PendingRoleStatApplication(
            lifeId,
            role,
            handle
        );
    }

    private void CompleteRoleStatApplication(uint networkId, int lifeId, RoleTypeId role)
    {
        if (!_pendingRoleStatApplications.TryGetValue(
                networkId,
                out PendingRoleStatApplication pending) ||
            pending.LifeId != lifeId ||
            pending.Role != role)
        {
            return;
        }

        _pendingRoleStatApplications.Remove(networkId);

        if (!IsRunning)
            return;

        Player? player = FindPlayer(networkId);
        if (player == null ||
            player.IsDestroyed ||
            !player.IsAlive ||
            player.NetworkId != networkId ||
            player.LifeId != lifeId ||
            player.Role != role)
        {
            return;
        }

        try
        {
            ApplyRoleStats(player);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCPEventSystem] Infection failed to apply role stats to " +
                $"'{player.Nickname}' ({player.Role}): {ex.Message}"
            );
        }
    }

    private void ApplyRoleStats(Player player)
    {
        if (!TryGetRoleStatMultipliers(
                player.Role,
                out float healthMultiplier,
                out float shieldMultiplier))
        {
            return;
        }

        healthMultiplier = NormalizeMultiplier(healthMultiplier);
        shieldMultiplier = NormalizeMultiplier(shieldMultiplier);

        if (IsDefaultMultiplier(healthMultiplier) && IsDefaultMultiplier(shieldMultiplier))
            return;

        ModifiedRoleStats state = new(player.LifeId, player.Role);
        _modifiedRoleStats[player.NetworkId] = state;

        if (!IsDefaultMultiplier(healthMultiplier) && player.MaxHealth > 0f)
        {
            float originalMaximumHealth = player.MaxHealth;
            float newMaximumHealth = originalMaximumHealth * healthMultiplier;

            if (IsValidMaximum(newMaximumHealth))
            {
                state.MaximumHealthApplied = true;
                state.OriginalMaximumHealth = originalMaximumHealth;
                float newHealth = ScaleCurrentPool(
                    player.Health,
                    originalMaximumHealth,
                    newMaximumHealth
                );
                player.MaxHealth = newMaximumHealth;
                player.Health = newHealth;
            }
        }

        if (!IsDefaultMultiplier(shieldMultiplier) && player.MaxHumeShield > 0f)
        {
            float originalMaximumShield = player.MaxHumeShield;
            float newMaximumShield = originalMaximumShield * shieldMultiplier;

            if (IsValidMaximum(newMaximumShield))
            {
                state.MaximumHumeShieldApplied = true;
                state.OriginalMaximumHumeShield = originalMaximumShield;
                float newShield = ScaleCurrentPool(
                    player.HumeShield,
                    originalMaximumShield,
                    newMaximumShield
                );
                player.MaxHumeShield = newMaximumShield;
                player.HumeShield = newShield;
            }
        }

        if (!state.MaximumHealthApplied && !state.MaximumHumeShieldApplied)
        {
            _modifiedRoleStats.Remove(player.NetworkId);
            return;
        }

    }

    private bool TryGetRoleStatMultipliers(
        RoleTypeId role,
        out float healthMultiplier,
        out float shieldMultiplier)
    {
        switch (role)
        {
            case RoleTypeId.Scp049:
                healthMultiplier = NormalizeMultiplier(_config.PlagueDoctorHealthMultiplier);
                shieldMultiplier = NormalizeMultiplier(_config.PlagueDoctorHumeShieldMultiplier);
                return true;
            case RoleTypeId.Scp0492:
                healthMultiplier = NormalizeMultiplier(_config.ZombieHealthMultiplier);
                shieldMultiplier = NormalizeMultiplier(_config.ZombieHumeShieldMultiplier);
                return true;
            default:
                healthMultiplier = 1f;
                shieldMultiplier = 1f;
                return false;
        }
    }

    private void RestoreAllModifiedRoleStats()
    {
        foreach (Player player in Player.List)
        {
            if (player == null ||
                player.IsDestroyed ||
                !_modifiedRoleStats.TryGetValue(player.NetworkId, out ModifiedRoleStats? state) ||
                state.LifeId != player.LifeId ||
                state.Role != player.Role)
            {
                continue;
            }

            try
            {
                RestoreRoleStats(player, state);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[SCPEventSystem] Infection failed to restore role stats for " +
                    $"'{player.Nickname}' ({player.Role}): {ex.Message}"
                );
            }
        }

        _modifiedRoleStats.Clear();
    }

    private static void RestoreRoleStats(Player player, ModifiedRoleStats state)
    {
        if (state.MaximumHealthApplied)
        {
            float restoredHealth = ScaleCurrentPool(
                player.Health,
                player.MaxHealth,
                state.OriginalMaximumHealth
            );
            player.MaxHealth = state.OriginalMaximumHealth;
            player.Health = restoredHealth;
        }

        if (state.MaximumHumeShieldApplied)
        {
            float restoredShield = ScaleCurrentPool(
                player.HumeShield,
                player.MaxHumeShield,
                state.OriginalMaximumHumeShield
            );
            player.MaxHumeShield = state.OriginalMaximumHumeShield;
            player.HumeShield = restoredShield;
        }
    }

    private static float ScaleCurrentPool(float current, float currentMaximum, float newMaximum)
    {
        if (currentMaximum <= 0f || newMaximum <= 0f)
            return 0f;

        float scaled = current * (newMaximum / currentMaximum);
        return Math.Min(newMaximum, Math.Max(0f, scaled));
    }

    private static bool IsValidMaximum(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

    private static bool IsDefaultMultiplier(float multiplier) =>
        Math.Abs(multiplier - 1f) < 0.0001f;

    private static float NormalizeMultiplier(float multiplier)
    {
        return float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f
            ? 1f
            : multiplier;
    }

    private void CancelPendingRoleStatApplication(uint networkId)
    {
        if (!_pendingRoleStatApplications.TryGetValue(
                networkId,
                out PendingRoleStatApplication pending))
        {
            return;
        }

        _pendingRoleStatApplications.Remove(networkId);
        CancelHandle(
            pending.Handle,
            $"Infection role-stat application for network ID '{networkId}'"
        );
    }

    private void CancelAllPendingRoleStatApplications()
    {
        foreach (PendingRoleStatApplication pending in _pendingRoleStatApplications.Values)
            CancelHandle(pending.Handle, "pending Infection role-stat application");

        _pendingRoleStatApplications.Clear();
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        PlayerEvents.Joined += OnPlayerJoined;
        PlayerEvents.Left += OnPlayerLeft;
        PlayerEvents.Spawned += OnPlayerSpawned;
        PlayerEvents.ChangedRole += OnPlayerChangedRole;
        PlayerEvents.Death += OnPlayerDeath;
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
        PlayerEvents.Death -= OnPlayerDeath;
        _subscribed = false;
    }

    private void OnPlayerJoined(PlayerJoinedEventArgs args)
    {
        ClearPlayerTracking(args.Player.NetworkId);
    }

    private void OnPlayerLeft(PlayerLeftEventArgs args)
    {
        ClearPlayerTracking(args.Player.NetworkId);
    }

    private void OnPlayerSpawned(PlayerSpawnedEventArgs args)
    {
        if (_pendingConversions.TryGetValue(args.Player.NetworkId, out PendingConversion? pending))
        {
            if (IsDeadSpectator(args.Player))
                UpdateExpectedDeadLife(pending, args.Player.LifeId);
            else
                CancelPendingConversion(args.Player.NetworkId);
        }

        ScheduleRoleStatApplication(args.Player);
    }

    private void OnPlayerChangedRole(PlayerChangedRoleEventArgs args)
    {
        if (_pendingConversions.TryGetValue(args.Player.NetworkId, out PendingConversion? pending))
        {
            if (args.ChangeReason == RoleChangeReason.Died && IsDeadSpectator(args.Player))
            {
                UpdateExpectedDeadLife(pending, args.Player.LifeId);
            }
            else
            {
                // Any respawn, escape, Remote Admin change, normal SCP-049 revival,
                // or other legitimate new life supersedes the delayed event conversion.
                CancelPendingConversion(args.Player.NetworkId);
            }
        }

        CancelPendingRoleStatApplication(args.Player.NetworkId);
        _modifiedRoleStats.Remove(args.Player.NetworkId);
        ScheduleRoleStatApplication(args.Player);
    }

    private void OnPlayerDeath(PlayerDeathEventArgs args)
    {
        if (!IsRunning || !IsZombieInfectionKill(args))
            return;

        uint networkId = args.Player.NetworkId;
        int observedLifeId = args.Player.LifeId;

        if (IsDeathLifeHandled(networkId, observedLifeId))
            return;

        MarkDeathLifeHandled(networkId, observedLifeId);

        if (_pendingConversions.TryGetValue(networkId, out PendingConversion? existing))
        {
            if (existing.ExpectedLifeId == observedLifeId)
                return;

            CancelPendingConversion(networkId);
        }

        int conversionId = ++_nextConversionId;
        PendingConversion pending = new(
            networkId,
            conversionId,
            observedLifeId,
            args.OldPosition
        );

        pending.Handle = Timing.CallDelayed(
            GetConversionDelaySeconds(),
            () => CompleteConversion(networkId, conversionId)
        );
        _pendingConversions[networkId] = pending;
    }

    private static bool IsZombieInfectionKill(PlayerDeathEventArgs args)
    {
        if (!IsPlayableHumanRole(args.OldRole))
            return false;

        if (args.DamageHandler is not Scp049DamageHandler damageHandler ||
            damageHandler.DamageSubType != Scp049DamageHandler.AttackType.Scp0492)
            return false;

        // The damage handler's Footprint captures the attacker's role and identity
        // when the hit is created. It remains authoritative even if the zombie dies,
        // changes role, or disconnects before the victim's Death event is dispatched.
        return damageHandler.Attacker.IsSet &&
            damageHandler.Attacker.NetId != 0 &&
            damageHandler.Attacker.NetId != args.Player.NetworkId &&
            damageHandler.Attacker.Role == RoleTypeId.Scp0492;
    }

    private void CompleteConversion(uint networkId, int conversionId)
    {
        if (!_pendingConversions.TryGetValue(networkId, out PendingConversion? pending) ||
            pending.ConversionId != conversionId)
        {
            return;
        }

        _pendingConversions.Remove(networkId);

        if (!IsRunning)
            return;

        Player? player = FindPlayer(networkId);
        if (player == null ||
            player.IsDestroyed ||
            player.NetworkId != networkId ||
            player.LifeId != pending.ExpectedLifeId ||
            !IsDeadSpectator(player))
        {
            return;
        }

        try
        {
            player.SetRole(
                RoleTypeId.Scp0492,
                RoleChangeReason.Revived,
                RoleSpawnFlags.None
            );

            if (player.Role != RoleTypeId.Scp0492)
            {
                Console.WriteLine(
                    $"[SCPEventSystem] Infection failed to convert '{player.Nickname}' to SCP-049-2."
                );
                return;
            }

            player.Position = pending.DeathPosition;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCPEventSystem] Infection failed to convert '{player.Nickname}' " +
                $"to SCP-049-2: {ex.Message}"
            );
        }
    }

    private void UpdateExpectedDeadLife(PendingConversion pending, int lifeId)
    {
        pending.ExpectedLifeId = lifeId;
        MarkDeathLifeHandled(pending.NetworkId, lifeId);
    }

    private bool IsDeathLifeHandled(uint networkId, int lifeId)
    {
        return _handledDeathLives.TryGetValue(networkId, out HashSet<int>? lifeIds) &&
            lifeIds.Contains(lifeId);
    }

    private void MarkDeathLifeHandled(uint networkId, int lifeId)
    {
        if (!_handledDeathLives.TryGetValue(networkId, out HashSet<int>? lifeIds))
        {
            lifeIds = new HashSet<int>();
            _handledDeathLives[networkId] = lifeIds;
        }

        lifeIds.Add(lifeId);
    }

    private void ClearPlayerTracking(uint networkId)
    {
        CancelPendingConversion(networkId);
        CancelPendingRoleStatApplication(networkId);
        _handledDeathLives.Remove(networkId);
        _modifiedRoleStats.Remove(networkId);
    }

    private void CancelPendingConversion(uint networkId)
    {
        if (!_pendingConversions.TryGetValue(networkId, out PendingConversion? pending))
            return;

        _pendingConversions.Remove(networkId);
        CancelHandle(pending.Handle, $"Infection conversion for network ID '{networkId}'");
    }

    private void CancelAllPendingConversions()
    {
        foreach (PendingConversion pending in _pendingConversions.Values)
            CancelHandle(pending.Handle, "pending Infection conversion");

        _pendingConversions.Clear();
    }

    private static void CancelHandle(CoroutineHandle handle, string operation)
    {
        try
        {
            if (handle.IsValid)
                Timing.KillCoroutines(handle);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SCPEventSystem] Failed to cancel {operation}: {ex.Message}");
        }
    }

    private float GetConversionDelaySeconds()
    {
        float configuredDelay = _config.ConversionDelaySeconds;
        if (float.IsNaN(configuredDelay) || float.IsInfinity(configuredDelay))
            configuredDelay = MinimumSafeConversionDelaySeconds;

        return Math.Max(MinimumSafeConversionDelaySeconds, configuredDelay);
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

    private static bool IsRoundParticipant(Player? player)
    {
        return player != null &&
            !player.IsDestroyed &&
            player.IsReady &&
            player.IsAlive &&
            (player.Team == Team.SCPs || IsPlayableHuman(player));
    }

    private static bool IsPlayableHuman(Player? player)
    {
        return player != null &&
            !player.IsDestroyed &&
            player.IsAlive &&
            IsPlayableHumanRole(player.Role);
    }

    private static bool IsPlayableHumanRole(RoleTypeId role)
    {
        switch (role)
        {
            case RoleTypeId.ClassD:
            case RoleTypeId.Scientist:
            case RoleTypeId.FacilityGuard:
            case RoleTypeId.NtfSpecialist:
            case RoleTypeId.NtfSergeant:
            case RoleTypeId.NtfCaptain:
            case RoleTypeId.NtfPrivate:
            case RoleTypeId.ChaosConscript:
            case RoleTypeId.ChaosRifleman:
            case RoleTypeId.ChaosMarauder:
            case RoleTypeId.ChaosRepressor:
                return true;
            default:
                return false;
        }
    }

    private static bool IsDeadSpectator(Player player)
    {
        return !player.IsDestroyed &&
            !player.IsAlive &&
            player.Team == Team.Dead &&
            player.Role == RoleTypeId.Spectator;
    }

    private static void SendAnnouncement(string announcement, ushort durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(announcement) || durationSeconds == 0)
            return;

        Server.SendBroadcast(announcement, durationSeconds);
    }

    private sealed class ModifiedRoleStats
    {
        public ModifiedRoleStats(int lifeId, RoleTypeId role)
        {
            LifeId = lifeId;
            Role = role;
        }

        public int LifeId { get; }

        public RoleTypeId Role { get; }

        public bool MaximumHealthApplied { get; set; }

        public float OriginalMaximumHealth { get; set; }

        public bool MaximumHumeShieldApplied { get; set; }

        public float OriginalMaximumHumeShield { get; set; }
    }

    private readonly struct PendingRoleStatApplication
    {
        public PendingRoleStatApplication(
            int lifeId,
            RoleTypeId role,
            CoroutineHandle handle)
        {
            LifeId = lifeId;
            Role = role;
            Handle = handle;
        }

        public int LifeId { get; }

        public RoleTypeId Role { get; }

        public CoroutineHandle Handle { get; }
    }

    private sealed class PendingConversion
    {
        public PendingConversion(
            uint networkId,
            int conversionId,
            int expectedLifeId,
            Vector3 deathPosition)
        {
            NetworkId = networkId;
            ConversionId = conversionId;
            ExpectedLifeId = expectedLifeId;
            DeathPosition = deathPosition;
        }

        public int ConversionId { get; }

        public uint NetworkId { get; }

        public int ExpectedLifeId { get; set; }

        public Vector3 DeathPosition { get; }

        public CoroutineHandle Handle { get; set; }
    }
}
