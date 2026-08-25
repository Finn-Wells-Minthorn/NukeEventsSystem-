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
    private const int HardMaximumStartingDoctors = 3;

    private readonly InfectionEventConfig _config;
    private readonly Dictionary<uint, PendingConversion> _pendingConversions = new();
    private readonly Dictionary<uint, HashSet<int>> _handledDeathLives = new();
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

        Console.WriteLine(
            "[SCPEventSystem] Infection is waiting for vanilla starting role assignment to complete."
        );
    }

    private void OnStartingPlayersSpawned()
    {
        TryConfigureStartingRoles(
            _initialNormalizationGeneration,
            "RoleAssigner.OnPlayersSpawned"
        );
    }

    private void OnInitialNormalizationFallback(int generation)
    {
        if (generation == _initialNormalizationGeneration)
            _initialNormalizationHandle = default;

        TryConfigureStartingRoles(generation, "post-start fallback");
    }

    private void TryConfigureStartingRoles(int generation, string trigger)
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
        {
            Console.WriteLine(
                $"[SCPEventSystem] Infection initial normalization trigger '{trigger}' " +
                "found no assigned round participants; continuing to wait for role assignment."
            );
            return;
        }

        StopWaitingForInitialRoles();

        try
        {
            ConfigureStartingRoles(participants, trigger);
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

    private void ConfigureStartingRoles(IReadOnlyList<Player> participants, string trigger)
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

        Console.WriteLine(
            $"[SCPEventSystem] Infection initial normalization triggered by '{trigger}': " +
            $"participants='{participants.Count}', targetDoctors='{targetDoctorCount}', " +
            $"rolesBefore='{DescribePlayers(participants)}'."
        );

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

        Console.WriteLine(
            $"[SCPEventSystem] Infection starting SCP slots='{DescribePlayers(startingScpSlots)}'; " +
            $"selectedDoctors='{DescribePlayers(selectedDoctors)}'; " +
            $"surplusScps='{DescribePlayers(surplusScps)}'."
        );

        List<string> assignments = new();

        foreach (Player doctor in selectedDoctors)
            SetStartingRole(doctor, RoleTypeId.Scp049, assignments);

        // Existing valid human roles remain untouched. Only surplus vanilla SCP
        // assignments receive the simple human fallback required by Infection.
        foreach (Player surplusScp in surplusScps)
            SetStartingRole(surplusScp, RoleTypeId.ClassD, assignments);

        List<Player> finalParticipants = Player.List
            .Where(IsRoundParticipant)
            .ToList();
        List<Player> nonDoctorScps = finalParticipants
            .Where(player => player.Team == Team.SCPs && player.Role != RoleTypeId.Scp049)
            .ToList();
        int actualDoctorCount = finalParticipants.Count(player => player.Role == RoleTypeId.Scp049);
        string result = actualDoctorCount == targetDoctorCount && nonDoctorScps.Count == 0
            ? "SUCCESS"
            : "FAILED";

        Console.WriteLine(
            $"[SCPEventSystem] Infection starting assignments='" +
            $"{(assignments.Count == 0 ? "(none)" : string.Join(", ", assignments))}'."
        );
        Console.WriteLine(
            $"[SCPEventSystem] Infection initial normalization {result}: " +
            $"targetDoctors='{targetDoctorCount}', actualDoctors='{actualDoctorCount}', " +
            $"non049ScpCount='{nonDoctorScps.Count}', " +
            $"non049Scps='{DescribePlayers(nonDoctorScps)}', " +
            $"rolesAfter='{DescribePlayers(finalParticipants)}'."
        );
    }

    private void SetStartingRole(Player player, RoleTypeId role, ICollection<string> assignments)
    {
        RoleTypeId previousRole = player.Role;
        if (previousRole == role)
            return;

        try
        {
            player.SetRole(role, RoleChangeReason.RoundStart, RoleSpawnFlags.All);
            assignments.Add(
                $"{player.Nickname}[net={player.NetworkId}] {previousRole}->{player.Role}"
            );

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
            assignments.Add(
                $"{player.Nickname}[net={player.NetworkId}] {previousRole}->FAILED({role})"
            );
            Console.WriteLine(
                $"[SCPEventSystem] Infection failed to assign starting role '{role}' " +
                $"to '{player.Nickname}': {ex.Message}"
            );
        }
    }

    private static string DescribePlayers(IEnumerable<Player> players)
    {
        string[] descriptions = players
            .Select(player =>
                $"{player.Nickname}[net={player.NetworkId},life={player.LifeId},role={player.Role}]")
            .ToArray();

        return descriptions.Length == 0 ? "(none)" : string.Join(", ", descriptions);
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
        if (!_pendingConversions.TryGetValue(args.Player.NetworkId, out PendingConversion? pending))
            return;

        if (IsDeadSpectator(args.Player))
        {
            UpdateExpectedDeadLife(pending, args.Player.LifeId);
            return;
        }

        CancelPendingConversion(args.Player.NetworkId);
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
        _handledDeathLives.Remove(networkId);
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
