using System;
using System.Collections.Generic;
using CustomPlayerEffects;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using MyFirstPlugin.Config;

namespace MyFirstPlugin.Events;

public sealed class SpeedDemonEvent : EventBase
{
    private readonly SpeedDemonEventConfig _config;
    private readonly Dictionary<uint, MovementBoostState> _affectedPlayers = new();
    private readonly Dictionary<uint, float> _originalStamina = new();
    private readonly Dictionary<uint, float> _lastStamina = new();
    private CoroutineHandle _staminaHandle;
    private bool _subscribed;

    public SpeedDemonEvent(SpeedDemonEventConfig? config = null)
    {
        _config = config ?? new SpeedDemonEventConfig();
    }

    public override string Name => "Speed Demon";

    public override string Description => "Everyone moves at extreme speed. Good luck.";

    protected override void OnStart()
    {
        Server.SendBroadcast(
            "<color=red><b>SPEED DEMON ACTIVATED!</b></color>",
            8
        );

        Subscribe();
        _staminaHandle = Timing.CallContinuously(0.1f, AdjustStamina, () => { });

        foreach (Player player in Player.List)
            ApplyToApplicablePlayer(player);
    }

    protected override void OnStop()
    {
        Unsubscribe();

        if (_staminaHandle.IsValid)
            Timing.KillCoroutines(_staminaHandle);

        _staminaHandle = default;

        foreach (KeyValuePair<uint, MovementBoostState> affectedPlayer in _affectedPlayers)
        {
            Player? player = null;
            foreach (Player candidate in Player.List)
            {
                if (candidate.NetworkId == affectedPlayer.Key)
                {
                    player = candidate;
                    break;
                }
            }

            if (player == null || player.IsDestroyed)
                continue;

            RestorePlayer(player, affectedPlayer.Value);
            if (_originalStamina.TryGetValue(affectedPlayer.Key, out float originalStamina))
                player.StaminaRemaining = originalStamina;
        }

        _affectedPlayers.Clear();
        _originalStamina.Clear();
        _lastStamina.Clear();
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        PlayerEvents.Joined += OnPlayerJoined;
        PlayerEvents.Spawned += OnPlayerSpawned;
        PlayerEvents.ChangedRole += OnPlayerChangedRole;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        PlayerEvents.Joined -= OnPlayerJoined;
        PlayerEvents.Spawned -= OnPlayerSpawned;
        PlayerEvents.ChangedRole -= OnPlayerChangedRole;
        _subscribed = false;
    }

    private void OnPlayerJoined(PlayerJoinedEventArgs args)
    {
        ApplyToApplicablePlayer(args.Player);
    }

    private void OnPlayerSpawned(PlayerSpawnedEventArgs args)
    {
        ApplyToApplicablePlayer(args.Player);
    }

    private void OnPlayerChangedRole(PlayerChangedRoleEventArgs args)
    {
        ApplyToApplicablePlayer(args.Player);
    }

    private void ApplyToApplicablePlayer(Player? player)
    {
        if (player == null || player.IsDestroyed || (!player.IsHuman && !player.IsSCP))
            return;

        if (!_affectedPlayers.ContainsKey(player.NetworkId))
        {
            MovementBoost? existingEffect = player.GetEffect<MovementBoost>();
            _affectedPlayers[player.NetworkId] = new MovementBoostState(
                existingEffect != null && existingEffect.IsEnabled,
                existingEffect?.Intensity ?? 0,
                existingEffect?.TimeLeft ?? 0f
            );
            _originalStamina[player.NetworkId] = player.StaminaRemaining;
        }

        _lastStamina[player.NetworkId] = player.StaminaRemaining;

        byte intensity = player.IsSCP ? _config.ScpIntensity : _config.Intensity;

        player.EnableEffect<MovementBoost>(
            intensity,
            _config.DurationSeconds,
            false
        );

    }

    private void AdjustStamina()
    {
        foreach (Player player in Player.List)
        {
            if (player == null || player.IsDestroyed || (!player.IsHuman && !player.IsSCP))
                continue;

            if (!_originalStamina.ContainsKey(player.NetworkId))
                ApplyToApplicablePlayer(player);

            float currentStamina = player.StaminaRemaining;
            if (!_lastStamina.TryGetValue(player.NetworkId, out float previousStamina))
            {
                _lastStamina[player.NetworkId] = currentStamina;
                continue;
            }

            float delta = currentStamina - previousStamina;
            if (delta < 0f)
            {
                currentStamina = previousStamina + (delta * Math.Max(0f, _config.StaminaDrainMultiplier));
            }
            else if (delta > 0f)
            {
                currentStamina = previousStamina + (delta * Math.Max(0f, _config.StaminaRegenerationMultiplier));
            }

            currentStamina = Math.Max(0f, Math.Min(100f, currentStamina));
            player.StaminaRemaining = currentStamina;
            _lastStamina[player.NetworkId] = currentStamina;
        }
    }

    private static void RestorePlayer(Player player, MovementBoostState state)
    {
        if (state.WasEnabled)
        {
            player.EnableEffect<MovementBoost>(
                state.Intensity,
                state.TimeLeft,
                false
            );
        }
        else
        {
            player.DisableEffect<MovementBoost>();
        }
    }

    private readonly struct MovementBoostState
    {
        public MovementBoostState(bool wasEnabled, byte intensity, float timeLeft)
        {
            WasEnabled = wasEnabled;
            Intensity = intensity;
            TimeLeft = timeLeft;
        }

        public bool WasEnabled { get; }

        public byte Intensity { get; }

        public float TimeLeft { get; }
    }
}
