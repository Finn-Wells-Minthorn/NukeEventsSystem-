using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using MyFirstPlugin.Config;
using PlayerRoles;

namespace MyFirstPlugin.Events;

public sealed class JailbirdMayhemEvent : EventBase
{
    private static readonly ItemType[] ConventionalFirearmAmmunition =
    {
        ItemType.Ammo12gauge,
        ItemType.Ammo556x45,
        ItemType.Ammo44cal,
        ItemType.Ammo762x39,
        ItemType.Ammo9x19
    };

    private readonly JailbirdMayhemEventConfig _config;
    private readonly Dictionary<uint, PlayerLifeState> _playerLives = new();
    private readonly Dictionary<uint, PendingProcessing> _pendingProcessing = new();
    private readonly List<Pickup> _overflowPickups = new();
    private CoroutineHandle _initialPlayerSweepHandle;
    private bool _subscribed;

    public JailbirdMayhemEvent(JailbirdMayhemEventConfig? config = null)
    {
        _config = config ?? new JailbirdMayhemEventConfig();

        if (!_config.Enabled)
            Disable();
    }

    public override string Name => "Jailbird Mayhem";

    public override string Description =>
        "Playable humans keep their utility loadouts but replace spawn firearms and ammunition with Jailbirds.";

    public override string DisplayColor => _config.DisplayColor;

    protected override void OnStart()
    {
        Subscribe();
        ScheduleInitialPlayerSweep();
        SendAnnouncement(_config.StartAnnouncement, _config.StartAnnouncementDurationSeconds);

        Console.WriteLine(
            $"[SCPEventSystem] Jailbird Mayhem activated: delay='{_config.SpawnProcessingDelaySeconds}', " +
            $"removeFirearms='{_config.RemoveFirearms}', " +
            $"removeAmmunition='{_config.RemoveConventionalFirearmAmmunition}', " +
            $"jailbirdAmount='{_config.JailbirdAmount}'."
        );
    }

    protected override void OnStop()
    {
        Unsubscribe();
        CancelInitialPlayerSweep();
        CancelAllPendingProcessing();

        foreach (Pickup pickup in _overflowPickups)
        {
            try
            {
                if (pickup != null && !pickup.IsDestroyed)
                    pickup.Destroy();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[SCPEventSystem] Failed to clean up a Jailbird Mayhem overflow pickup: {ex.Message}"
                );
            }
        }

        _playerLives.Clear();
        _overflowPickups.Clear();
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
        CancelPlayerProcessing(args.Player.NetworkId);
        _playerLives.Remove(args.Player.NetworkId);
    }

    private void OnPlayerLeft(PlayerLeftEventArgs args)
    {
        CancelPlayerProcessing(args.Player.NetworkId);
        _playerLives.Remove(args.Player.NetworkId);
    }

    private void OnPlayerSpawned(PlayerSpawnedEventArgs args)
    {
        ScheduleProcessing(args.Player);
    }

    private void OnPlayerChangedRole(PlayerChangedRoleEventArgs args)
    {
        CancelPlayerProcessing(args.Player.NetworkId);
        _playerLives.Remove(args.Player.NetworkId);

        // Depending on the role-transition path, LabAPI can raise Spawned
        // before ChangedRole. Replace any callback for the previous ordering
        // with one validated callback for the new playable-human life.
        ScheduleProcessing(args.Player);
    }

    private void OnPlayerDeath(PlayerDeathEventArgs args)
    {
        CancelPlayerProcessing(args.Player.NetworkId);
        _playerLives.Remove(args.Player.NetworkId);
    }

    private void ScheduleProcessing(Player? player)
    {
        if (!IsRunning || !IsPlayableHuman(player))
            return;

        uint networkId = player!.NetworkId;
        int lifeId = player.LifeId;

        if (_playerLives.TryGetValue(networkId, out PlayerLifeState? state) &&
            state.LifeId == lifeId &&
            state.Processed)
        {
            return;
        }

        if (_pendingProcessing.TryGetValue(networkId, out PendingProcessing pending))
        {
            if (pending.LifeId == lifeId)
                return;

            CancelPlayerProcessing(networkId);
        }

        float delaySeconds = Math.Max(0.1f, _config.SpawnProcessingDelaySeconds);
        CoroutineHandle handle = Timing.CallDelayed(
            delaySeconds,
            () => CompleteProcessing(networkId, lifeId)
        );

        _pendingProcessing[networkId] = new PendingProcessing(lifeId, handle);
    }

    private void ScheduleInitialPlayerSweep()
    {
        CancelInitialPlayerSweep();

        float delaySeconds = Math.Max(0.1f, _config.SpawnProcessingDelaySeconds);
        _initialPlayerSweepHandle = Timing.CallDelayed(delaySeconds, RunInitialPlayerSweep);
    }

    private void RunInitialPlayerSweep()
    {
        _initialPlayerSweepHandle = default;

        if (!IsRunning)
            return;

        foreach (Player player in Player.List)
        {
            if (IsPlayableHuman(player))
                ProcessCurrentLife(player.NetworkId, player.LifeId);
        }
    }

    private void CompleteProcessing(uint networkId, int lifeId)
    {
        if (!_pendingProcessing.TryGetValue(networkId, out PendingProcessing pending) ||
            pending.LifeId != lifeId)
        {
            return;
        }

        _pendingProcessing.Remove(networkId);

        ProcessCurrentLife(networkId, lifeId);
    }

    private void ProcessCurrentLife(uint networkId, int lifeId)
    {
        if (!IsRunning)
            return;

        Player? player = FindPlayer(networkId);
        if (!IsPlayableHuman(player) || player!.NetworkId != networkId || player.LifeId != lifeId)
            return;

        PlayerLifeState state = GetCurrentLifeState(player);
        if (state.Processed)
            return;

        try
        {
            ProcessSpawnInventory(player);
            state.Processed = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCPEventSystem] Failed to process Jailbird Mayhem loadout for '{player.Nickname}': {ex.Message}"
            );
        }
    }

    private void ProcessSpawnInventory(Player player)
    {
        if (_config.RemoveFirearms)
        {
            foreach (FirearmItem firearm in player.Items.OfType<FirearmItem>().ToArray())
            {
                try
                {
                    player.RemoveItem(firearm);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[SCPEventSystem] Failed to remove firearm '{firearm.Type}' from '{player.Nickname}': {ex.Message}"
                    );
                }
            }
        }

        if (_config.RemoveConventionalFirearmAmmunition)
        {
            foreach (ItemType ammunitionType in ConventionalFirearmAmmunition)
            {
                try
                {
                    player.SetAmmo(ammunitionType, 0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[SCPEventSystem] Failed to remove ammunition '{ammunitionType}' from '{player.Nickname}': {ex.Message}"
                    );
                }
            }
        }

        if (player.Items.Any(item => item.Type == ItemType.Jailbird))
            return;

        int jailbirdAmount = Math.Max(0, _config.JailbirdAmount);
        for (int index = 0; index < jailbirdAmount; index++)
            GrantJailbird(player);
    }

    private void GrantJailbird(Player player)
    {
        try
        {
            if (!player.IsInventoryFull && player.AddItem(ItemType.Jailbird) != null)
                return;

            Pickup? pickup = Pickup.Create(ItemType.Jailbird, player.Position);
            if (pickup == null || !pickup.IsSpawned)
            {
                if (pickup != null && !pickup.IsDestroyed)
                    pickup.Destroy();

                Console.WriteLine(
                    $"[SCPEventSystem] Failed to grant a Jailbird to '{player.Nickname}'."
                );
                return;
            }

            _overflowPickups.Add(pickup);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCPEventSystem] Failed to grant a Jailbird to '{player.Nickname}': {ex.Message}"
            );
        }
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

    private void CancelPlayerProcessing(uint networkId)
    {
        if (!_pendingProcessing.TryGetValue(networkId, out PendingProcessing pending))
            return;

        _pendingProcessing.Remove(networkId);

        try
        {
            if (pending.Handle.IsValid)
                Timing.KillCoroutines(pending.Handle);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCPEventSystem] Failed to cancel Jailbird Mayhem processing for network ID '{networkId}': {ex.Message}"
            );
        }
    }

    private void CancelInitialPlayerSweep()
    {
        CoroutineHandle handle = _initialPlayerSweepHandle;
        _initialPlayerSweepHandle = default;

        try
        {
            if (handle.IsValid)
                Timing.KillCoroutines(handle);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SCPEventSystem] Failed to cancel the Jailbird Mayhem initial-player sweep: {ex.Message}"
            );
        }
    }

    private void CancelAllPendingProcessing()
    {
        foreach (PendingProcessing pending in _pendingProcessing.Values)
        {
            try
            {
                if (pending.Handle.IsValid)
                    Timing.KillCoroutines(pending.Handle);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[SCPEventSystem] Failed to cancel Jailbird Mayhem processing: {ex.Message}"
                );
            }
        }

        _pendingProcessing.Clear();
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

        public bool Processed { get; set; }
    }

    private readonly struct PendingProcessing
    {
        public PendingProcessing(int lifeId, CoroutineHandle handle)
        {
            LifeId = lifeId;
            Handle = handle;
        }

        public int LifeId { get; }

        public CoroutineHandle Handle { get; }
    }
}
