using System;
using System.Collections.Generic;
using MyFirstPlugin.Hints;

namespace MyFirstPlugin.Events;

public abstract class EventBase
{
    private readonly List<IDisposable> _subscriptions = new();
    private readonly List<System.Timers.Timer> _timers = new();
    private readonly List<Action> _cleanupActions = new();

    public abstract string Name { get; }

    public abstract string Description { get; }

    public virtual string DisplayColor => HintUiFormatter.DefaultEventColor;

    public virtual string DisplayDescription => Description;

    public bool IsEnabled { get; private set; } = true;

    public bool IsRunning { get; private set; }

    public void Enable()
    {
        IsEnabled = true;
    }

    public void Disable()
    {
        IsEnabled = false;
    }

    public virtual void Start()
    {
        if (IsRunning)
            return;

        IsRunning = true;

        try
        {
            OnStart();
        }
        catch
        {
            // Best-effort rollback for events that subscribed handlers or started
            // coroutines before their startup failed. Preserve the original error.
            try
            {
                OnStop();
            }
            catch
            {
            }
            finally
            {
                try
                {
                    Cleanup();
                }
                finally
                {
                    IsRunning = false;
                }
            }

            throw;
        }
    }

    public virtual void Stop()
    {
        if (!IsRunning)
            return;

        try
        {
            OnStop();
        }
        finally
        {
            try
            {
                Cleanup();
            }
            finally
            {
                IsRunning = false;
            }
        }
    }

    protected virtual void OnStart()
    {
    }

    protected virtual void OnStop()
    {
    }

    public void TrackTimer(System.Timers.Timer timer)
    {
        if (timer == null)
            throw new ArgumentNullException(nameof(timer));

        _timers.Add(timer);
    }

    public void TrackSubscription(IDisposable subscription)
    {
        if (subscription == null)
            throw new ArgumentNullException(nameof(subscription));

        _subscriptions.Add(subscription);
    }

    public void TrackCleanupAction(Action cleanupAction)
    {
        if (cleanupAction == null)
            throw new ArgumentNullException(nameof(cleanupAction));

        _cleanupActions.Add(cleanupAction);
    }

    protected void Cleanup()
    {
        foreach (System.Timers.Timer timer in _timers)
        {
            try
            {
                timer.Stop();
                timer.Dispose();
            }
            catch
            {
                // Ignore cleanup errors so a single event can fail without corrupting the registry.
            }
        }

        foreach (IDisposable subscription in _subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch
            {
                // Ignore cleanup errors so a single event can fail without corrupting the registry.
            }
        }

        foreach (Action cleanupAction in _cleanupActions)
        {
            try
            {
                cleanupAction();
            }
            catch
            {
                // Ignore cleanup errors so a single event can fail without corrupting the registry.
            }
        }

        _timers.Clear();
        _subscriptions.Clear();
        _cleanupActions.Clear();
    }
}
