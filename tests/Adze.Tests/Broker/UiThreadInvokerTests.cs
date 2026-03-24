using System;
using NUnit.Framework;
using Adze.Contracts.Abstractions;
using Adze.Broker.Orchestration;

namespace Adze.Tests.Broker;

[TestFixture]
public class UiThreadInvokerTests
{
    [Test]
    public void SynchronousInvoker_ExecutesAction()
    {
        IUiThreadInvoker invoker = new SynchronousUiThreadInvoker();
        bool executed = false;

        invoker.Invoke(() => executed = true);

        Assert.IsTrue(executed);
    }

    [Test]
    public void SynchronousInvoker_ReturnsValue()
    {
        IUiThreadInvoker invoker = new SynchronousUiThreadInvoker();

        int result = invoker.Invoke(() => 42);

        Assert.AreEqual(42, result);
    }

    [Test]
    public void SynchronousInvoker_PropagatesException()
    {
        IUiThreadInvoker invoker = new SynchronousUiThreadInvoker();

        Assert.Throws<InvalidOperationException>(() =>
            invoker.Invoke(() => throw new InvalidOperationException("boom")));
    }

    [Test]
    public void SynchronousInvoker_PropagatesExceptionFromFunc()
    {
        IUiThreadInvoker invoker = new SynchronousUiThreadInvoker();

        Assert.Throws<InvalidOperationException>(() =>
            invoker.Invoke<int>(() => throw new InvalidOperationException("boom")));
    }

    [Test]
    public void SynchronousInvoker_NullActionThrows()
    {
        IUiThreadInvoker invoker = new SynchronousUiThreadInvoker();

        Assert.Throws<ArgumentNullException>(() => invoker.Invoke((Action)null!));
    }

    [Test]
    public void SynchronousInvoker_NullFuncThrows()
    {
        IUiThreadInvoker invoker = new SynchronousUiThreadInvoker();

        Assert.Throws<ArgumentNullException>(() => invoker.Invoke((Func<int>)null!));
    }

    [Test]
    public void SynchronousInvoker_NestedInvokesWork()
    {
        IUiThreadInvoker invoker = new SynchronousUiThreadInvoker();
        int result = 0;

        invoker.Invoke(() =>
        {
            result = invoker.Invoke(() => 10 + invoker.Invoke(() => 5));
        });

        Assert.AreEqual(15, result);
    }

    [Test]
    public void SynchronousInvoker_ReturnsReferenceType()
    {
        IUiThreadInvoker invoker = new SynchronousUiThreadInvoker();

        string result = invoker.Invoke(() => "hello");

        Assert.AreEqual("hello", result);
    }
}
