using System;
using System.Globalization;
using System.Linq;
using CommandSystem;
using LabApi.Features.Wrappers;
using MyFirstPlugin.Hints;

namespace MyFirstPlugin.Commands;

// Narrow Remote Admin-only harness for Phase 3 live visual verification.
[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class HintTestCommand : ICommand
{
    private const float DefaultVerticalPosition = 750f;

    public string Command => "nukehinttest";

    public string[] Aliases => Array.Empty<string>();

    public string Description => "Developer harness for the internal Nuke Events hint framework.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        string[] args = arguments.Array == null ? Array.Empty<string>() : arguments.ToArray();
        if (args.Length < 2 || !int.TryParse(args[0], out int playerId))
        {
            response = BuildUsage();
            return false;
        }

        HintManager? manager = global::MyFirstPlugin.MyFirstPlugin.Hints;
        if (manager == null || !manager.IsEnabled)
        {
            response = "The internal hint framework is not active.";
            return false;
        }

        Player? player = Player.Get(playerId);
        if (player == null || player.IsDestroyed || player.IsHost)
        {
            response = $"Player ID {playerId} is not an online client.";
            return false;
        }

        if (!TryGetPosition(args, out float verticalPosition, out response))
            return false;

        switch (args[1].Trim().ToLowerInvariant())
        {
            case "show":
                manager.Set(
                    player,
                    HintElementId.ManualTestPrimary,
                    "<color=#ff4040><b>NUKE EVENTS HINT TEST</b></color>",
                    verticalPosition);
                response = $"Displayed the primary test element at {verticalPosition:0.###}.";
                return true;

            case "update":
                manager.Set(
                    player,
                    HintElementId.ManualTestPrimary,
                    "<color=#40ff80><b>NUKE EVENTS HINT TEST UPDATED</b></color>",
                    verticalPosition);
                response = $"Replaced the primary test element at {verticalPosition:0.###}.";
                return true;

            case "multi":
                manager.Set(
                    player,
                    HintElementId.ManualTestPrimary,
                    "<color=#ff4040><b>NUKE EVENTS HINT TEST</b></color>",
                    verticalPosition);
                manager.Set(
                    player,
                    HintElementId.ManualTestSecondary,
                    "<color=#80c0ff>SECOND TEST ELEMENT</color>",
                    Math.Max(0f, verticalPosition - 55f));
                response = "Displayed two independently tagged test elements.";
                return true;

            case "remove":
                response = manager.Remove(player, HintElementId.ManualTestPrimary)
                    ? "Removed the primary test element; other elements were left intact."
                    : "The primary test element was not present.";
                return true;

            case "clear":
                manager.Clear(player);
                response = "Cleared all Nuke Events hints for the player.";
                return true;

            case "status":
                response = manager.GetDiagnosticStatus(player);
                return true;

            default:
                response = BuildUsage();
                return false;
        }
    }

    private static bool TryGetPosition(string[] args, out float position, out string response)
    {
        position = DefaultVerticalPosition;
        response = string.Empty;

        if (args.Length < 3)
            return true;

        if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out position) ||
            float.IsNaN(position) || float.IsInfinity(position) || position < 0f || position > 1000f)
        {
            response = "Vertical position must be a number from 0 (bottom) to 1000 (top).";
            return false;
        }

        return true;
    }

    private static string BuildUsage() =>
        "Usage: nukehinttest <playerId> <show|update|multi|remove|clear|status> [verticalPosition]";
}
