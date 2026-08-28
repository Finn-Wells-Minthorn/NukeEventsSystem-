using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using MEC;
using MyFirstPlugin.Config;
using PlayerRoles;

namespace MyFirstPlugin.Events;

public class BlackoutEvent : EventBase
{
    private const float CassieAnnouncementTimeSeconds = 1f;
    private const float FirstIntroFlickerTimeSeconds = 2f;
    private const float SecondIntroFlickerTimeSeconds = 5f;
    private const float FinalIntroWarningTimeSeconds = 8f;
    private const float NormalCycleStartTimeSeconds = 10f;
    private const float MinimumSafeCallbackDelaySeconds = 0.1f;

    private static readonly char[] CassieTokenSeparators = { ' ', '\t', '\r', '\n' };
    private static readonly char[] CassieTokenPunctuation = { '.', ',', '!', '?', ';', ':' };

    private readonly BlackoutEventConfig _config;
    private readonly Random _random = new();
    private readonly List<CoroutineHandle> _cinematicActionHandles = new();
    private readonly Dictionary<uint, PlayerLifeState> _playerLives = new();
    private readonly Dictionary<uint, PendingLightProcessing> _pendingLightProcessing = new();
    private readonly List<Pickup> _overflowPickups = new();

    private CoroutineHandle _introDelayHandle;
    private CoroutineHandle _normalCycleHandle;
    private CoroutineHandle _darkSegmentFlickerHandle;
    private CoroutineHandle _poweredFlickerHandle;
    private CoroutineHandle _initialPlayerSweepHandle;
    private int _introGeneration;
    private int _lightingPhaseGeneration;
    private LightingPhase _lightingPhase;
    private bool _cinematicActive;
    private bool _lightsOn;
    private bool _grantLightSourcesThisRound;
    private bool _subscribed;
    private bool _cleanupCompleted = true;

    public BlackoutEvent(BlackoutEventConfig? config)
    {
        _config = config ?? new BlackoutEventConfig();
    }

    public override string Name => "Blackout Event";

    public override string Description =>
        "A round-long facility blackout with a delayed cinematic intro and randomized dark and powered periods.";

    public override string DisplayColor => _config.DisplayColor;

    protected override void OnStart()
    {
        _cleanupCompleted = false;
        _cinematicActive = false;
        _lightingPhase = LightingPhase.Intro;
        _lightingPhaseGeneration++;
        _introGeneration++;
        _lightsOn = true;
        Map.TurnOnLights();

        _grantLightSourcesThisRound = RollChance(_config.LightSourceChance);
        Logger.Info(
            $"[SCPEventSystem] Blackout light-source roll: " +
            $"{(_grantLightSourcesThisRound ? "enabled" : "disabled")} " +
            $"(configured chance '{_config.LightSourceChance}')."
        );

        Subscribe();
        ScheduleInitialPlayerSweep();
        ScheduleIntro();
    }

    protected override void OnStop()
    {
        if (_cleanupCompleted)
        {
            RestoreFacilityLighting();
            return;
        }

        _cleanupCompleted = true;
        _introGeneration++;
        _lightingPhaseGeneration++;
        _cinematicActive = false;
        _lightingPhase = LightingPhase.None;
        _grantLightSourcesThisRound = false;

        Unsubscribe();
        CancelIntroDelay();
        CancelCinematicActions();
        CancelInitialPlayerSweep();
        CancelAllPendingLightProcessing();
        CancelNormalCycle();
        CancelDarkSegmentFlicker();
        CancelPoweredFlicker();
        CleanupOverflowPickups();

        _playerLives.Clear();
        RestoreFacilityLighting();
        SendBroadcast(_config.EndAnnouncement, _config.EndAnnouncementDurationSeconds);
    }

    private void ScheduleIntro()
    {
        CancelIntroDelay();

        int introGeneration = _introGeneration;
        float delaySeconds = NormalizeNonnegative(_config.IntroStartDelaySeconds);
        _introDelayHandle = Timing.CallDelayed(
            delaySeconds,
            () => BeginCinematic(introGeneration)
        );
    }

    private void BeginCinematic(int introGeneration)
    {
        _introDelayHandle = default;

        if (!IsRunning || introGeneration != _introGeneration)
            return;

        _cinematicActive = true;
        SendBroadcast(_config.StartAnnouncement, _config.StartAnnouncementDurationSeconds);

        ScheduleCinematicAction(CassieAnnouncementTimeSeconds, introGeneration, PlayCassieAnnouncement);
        ScheduleCinematicAction(
            FirstIntroFlickerTimeSeconds,
            introGeneration,
            () => DoIntroFlickerBurst(1, introGeneration)
        );
        ScheduleCinematicAction(
            SecondIntroFlickerTimeSeconds,
            introGeneration,
            () => DoIntroFlickerBurst(2, introGeneration)
        );
        ScheduleCinematicAction(
            FinalIntroWarningTimeSeconds,
            introGeneration,
            () =>
            {
                SendBroadcast(_config.PreBlackoutWarning, _config.PreBlackoutWarningDurationSeconds);
                DoIntroFlickerBurst(3, introGeneration);
            }
        );
        ScheduleCinematicAction(
            NormalCycleStartTimeSeconds,
            introGeneration,
            () => StartNormalCycle(introGeneration)
        );
    }

    private void ScheduleCinematicAction(float delaySeconds, int introGeneration, Action action)
    {
        CoroutineHandle handle = Timing.CallDelayed(
            NormalizeNonnegative(delaySeconds),
            () =>
            {
                if (!IsCinematicActive(introGeneration))
                    return;

                action();
            }
        );

        _cinematicActionHandles.Add(handle);
    }

    private void DoIntroFlickerBurst(int burstCount, int introGeneration)
    {
        if (!_config.EnableFlickering || burstCount <= 0 || !IsCinematicActive(introGeneration))
            return;

        int stepDurationMilliseconds = Math.Max(0, _config.FlickerStepDurationMilliseconds);
        float stepDurationSeconds = stepDurationMilliseconds / 1000f;

        for (int index = 0; index < burstCount; index++)
        {
            float turnOffDelay = index * 2f * stepDurationSeconds;
            float turnOnDelay = turnOffDelay + stepDurationSeconds;

            ScheduleCinematicAction(
                turnOffDelay,
                introGeneration,
                () => SetFacilityLighting(false)
            );
            ScheduleCinematicAction(
                turnOnDelay,
                introGeneration,
                () => SetFacilityLighting(true)
            );
        }
    }

    private bool IsCinematicActive(int introGeneration)
    {
        return IsRunning &&
            _cinematicActive &&
            introGeneration == _introGeneration &&
            _lightingPhase == LightingPhase.Intro;
    }

    private void PlayCassieAnnouncement()
    {
        if (!_config.CassieEnabled)
            return;

        string message = _config.CassieSpokenMessage ?? string.Empty;
        if (!TryValidateCassieMessage(message, out string invalidToken))
        {
            Logger.Warn(
                $"[SCPEventSystem] Invalid Blackout CASSIE message '{message}'. " +
                $"Unrecognized spoken token: '{invalidToken}'. The announcement was skipped."
            );
            return;
        }

        try
        {
            Announcer.Message(
                message,
                _config.CassieCustomSubtitle ?? string.Empty,
                _config.CassiePlayBackgroundAudio,
                NormalizeFinite(_config.CassiePriority),
                Clamp(NormalizeFinite(_config.CassieGlitchIntensity), 0f, 1f)
            );
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[SCPEventSystem] Failed to queue the Blackout CASSIE message '{message}': {ex.Message}"
            );
        }
    }

    private static bool TryValidateCassieMessage(string message, out string invalidToken)
    {
        invalidToken = string.Empty;
        bool foundSpokenToken = false;

        foreach (string rawToken in message.Split(CassieTokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = rawToken.Trim(CassieTokenPunctuation);
            if (string.IsNullOrWhiteSpace(token))
                continue;

            foundSpokenToken = true;
            if (Announcer.IsValid(token))
                continue;

            invalidToken = token;
            return false;
        }

        if (foundSpokenToken)
            return true;

        invalidToken = "<empty>";
        return false;
    }

    private void StartNormalCycle(int introGeneration)
    {
        if (!IsCinematicActive(introGeneration))
            return;

        _cinematicActive = false;
        CancelNormalCycle();
        _normalCycleHandle = Timing.RunCoroutine(NormalCycleRoutine());
    }

    private IEnumerator<float> NormalCycleRoutine()
    {
        while (IsRunning)
        {
            int blackoutSeconds = GetRandomBlackoutDurationSeconds();
            int darkGeneration = EnterLightingPhase(LightingPhase.Dark);

            CancelPoweredFlicker();
            SetFacilityLighting(false);

            if (_config.EnableFlickering && RollChance(_config.BlackoutFlickerChance))
            {
                CancelDarkSegmentFlicker();
                _darkSegmentFlickerHandle = Timing.RunCoroutine(
                    FlickeringBlackoutRoutine(darkGeneration)
                );
            }

            yield return Timing.WaitForSeconds(blackoutSeconds);

            if (!IsDarkSegmentActive(darkGeneration))
                yield break;

            int poweredSeconds = Math.Max(1, _config.NormalPoweredSeconds);
            int poweredGeneration = EnterLightingPhase(LightingPhase.Powered);

            CancelDarkSegmentFlicker();
            SetFacilityLighting(true);

            if (_config.EnableFlickering && RollChance(_config.PoweredFlickerChance))
            {
                int flickerCount = GetRandomPoweredFlickerCount();
                if (flickerCount > 0)
                {
                    CancelPoweredFlicker();
                    _poweredFlickerHandle = Timing.RunCoroutine(
                        PoweredFlickerRoutine(poweredSeconds, flickerCount, poweredGeneration)
                    );
                }
            }

            yield return Timing.WaitForSeconds(poweredSeconds);
        }
    }

    private int EnterLightingPhase(LightingPhase phase)
    {
        _lightingPhaseGeneration++;
        _lightingPhase = phase;
        return _lightingPhaseGeneration;
    }

    private int GetRandomBlackoutDurationSeconds()
    {
        int shortMin = Math.Max(1, _config.ShortBlackoutMinSeconds);
        int shortMax = Math.Max(shortMin, _config.ShortBlackoutMaxSeconds);
        int longMin = Math.Max(1, _config.LongBlackoutMinSeconds);
        int longMax = Math.Max(longMin, _config.LongBlackoutMaxSeconds);

        if (RollChance(_config.ShortBlackoutChance))
            return GetRandomInclusive(shortMin, shortMax);

        return GetRandomInclusive(longMin, longMax);
    }

    private int GetRandomInclusive(int min, int max)
    {
        if (min >= max)
            return min;

        return max == int.MaxValue
            ? _random.Next(min, max)
            : _random.Next(min, max + 1);
    }

    private IEnumerator<float> FlickeringBlackoutRoutine(int darkGeneration)
    {
        while (IsDarkSegmentActive(darkGeneration))
        {
            float intervalSeconds = GetRandomBlackoutFlickerIntervalSeconds();
            yield return Timing.WaitForSeconds(intervalSeconds);

            // Revalidate after the interval elapsed.
            if (!IsDarkSegmentActive(darkGeneration))
                yield break;

            // Revalidate immediately before changing the lights.
            if (!IsDarkSegmentActive(darkGeneration) || _lightsOn)
                yield break;

            SetFacilityLighting(true);

            float pulseDurationSeconds = NormalizeNonnegative(_config.BlackoutFlickerDurationSeconds);
            if (pulseDurationSeconds > 0f)
                yield return Timing.WaitForSeconds(pulseDurationSeconds);

            // Revalidate after the light-on pulse.
            if (!IsDarkSegmentActive(darkGeneration))
                yield break;

            // Revalidate immediately before restoring darkness.
            if (!IsDarkSegmentActive(darkGeneration) || !_lightsOn)
                yield break;

            SetFacilityLighting(false);
        }
    }

    private bool IsDarkSegmentActive(int generation)
    {
        return IsRunning &&
            _lightingPhase == LightingPhase.Dark &&
            _lightingPhaseGeneration == generation;
    }

    private float GetRandomBlackoutFlickerIntervalSeconds()
    {
        float min = NormalizeNonnegative(_config.BlackoutFlickerMinIntervalSeconds);
        float max = NormalizeNonnegative(_config.BlackoutFlickerMaxIntervalSeconds);

        if (min > max)
        {
            float originalMin = min;
            min = max;
            max = originalMin;
        }

        min = Math.Max(MinimumSafeCallbackDelaySeconds, min);
        max = Math.Max(min, max);

        if (Math.Abs(max - min) < 0.0001f)
            return min;

        return (float)_random.NextDouble() * (max - min) + min;
    }

    private int GetRandomPoweredFlickerCount()
    {
        double roll = _random.NextDouble();
        if (roll < 0.45d)
            return 0;

        if (roll < 0.8d)
            return 1;

        return 2;
    }

    private IEnumerator<float> PoweredFlickerRoutine(
        float poweredSeconds,
        int flickerCount,
        int poweredGeneration)
    {
        if (flickerCount <= 0 || poweredSeconds <= 0f)
            yield break;

        float flickerDuration = NormalizeNonnegative(_config.SubtleFlickerDurationSeconds);
        float minTime = Math.Max(1f, poweredSeconds * 0.12f);
        float maxTime = Math.Max(minTime + 0.5f, poweredSeconds - 1f - flickerDuration);

        List<float> times = new();
        float minimumGap = Math.Max(2f, poweredSeconds / 8f);

        for (int index = 0; index < flickerCount && IsPoweredPhaseActive(poweredGeneration); index++)
        {
            float candidate;
            int attempts = 0;
            do
            {
                candidate = (float)_random.NextDouble() * (maxTime - minTime) + minTime;
                attempts++;
            }
            while (attempts < 20 && times.Exists(existing => Math.Abs(candidate - existing) < minimumGap));

            if (candidate <= minTime || candidate >= poweredSeconds - flickerDuration)
                continue;

            times.Add(candidate);
        }

        if (times.Count == 0)
            yield break;

        times.Sort();
        float elapsed = 0f;

        foreach (float when in times)
        {
            if (!IsPoweredPhaseActive(poweredGeneration))
                yield break;

            float delay = when - elapsed;
            if (delay > 0f)
                yield return Timing.WaitForSeconds(delay);

            elapsed = when;

            if (!IsPoweredPhaseActive(poweredGeneration) || !_lightsOn)
                yield break;

            SetFacilityLighting(false);
            if (flickerDuration > 0f)
                yield return Timing.WaitForSeconds(flickerDuration);

            if (!IsPoweredPhaseActive(poweredGeneration))
                yield break;

            if (!IsPoweredPhaseActive(poweredGeneration) || _lightsOn)
                yield break;

            SetFacilityLighting(true);
        }
    }

    private bool IsPoweredPhaseActive(int generation)
    {
        return IsRunning &&
            _lightingPhase == LightingPhase.Powered &&
            _lightingPhaseGeneration == generation;
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
        CancelPlayerLightProcessing(args.Player.NetworkId);
        _playerLives.Remove(args.Player.NetworkId);
    }

    private void OnPlayerLeft(PlayerLeftEventArgs args)
    {
        CancelPlayerLightProcessing(args.Player.NetworkId);
        _playerLives.Remove(args.Player.NetworkId);
    }

    private void OnPlayerSpawned(PlayerSpawnedEventArgs args)
    {
        ScheduleLightProcessing(args.Player);
    }

    private void OnPlayerChangedRole(PlayerChangedRoleEventArgs args)
    {
        uint networkId = args.Player.NetworkId;
        int lifeId = args.Player.LifeId;

        CancelPlayerLightProcessing(networkId);

        if (_playerLives.TryGetValue(networkId, out PlayerLifeState? state) && state.LifeId != lifeId)
            _playerLives.Remove(networkId);

        // Spawned and ChangedRole can arrive in either order. Both paths converge
        // on one delayed, NetworkId/LifeId-validated callback for the new life.
        ScheduleLightProcessing(args.Player);
    }

    private void OnPlayerDeath(PlayerDeathEventArgs args)
    {
        CancelPlayerLightProcessing(args.Player.NetworkId);
        _playerLives.Remove(args.Player.NetworkId);
    }

    private void ScheduleLightProcessing(Player? player)
    {
        if (!IsRunning || !_grantLightSourcesThisRound || !TryGetAssignedLightSource(player, out _))
            return;

        uint networkId = player!.NetworkId;
        int lifeId = player.LifeId;

        if (_playerLives.TryGetValue(networkId, out PlayerLifeState? state) &&
            state.LifeId == lifeId &&
            state.Processed)
        {
            return;
        }

        if (_pendingLightProcessing.TryGetValue(networkId, out PendingLightProcessing pending))
        {
            if (pending.LifeId == lifeId)
                return;

            CancelPlayerLightProcessing(networkId);
        }

        float delaySeconds = Math.Max(
            MinimumSafeCallbackDelaySeconds,
            NormalizeNonnegative(_config.LightSourceGrantDelaySeconds)
        );
        CoroutineHandle handle = Timing.CallDelayed(
            delaySeconds,
            () => CompleteLightProcessing(networkId, lifeId)
        );

        _pendingLightProcessing[networkId] = new PendingLightProcessing(lifeId, handle);
    }

    private void ScheduleInitialPlayerSweep()
    {
        CancelInitialPlayerSweep();

        float delaySeconds = Math.Max(
            MinimumSafeCallbackDelaySeconds,
            NormalizeNonnegative(_config.LightSourceGrantDelaySeconds)
        );
        _initialPlayerSweepHandle = Timing.CallDelayed(delaySeconds, RunInitialPlayerSweep);
    }

    private void RunInitialPlayerSweep()
    {
        _initialPlayerSweepHandle = default;

        if (!IsRunning || !_grantLightSourcesThisRound)
            return;

        foreach (Player player in Player.List)
        {
            if (TryGetAssignedLightSource(player, out _))
                ProcessCurrentLife(player.NetworkId, player.LifeId);
        }
    }

    private void CompleteLightProcessing(uint networkId, int lifeId)
    {
        if (!_pendingLightProcessing.TryGetValue(networkId, out PendingLightProcessing pending) ||
            pending.LifeId != lifeId)
        {
            return;
        }

        _pendingLightProcessing.Remove(networkId);
        ProcessCurrentLife(networkId, lifeId);
    }

    private void ProcessCurrentLife(uint networkId, int lifeId)
    {
        if (!IsRunning || !_grantLightSourcesThisRound)
            return;

        Player? player = FindPlayer(networkId);
        if (player == null ||
            player.NetworkId != networkId ||
            player.LifeId != lifeId ||
            !TryGetAssignedLightSource(player, out ItemType assignedItem))
        {
            return;
        }

        PlayerLifeState state = GetCurrentLifeState(player);
        if (state.Processed)
            return;

        if (player.Items.Any(item => item.Type == assignedItem) || TryGrantLightSource(player, assignedItem))
        {
            state.Processed = true;
            CancelPlayerLightProcessing(networkId);
        }
    }

    private bool TryGrantLightSource(Player player, ItemType itemType)
    {
        try
        {
            if (!player.IsInventoryFull && player.AddItem(itemType) != null)
                return true;

            Pickup? pickup = Pickup.Create(itemType, player.Position);
            if (pickup == null || !pickup.IsSpawned)
            {
                if (pickup != null && !pickup.IsDestroyed)
                    pickup.Destroy();

                Logger.Warn(
                    $"[SCPEventSystem] Failed to grant Blackout light source '{itemType}' " +
                    $"to '{player.Nickname}'."
                );
                return false;
            }

            _overflowPickups.Add(pickup);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[SCPEventSystem] Failed to grant Blackout light source '{itemType}' " +
                $"to '{player.Nickname}': {ex.Message}"
            );
            return false;
        }
    }

    private static bool TryGetAssignedLightSource(Player? player, out ItemType itemType)
    {
        itemType = ItemType.None;
        if (!IsPlayableHuman(player))
            return false;

        if (player!.Team == Team.ClassD)
        {
            itemType = ItemType.Lantern;
            return true;
        }

        itemType = ItemType.Flashlight;
        return true;
    }

    private static bool IsPlayableHuman(Player? player)
    {
        if (player == null || player.IsDestroyed || !player.IsAlive)
            return false;

        return player.Team == Team.FoundationForces ||
            player.Team == Team.ChaosInsurgency ||
            player.Team == Team.Scientists ||
            player.Team == Team.ClassD;
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

    private void CancelIntroDelay()
    {
        CancelHandle(ref _introDelayHandle, "Blackout intro delay");
    }

    private void CancelCinematicActions()
    {
        for (int index = _cinematicActionHandles.Count - 1; index >= 0; index--)
        {
            CoroutineHandle handle = _cinematicActionHandles[index];
            CancelHandle(handle, "Blackout cinematic action");
        }

        _cinematicActionHandles.Clear();
    }

    private void CancelInitialPlayerSweep()
    {
        CancelHandle(ref _initialPlayerSweepHandle, "Blackout initial-player light sweep");
    }

    private void CancelPlayerLightProcessing(uint networkId)
    {
        if (!_pendingLightProcessing.TryGetValue(networkId, out PendingLightProcessing pending))
            return;

        _pendingLightProcessing.Remove(networkId);
        CancelHandle(pending.Handle, $"Blackout light processing for network ID '{networkId}'");
    }

    private void CancelAllPendingLightProcessing()
    {
        foreach (PendingLightProcessing pending in _pendingLightProcessing.Values)
            CancelHandle(pending.Handle, "Blackout pending light processing");

        _pendingLightProcessing.Clear();
    }

    private void CancelNormalCycle()
    {
        CancelHandle(ref _normalCycleHandle, "Blackout normal cycle");
    }

    private void CancelDarkSegmentFlicker()
    {
        CancelHandle(ref _darkSegmentFlickerHandle, "Blackout dark-segment flicker");
    }

    private void CancelPoweredFlicker()
    {
        CancelHandle(ref _poweredFlickerHandle, "Blackout powered-period flicker");
    }

    private void CleanupOverflowPickups()
    {
        foreach (Pickup pickup in _overflowPickups)
        {
            try
            {
                if (pickup != null && !pickup.IsDestroyed)
                    pickup.Destroy();
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[SCPEventSystem] Failed to clean up a Blackout overflow pickup: {ex.Message}"
                );
            }
        }

        _overflowPickups.Clear();
    }

    private static void CancelHandle(ref CoroutineHandle handle, string operation)
    {
        CoroutineHandle capturedHandle = handle;
        handle = default;
        CancelHandle(capturedHandle, operation);
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
            Logger.Warn($"[SCPEventSystem] Failed to cancel {operation}: {ex.Message}");
        }
    }

    private static void SendBroadcast(string announcement, ushort durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(announcement) || durationSeconds == 0)
            return;

        Server.SendBroadcast(announcement, durationSeconds);
    }

    private void SetFacilityLighting(bool lightsOn)
    {
        _lightsOn = lightsOn;

        if (lightsOn)
            Map.TurnOnLights();
        else
            Map.TurnOffLights();
    }

    private void RestoreFacilityLighting()
    {
        _lightsOn = true;

        try
        {
            Map.TurnOnLights();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SCPEventSystem] Failed to restore facility lighting after Blackout: {ex.Message}");
        }
    }

    private bool RollChance(float configuredChance)
    {
        float normalizedChance = NormalizeChance(configuredChance);
        return _random.NextDouble() < normalizedChance;
    }

    private static float NormalizeChance(float configuredChance)
    {
        float finiteChance = NormalizeFinite(configuredChance);
        float clampedChance = Clamp(finiteChance, 0f, 100f);

        // Preserve compatibility with the existing 0..1 Blackout chance values
        // while allowing the new settings to use direct percentages such as 35 or 50.
        return clampedChance <= 1f ? clampedChance : clampedChance / 100f;
    }

    private static float NormalizeNonnegative(float value)
    {
        return Math.Max(0f, NormalizeFinite(value));
    }

    private static float NormalizeFinite(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }

    private sealed class PlayerLifeState
    {
        public PlayerLifeState(int lifeId)
        {
            LifeId = lifeId;
        }

        public int LifeId { get; }

        public bool Processed { get; set; }
    }

    private readonly struct PendingLightProcessing
    {
        public PendingLightProcessing(int lifeId, CoroutineHandle handle)
        {
            LifeId = lifeId;
            Handle = handle;
        }

        public int LifeId { get; }

        public CoroutineHandle Handle { get; }
    }

    private enum LightingPhase
    {
        None,
        Intro,
        Dark,
        Powered
    }
}
