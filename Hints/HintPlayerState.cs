using System.Collections.Generic;

namespace MyFirstPlugin.Hints;

internal sealed class HintPlayerState
{
    private readonly Dictionary<HintElementId, HintElement> _elements = new();
    private int _externalHintGeneration;
    private ulong _externalHintFingerprint;

    public IEnumerable<HintElement> Elements => _elements.Values;

    public int ElementCount => _elements.Count;

    public int ExternalHintGeneration => _externalHintGeneration;

    public bool IsExternalHintActive { get; private set; }

    public bool IsOwnedHintVisible { get; set; }

    public string? LastRenderedContent { get; set; }

    public bool Set(HintElement element)
    {
        if (_elements.TryGetValue(element.Id, out HintElement? existing) && existing.Equals(element))
            return false;

        _elements[element.Id] = element;
        return true;
    }

    public bool Remove(HintElementId id) => _elements.Remove(id);

    public void ClearElements() => _elements.Clear();

    public bool IsSameExternalHint(ulong fingerprint) =>
        IsExternalHintActive && _externalHintFingerprint == fingerprint;

    public int BeginExternalHint(ulong fingerprint = 0UL)
    {
        IsExternalHintActive = true;
        IsOwnedHintVisible = false;
        _externalHintFingerprint = fingerprint;
        return ++_externalHintGeneration;
    }

    public bool CompleteExternalHint(int generation)
    {
        if (!IsExternalHintActive || generation != _externalHintGeneration)
            return false;

        IsExternalHintActive = false;
        return true;
    }

    public void CancelExternalHint()
    {
        ++_externalHintGeneration;
        IsExternalHintActive = false;
    }
}

internal sealed class HintStateRegistry
{
    private readonly Dictionary<uint, HintPlayerState> _states = new();

    public int Count => _states.Count;

    public HintPlayerState GetOrCreate(uint networkId)
    {
        if (!_states.TryGetValue(networkId, out HintPlayerState? state))
        {
            state = new HintPlayerState();
            _states.Add(networkId, state);
        }

        return state;
    }

    public bool TryGet(uint networkId, out HintPlayerState state)
    {
        if (_states.TryGetValue(networkId, out HintPlayerState? existing))
        {
            state = existing;
            return true;
        }

        state = null!;
        return false;
    }

    public bool Remove(uint networkId) => _states.Remove(networkId);

    public List<KeyValuePair<uint, HintPlayerState>> Snapshot() => new(_states);

    public void Clear() => _states.Clear();
}
