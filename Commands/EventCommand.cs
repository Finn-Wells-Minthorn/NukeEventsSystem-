using System;
using System.Linq;
using CommandSystem;
using MyFirstPlugin.Events;

namespace MyFirstPlugin.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public class EventCommand : ICommand
{
    public string Command => "event";

    public string[] Aliases => new[] { "events" };

    public string Description => "Lists available events and starts or stops the active one.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        string[] args = arguments.Array == null ? Array.Empty<string>() : arguments.ToArray();

        if (args.Length == 0)
        {
            response = BuildUsage();
            return true;
        }

        switch (args[0].Trim().ToLowerInvariant())
        {
            case "list":
                response = GetListResponse();
                return true;

            case "current":
                response = GetCurrentResponse();
                return true;

            case "stop":
                response = StopCurrentEvent();
                return true;

            case "start":
                if (args.Length < 2)
                {
                    response = "Usage: /event start <event>\n" + GetListResponse();
                    return true;
                }

                response = StartEvent(GetEventNameArgument(args));
                return true;

            case "enable":
                if (args.Length < 2)
                {
                    response = "Usage: /event enable <event>\n" + GetListResponse();
                    return true;
                }

                response = EnableEvent(GetEventNameArgument(args));
                return true;

            case "disable":
                if (args.Length < 2)
                {
                    response = "Usage: /event disable <event>\n" + GetListResponse();
                    return true;
                }

                response = DisableEvent(GetEventNameArgument(args));
                return true;

            case "auto":
                if (args.Length < 2)
                {
                    response = "Usage: /event auto <on|off>\n" + GetAutoModeResponse();
                    return true;
                }

                response = SetAutomaticMode(args[1]);
                return true;

            case "help":
            default:
                response = BuildUsage();
                return true;
        }
    }

    private static string BuildUsage()
    {
        return "Usage: /event <list|current|start <event>|stop|enable <event>|disable <event>|auto <on|off>>\n" + GetListResponse();
    }

    private static string GetEventNameArgument(string[] args)
    {
        if (args.Length < 2)
            return string.Empty;

        return string.Join(" ", args.Skip(1));
    }

    private static string GetListResponse()
    {
        if (EventManager.RegisteredEvents.Count == 0)
            return "No events are available. Automatic events: " + (global::MyFirstPlugin.MyFirstPlugin.AutomaticEventsEnabled ? "enabled" : "disabled");

        string list = string.Join(
            ", ",
            EventManager.RegisteredEvents.Select(x => $"{x.Name} [{(x.IsEnabled ? "enabled" : "disabled")}]"));

        return "Automatic events: " + (global::MyFirstPlugin.MyFirstPlugin.AutomaticEventsEnabled ? "enabled" : "disabled") + "\nAvailable events: " + list;
    }

    private static string GetCurrentResponse()
    {
        EventBase? current = EventManager.CurrentEvent;
        return current == null ? "No event is currently running." : $"Current event: {current.Name}";
    }

    private static string StopCurrentEvent()
    {
        EventBase? stopped = EventManager.StopCurrentEvent();
        return stopped == null ? "No event is currently running." : $"Stopped event: {stopped.Name}";
    }

    private static string StartEvent(string eventName)
    {
        EventBase? target = EventManager.GetEvent(eventName);
        if (target == null)
        {
            return $"Event '{eventName}' was not found. " + GetListResponse();
        }

        if (!target.IsEnabled)
        {
            return $"Event '{target.Name}' is disabled. Use /event enable {target.Name} first.";
        }

        EventBase? started = EventManager.StartEvent(target);
        return started == null ? $"Failed to start event '{target.Name}'." : $"Started event: {started.Name}";
    }

    private static string GetAutoModeResponse()
    {
        return $"Automatic events are currently {(global::MyFirstPlugin.MyFirstPlugin.AutomaticEventsEnabled ? "enabled" : "disabled")}.";
    }

    private static string SetAutomaticMode(string mode)
    {
        if (global::MyFirstPlugin.MyFirstPlugin.Instance == null)
            return "The plugin is not active, so automatic mode cannot be changed.";

        string normalized = mode.Trim();

        if (string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase))
        {
            global::MyFirstPlugin.MyFirstPlugin.Instance.Config.AutomaticEventsEnabled = true;
            return "Automatic events enabled.";
        }

        if (string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase))
        {
            global::MyFirstPlugin.MyFirstPlugin.Instance.Config.AutomaticEventsEnabled = false;
            return "Automatic events disabled.";
        }

        return "Usage: /event auto <on|off>";
    }

    private static string EnableEvent(string eventName)
    {
        EventBase? target = EventManager.GetEvent(eventName);
        if (target == null)
            return $"Event '{eventName}' was not found. " + GetListResponse();

        if (target.IsEnabled)
            return $"Event '{target.Name}' is already enabled.";

        EventManager.EnableEvent(target);
        return $"Enabled event: {target.Name}";
    }

    private static string DisableEvent(string eventName)
    {
        EventBase? target = EventManager.GetEvent(eventName);
        if (target == null)
            return $"Event '{eventName}' was not found. " + GetListResponse();

        bool wasRunning = target.IsRunning;

        if (!target.IsEnabled && !wasRunning)
            return $"Event '{target.Name}' is already disabled.";

        EventManager.DisableEvent(target);

        return wasRunning
            ? $"Stopped and disabled event: {target.Name}"
            : $"Disabled event: {target.Name}";
    }
}
