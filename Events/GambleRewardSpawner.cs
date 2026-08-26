using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace MyFirstPlugin.Events;

public sealed class GambleRewardSpawner
{
    private readonly List<Pickup> _spawnedPickups = new();

    public IReadOnlyCollection<Pickup> SpawnedPickups => _spawnedPickups;

    public Pickup? SpawnReward(GambleReward reward, Vector3 position, Quaternion rotation)
    {
        if (reward == null)
            throw new ArgumentNullException(nameof(reward));

        try
        {
            Pickup? pickup = Pickup.Create(reward.ItemType, position, rotation);
            if (pickup == null)
            {
                Console.WriteLine($"[SCPEventSystem] Failed to spawn gamble reward: {reward.DisplayName}");
                return null;
            }

            pickup.Spawn();

            if (!pickup.IsSpawned)
            {
                Console.WriteLine($"[SCPEventSystem] Failed to spawn gamble reward: {reward.DisplayName}");
                return null;
            }

            _spawnedPickups.Add(pickup);
            return pickup;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SCPEventSystem] Failed to spawn gamble reward: {reward.DisplayName} ({ex.Message})");
            return null;
        }
    }

    public void Cleanup()
    {
        foreach (Pickup pickup in _spawnedPickups)
        {
            try
            {
                if (pickup != null && !pickup.IsDestroyed)
                    pickup.Destroy();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SCPEventSystem] Failed to clean up gamble reward pickup: {ex.Message}");
            }
        }

        _spawnedPickups.Clear();
    }
}
