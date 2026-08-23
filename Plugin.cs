using LabApi.Events.CustomHandlers;
using LabApi.Features;
using LabApi.Loader;
using LabApi.Loader.Features.Plugins;
using System;
using MyFirstPlugin.Config;
using MyFirstPlugin.Handlers;
using MyFirstPlugin.Events;

namespace MyFirstPlugin;

public class MyFirstPlugin : Plugin<PluginConfig>
{
    public static MyFirstPlugin? Instance { get; private set; }

    public static bool AutomaticEventsEnabled =>
        Instance == null ? true : Instance.Config.AutomaticEventsEnabled;

    public override string Name => "SCP Event System";

    public override string Author => "Your Name";

    public override string Description =>
        "Event system for the server.";

    public override Version Version => new(0, 1, 0);

    public override Version RequiredApiVersion =>
        new(LabApiProperties.CompiledVersion);

    private readonly RoundHandler _roundHandler = new();

    private void RegisterEvents()
    {
        EventManager.Register(new BlackoutEvent(Config.Blackout));
        EventManager.Register(new TimeToGambleEvent(Config.TimeToGamble));
        EventManager.Register(new SpeedDemonEvent(Config.SpeedDemon));
        EventManager.Register(new EscalationEvent(Config.Escalation));
    }

    public override void Enable()
    {
        try
        {
            EventManager.Reset();
            Instance = this;
            RegisterEvents();
            _roundHandler.Activate();
            CustomHandlersManager.RegisterEventsHandler(_roundHandler);
        }
        catch
        {
            CleanupPluginState();
            throw;
        }

        Console.WriteLine("[SCPEventSystem] Enabled!");
    }

    public override void Disable()
    {
        CleanupPluginState();

        Console.WriteLine("[SCPEventSystem] Disabled!");
    }

    private void CleanupPluginState()
    {
        try
        {
            _roundHandler.Deactivate();
        }
        finally
        {
            try
            {
                EventManager.Reset();
            }
            finally
            {
                try
                {
                    CustomHandlersManager.UnregisterEventsHandler(_roundHandler);
                }
                finally
                {
                    try
                    {
                        this.UnregisterCommands();
                    }
                    finally
                    {
                        Instance = null;
                    }
                }
            }
        }
    }
}
