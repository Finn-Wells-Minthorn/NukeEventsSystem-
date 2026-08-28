using System;
using System.Collections.Generic;
using MEC;

namespace MyFirstPlugin.Events;

public sealed class EventStartSequencePresenter
{
    private CoroutineHandle _sequenceHandle;
    private bool _isCancelled;
    private bool _isRunning;

    public bool IsRunning => _isRunning && _sequenceHandle.IsValid;

    public void Start(Action onStarted, Action onCompleted)
    {
        if (onStarted == null)
            throw new ArgumentNullException(nameof(onStarted));

        if (onCompleted == null)
            throw new ArgumentNullException(nameof(onCompleted));

        Cancel();

        _isCancelled = false;
        _isRunning = true;
        onStarted();
        _sequenceHandle = Timing.RunCoroutine(RunSequence(onCompleted));
    }

    public void Cancel()
    {
        _isCancelled = true;

        if (_sequenceHandle.IsValid)
            Timing.KillCoroutines(_sequenceHandle);

        _sequenceHandle = default;
        _isRunning = false;
    }

    private IEnumerator<float> RunSequence(Action onCompleted)
    {
        try
        {
            if (_isCancelled)
                yield break;

            yield return Timing.WaitForSeconds(0.6f);

            for (int count = 3; count >= 1; count--)
            {
                if (_isCancelled)
                    yield break;

                yield return Timing.WaitForSeconds(0.6f);
            }

            if (_isCancelled)
                yield break;

            yield return Timing.WaitForSeconds(0.35f);

            if (_isCancelled)
                yield break;

            onCompleted();
        }
        finally
        {
            _isCancelled = false;
            _isRunning = false;
            _sequenceHandle = default;
        }
    }
}
