using System;

namespace Adze.Contracts.Abstractions;

/// <summary>
/// Abstracts UI-thread marshaling for COM calls and other STA-bound operations.
/// All SOLIDWORKS COM access must go through this interface to ensure
/// correct threading (STA/UI thread only).
/// </summary>
public interface IUiThreadInvoker
{
    /// <summary>
    /// Executes the given action synchronously on the UI thread.
    /// If already on the UI thread, executes inline.
    /// </summary>
    void Invoke(Action action);

    /// <summary>
    /// Executes the given function synchronously on the UI thread and returns its result.
    /// If already on the UI thread, executes inline.
    /// </summary>
    T Invoke<T>(Func<T> func);
}
