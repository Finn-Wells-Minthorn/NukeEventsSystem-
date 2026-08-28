using System;
using System.Collections.Generic;
using System.Linq;

namespace MyFirstPlugin.Hints;

internal readonly struct BottomInfoContext
{
    public BottomInfoContext(string? eventName, string? eventDescription, string? eventColor)
    {
        EventName = eventName;
        EventDescription = eventDescription;
        EventColor = eventColor;
    }

    public string? EventName { get; }

    public string? EventDescription { get; }

    public string? EventColor { get; }

    public bool HasActiveEvent =>
        !string.IsNullOrWhiteSpace(EventName) && !string.IsNullOrWhiteSpace(EventDescription);
}

internal readonly struct BottomInfoContent
{
    public BottomInfoContent(string text, string? color = null)
    {
        Text = text;
        Color = color;
    }

    public string Text { get; }

    public string? Color { get; }
}

internal interface IBottomInfoProvider
{
    void Reset();

    bool TryGetContent(BottomInfoContext context, out BottomInfoContent content);
}

internal sealed class BottomInfoCycle
{
    private readonly IReadOnlyList<IBottomInfoProvider> _providers;
    private int _nextProviderIndex;

    public BottomInfoCycle(IEnumerable<IBottomInfoProvider> providers)
    {
        if (providers == null)
            throw new ArgumentNullException(nameof(providers));

        _providers = providers.Where(provider => provider != null).ToList();
    }

    public void Reset()
    {
        _nextProviderIndex = 0;

        foreach (IBottomInfoProvider provider in _providers)
            provider.Reset();
    }

    public bool TryGetNext(BottomInfoContext context, out BottomInfoContent content)
    {
        for (int attempt = 0; attempt < _providers.Count; attempt++)
        {
            IBottomInfoProvider provider = _providers[_nextProviderIndex];
            _nextProviderIndex = (_nextProviderIndex + 1) % _providers.Count;

            if (provider.TryGetContent(context, out content) && !string.IsNullOrWhiteSpace(content.Text))
                return true;
        }

        content = default;
        return false;
    }
}

internal sealed class ServerInfoProvider : IBottomInfoProvider
{
    private readonly bool _enabled;
    private readonly string _text;
    private readonly string? _color;

    public ServerInfoProvider(bool enabled, string text, string? color)
    {
        _enabled = enabled;
        _text = text;
        _color = color;
    }

    public void Reset()
    {
    }

    public bool TryGetContent(BottomInfoContext context, out BottomInfoContent content)
    {
        content = new BottomInfoContent(_text, _color);
        return _enabled && !string.IsNullOrWhiteSpace(_text);
    }
}

internal sealed class EventDetailsProvider : IBottomInfoProvider
{
    private readonly bool _enabled;

    public EventDetailsProvider(bool enabled)
    {
        _enabled = enabled;
    }

    public void Reset()
    {
    }

    public bool TryGetContent(BottomInfoContext context, out BottomInfoContent content)
    {
        content = default;
        if (!_enabled || !context.HasActiveEvent)
            return false;

        content = new BottomInfoContent(
            $"{context.EventName}: {context.EventDescription}",
            context.EventColor);
        return true;
    }
}

internal sealed class TipProvider : IBottomInfoProvider
{
    private readonly bool _enabled;
    private readonly IReadOnlyList<string> _tips;
    private readonly string? _color;
    private int _nextTipIndex;

    public TipProvider(bool enabled, IEnumerable<string>? tips, string? color)
    {
        _enabled = enabled;
        _tips = tips?
            .Where(tip => !string.IsNullOrWhiteSpace(tip))
            .Select(tip => tip.Trim())
            .ToList() ?? new List<string>();
        _color = color;
    }

    public void Reset()
    {
        _nextTipIndex = 0;
    }

    public bool TryGetContent(BottomInfoContext context, out BottomInfoContent content)
    {
        content = default;
        if (!_enabled || _tips.Count == 0)
            return false;

        string tip = _tips[_nextTipIndex];
        _nextTipIndex = (_nextTipIndex + 1) % _tips.Count;
        content = new BottomInfoContent($"TIP: {tip}", _color);
        return true;
    }
}

internal sealed class BottomInfoLoopState
{
    private int _generation;

    public bool IsRunning { get; private set; }

    public bool TryStart(out int generation)
    {
        if (IsRunning)
        {
            generation = _generation;
            return false;
        }

        IsRunning = true;
        generation = ++_generation;
        return true;
    }

    public void Stop()
    {
        IsRunning = false;
        ++_generation;
    }

    public bool IsCurrent(int generation) => IsRunning && generation == _generation;
}
