using System;
using HarmonyLib;
using Hints;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using Mirror;

namespace MyFirstPlugin.Hints;

[HarmonyPatch(typeof(HintDisplay), nameof(HintDisplay.Show))]
internal static class HintDisplayPatch
{
    [HarmonyPostfix]
    private static void AfterHintShown(HintDisplay __instance, Hint hint)
    {
        try
        {
            if (!NetworkServer.active || __instance.isLocalPlayer)
                return;

            NetworkConnectionToClient? connection = __instance.netIdentity?.connectionToClient;
            if (connection == null || HintDisplay.SuppressedReceivers.Contains(connection))
                return;

            if (!ReferenceHub.TryGetHub(connection, out ReferenceHub hub))
                return;

            HintPatchBridge.NotifyExternalHint(Player.Get(hub), hint.DurationScalar);
        }
        catch (Exception exception)
        {
            // A compatibility observer must never turn a successfully sent native
            // hint into an exception for the game or another plugin.
            Logger.Error($"[SCPEventSystem] Hint compatibility observer failed: {exception}");
        }
    }
}

internal static class HintPatchBridge
{
    private static HintManager? _manager;

    public static void Attach(HintManager manager)
    {
        if (_manager != null && !ReferenceEquals(_manager, manager))
            throw new InvalidOperationException("Another Nuke Events HintManager is already attached.");

        _manager = manager;
    }

    public static void Detach(HintManager manager)
    {
        if (ReferenceEquals(_manager, manager))
            _manager = null;
    }

    public static void NotifyExternalHint(Player player, float durationSeconds) =>
        _manager?.OnHintShown(player, durationSeconds);
}
