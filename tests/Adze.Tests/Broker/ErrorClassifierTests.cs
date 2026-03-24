using System;
using System.Net;
using NUnit.Framework;
using Adze.Broker.Orchestration;

namespace Adze.Tests.Broker;

[TestFixture]
public class ErrorClassifierTests
{
    [Test]
    public void Classify_OperationCancelled_ReturnsHostTier()
    {
        var ex = new OperationCanceledException("Cancelled");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.HostError, result.Tier);
        Assert.That(result.UserMessage, Does.Contain("cancelled"));
    }

    [Test]
    public void Classify_RateLimitMessage_ReturnsApiTier()
    {
        var ex = new Exception("HTTP 429 Too Many Requests");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.ApiError, result.Tier);
        Assert.That(result.UserMessage, Does.Contain("Rate limited"));
    }

    [Test]
    public void Classify_RateLimitedByProvider_ReturnsApiTier()
    {
        var ex = new Exception("Rate limited by provider. Try again in a few seconds.");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.ApiError, result.Tier);
    }

    [Test]
    public void Classify_AuthError_ReturnsApiTier()
    {
        var ex = new Exception("401 Unauthorized: Invalid API key");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.ApiError, result.Tier);
        Assert.That(result.UserMessage, Does.Contain("Authentication"));
        Assert.That(result.Guidance, Does.Contain("API key"));
    }

    [Test]
    public void Classify_ForbiddenError_ReturnsApiTier()
    {
        var ex = new Exception("403 Forbidden");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.ApiError, result.Tier);
    }

    [Test]
    public void Classify_TimeoutException_ReturnsApiTier()
    {
        var ex = new TimeoutException("Request timed out");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.ApiError, result.Tier);
        Assert.That(result.UserMessage, Does.Contain("too long"));
    }

    [Test]
    public void Classify_TimeoutMessage_ReturnsApiTier()
    {
        var ex = new Exception("The operation timed out after 30 seconds");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.ApiError, result.Tier);
    }

    [Test]
    public void Classify_WebException_ReturnsApiTier()
    {
        var ex = new WebException("Unable to connect to the remote server");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.ApiError, result.Tier);
        Assert.That(result.UserMessage, Does.Contain("reach"));
    }

    [Test]
    public void Classify_ConnectionRefused_ReturnsApiTier()
    {
        var ex = new Exception("Connection refused to localhost:11434");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.ApiError, result.Tier);
        Assert.That(result.Guidance, Does.Contain("local model"));
    }

    [Test]
    public void Classify_ServerError_ReturnsApiTier()
    {
        var ex = new Exception("HTTP 500 Internal Server Error");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.ApiError, result.Tier);
        Assert.That(result.Guidance, Does.Contain("temporary"));
    }

    [Test]
    public void Classify_COMError_ReturnsHostTier()
    {
        var ex = new Exception("COM object call failed on ActiveDoc");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.HostError, result.Tier);
        Assert.That(result.UserMessage, Does.Contain("SOLIDWORKS"));
        Assert.That(result.Guidance, Does.Contain("document"));
    }

    [Test]
    public void Classify_InvalidOperation_ReturnsHostTier()
    {
        var ex = new InvalidOperationException("Something is wrong");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.HostError, result.Tier);
    }

    [Test]
    public void Classify_UnknownError_ReturnsHostTier()
    {
        var ex = new Exception("Random unknown thing happened");
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.AreEqual(ErrorTier.HostError, result.Tier);
        Assert.That(result.UserMessage, Does.Contain("went wrong"));
    }

    [Test]
    public void FormatForUser_IncludesGuidance()
    {
        var error = new ClassifiedError
        {
            Tier = ErrorTier.ApiError,
            UserMessage = "Rate limited.",
            Guidance = "Try again in a few seconds."
        };

        string text = ErrorClassifier.FormatForUser(error);
        Assert.That(text, Does.Contain("Rate limited."));
        Assert.That(text, Does.Contain("Try again"));
    }

    [Test]
    public void FormatForUser_NoGuidance_JustMessage()
    {
        var error = new ClassifiedError
        {
            Tier = ErrorTier.HostError,
            UserMessage = "Something went wrong."
        };

        string text = ErrorClassifier.FormatForUser(error);
        Assert.AreEqual("Something went wrong.", text);
    }

    [Test]
    public void Classify_NeverContainsStackTrace()
    {
        var ex = new Exception("Outer error", new Exception("Inner error"));
        ClassifiedError result = ErrorClassifier.Classify(ex);
        string formatted = ErrorClassifier.FormatForUser(result);

        Assert.That(formatted, Does.Not.Contain("at "));
        Assert.That(formatted, Does.Not.Contain("StackTrace"));
        Assert.That(formatted, Does.Not.Contain(".cs:line"));
    }

    [Test]
    public void Classify_TruncatesLongTechnicalDetail()
    {
        string longMessage = new string('x', 500);
        var ex = new InvalidOperationException(longMessage);
        ClassifiedError result = ErrorClassifier.Classify(ex);

        Assert.IsNotNull(result.TechnicalDetail);
        Assert.LessOrEqual(result.TechnicalDetail!.Length, 250);
    }
}
