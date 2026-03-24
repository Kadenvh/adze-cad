using System;
using Adze.Contracts.Abstractions;

namespace Adze.Broker.Orchestration;

/// <summary>
/// Executes actions inline on the calling thread.
/// Used in unit tests and deterministic fallback paths where no UI thread exists.
/// </summary>
public sealed class SynchronousUiThreadInvoker : IUiThreadInvoker
{
    public void Invoke(Action action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        action();
    }

    public T Invoke<T>(Func<T> func)
    {
        if (func == null) throw new ArgumentNullException(nameof(func));
        return func();
    }
}
