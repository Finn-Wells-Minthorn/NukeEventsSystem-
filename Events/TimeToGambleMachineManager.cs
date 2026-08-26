using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;

namespace MyFirstPlugin.Events;

public sealed class TimeToGambleMachineManager
{
    private readonly List<GamblingMachine> _machines = new();
    private readonly Dictionary<string, uint> _lastKnownUsers = new();
    private CoroutineHandle _monitorHandle;

    public IReadOnlyCollection<GamblingMachine> Machines => _machines;

    public event Action<GamblingMachine, Player>? AuthorizedInteraction;

    public void RegisterMachine(GamblingMachine machine, Workstation workstation)
    {
        if (machine == null)
            throw new ArgumentNullException(nameof(machine));

        if (workstation == null)
            throw new ArgumentNullException(nameof(workstation));

        machine.BindWorkstation(workstation);
        _machines.Add(machine);
    }

    public void Clear()
    {
        _machines.Clear();
        _lastKnownUsers.Clear();
    }

    public void Subscribe()
    {
        if (_monitorHandle.IsValid)
            return;

        _monitorHandle = Timing.CallContinuously(0.1f, CheckWorkstations, () => { });
    }

    public void Unsubscribe()
    {
        if (_monitorHandle.IsValid)
            Timing.KillCoroutines(_monitorHandle);

        _monitorHandle = default;
    }

    private void CheckWorkstations()
    {
        foreach (GamblingMachine machine in _machines)
        {
            Workstation? workstation = machine.BoundWorkstation;
            if (workstation == null || workstation.IsDestroyed)
                continue;

            Player? user = workstation.KnownUser;
            uint userId = user?.NetworkId ?? 0;

            if (!_lastKnownUsers.TryGetValue(machine.Id, out uint previousUserId))
            {
                _lastKnownUsers[machine.Id] = userId;
                continue;
            }

            if (userId == previousUserId)
                continue;

            _lastKnownUsers[machine.Id] = userId;

            if (user != null)
            {
                if (!machine.TryUse(user, out string reason))
                {
                    Console.WriteLine($"[SCPEventSystem] Gamble terminal denied for '{user.Nickname}': {reason}");
                    user.SendHint(reason, 3f);
                    continue;
                }

                try
                {
                    AuthorizedInteraction?.Invoke(machine, user);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SCPEventSystem] Gamble terminal interaction handler failed: {ex.Message}");
                }
            }
        }
    }
}
