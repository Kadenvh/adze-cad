using System;
using System.Windows.Forms;
using Adze.Contracts.Abstractions;

namespace Adze.Host.Infrastructure;

/// <summary>
/// Marshals actions to the UI thread via a WinForms Control.
/// Required for all SOLIDWORKS COM calls which must run on the STA/UI thread.
/// </summary>
internal sealed class WinFormsUiThreadInvoker : IUiThreadInvoker
{
    private readonly Control _control;

    public WinFormsUiThreadInvoker(Control control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
    }

    public void Invoke(Action action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        if (_control.InvokeRequired)
        {
            _control.Invoke(action);
        }
        else
        {
            action();
        }
    }

    public T Invoke<T>(Func<T> func)
    {
        if (func == null) throw new ArgumentNullException(nameof(func));

        if (_control.InvokeRequired)
        {
            return (T)_control.Invoke(func);
        }
        else
        {
            return func();
        }
    }
}
