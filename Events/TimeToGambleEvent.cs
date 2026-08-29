using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using MyFirstPlugin.Config;
using UnityEngine;

namespace MyFirstPlugin.Events;

public class TimeToGambleEvent : EventBase
{
    private readonly TimeToGambleMachineManager _machineManager = new();
    private readonly GambleRewardSpawner _rewardSpawner = new();
    private readonly TimeToGambleEventConfig _config;

    public TimeToGambleEvent(TimeToGambleEventConfig? config = null)
    {
        _config = config ?? new TimeToGambleEventConfig();

        if (!_config.Enabled)
            Disable();
    }

    public override string Name => "Time To Gamble (Development)";

    protected override EventDisplayConfig? DisplayConfig => _config.Display;

    protected override void OnStart()
    {
        _machineManager.Unsubscribe();
        _machineManager.Clear();
        _rewardSpawner.Cleanup();
        _machineManager.AuthorizedInteraction -= OnAuthorizedTerminalInteraction;
        _machineManager.AuthorizedInteraction += OnAuthorizedTerminalInteraction;

        Room? targetRoom = ResolveTargetRoom();
        if (targetRoom == null)
        {
            Console.WriteLine($"[TimeToGambleEvent] Failed to find target room '{_config.TargetRoomName}'. No gamble terminal was registered.");
            return;
        }

        int targetWorkstationIndex = Math.Max(0, _config.TargetWorkstationIndex);
        Workstation? targetWorkstation = Workstation.List
            .Where(workstation => workstation.Room != null && workstation.Room.Name == _config.TargetRoomName)
            .ElementAtOrDefault(targetWorkstationIndex);

        if (targetWorkstation == null)
        {
            Console.WriteLine($"[SCPEventSystem] No existing workstation found in room '{targetRoom.Name}' at resolved index {targetWorkstationIndex}.");
            return;
        }

        GamblingMachine gambleMachine = new GamblingMachine(
            "gamble-terminal",
            GamblingMachineTeamType.Mtf,
            5f
        );

        _machineManager.RegisterMachine(gambleMachine, targetWorkstation);
        _machineManager.Subscribe();
        Console.WriteLine(
            $"[SCPEventSystem] Time To Gamble attached configured workstation: " +
            $"room='{targetRoom.Name}', index='{targetWorkstationIndex}'."
        );

        foreach (Player player in Player.List)
        {
            if (player == null || !player.IsHuman || !player.IsAlive)
                continue;

            player.ClearInventory(true, true);
        }
    }

    protected override void OnStop()
    {
        _machineManager.Unsubscribe();
        _machineManager.AuthorizedInteraction -= OnAuthorizedTerminalInteraction;
        _machineManager.Clear();
        _rewardSpawner.Cleanup();

        Console.WriteLine("[TimeToGambleEvent] Stopped.");
    }

    private void OnAuthorizedTerminalInteraction(GamblingMachine machine, Player _)
    {
        GambleRewardPool rewardPool = new(_config.Rewards);
        GambleReward? selectedReward = rewardPool.SelectReward();

        if (selectedReward == null)
        {
            Console.WriteLine("[SCPEventSystem] Gamble result: no reward selected because the reward pool is empty or has no positive weights.");
            return;
        }

        Workstation? workstation = machine.BoundWorkstation;
        if (workstation == null || workstation.IsDestroyed)
        {
            Console.WriteLine($"[SCPEventSystem] Failed to spawn gamble reward: {selectedReward.DisplayName}");
            return;
        }

        Vector3 rewardPosition = workstation.Position + _config.RewardSpawnOffset;
        _rewardSpawner.SpawnReward(selectedReward, rewardPosition, Quaternion.identity);
    }

    private Room? ResolveTargetRoom()
    {
        IEnumerable<Room> rooms = Room.Get(_config.TargetRoomName);
        Room? room = rooms.FirstOrDefault();

        if (room == null)
            room = Map.Rooms.FirstOrDefault(r => r.Name == _config.TargetRoomName);

        return room;
    }
}
