using System;
using System.Net;
using System.Threading;
using Adze.Broker.Infrastructure;

namespace Adze.Broker.Clients;

/// <summary>
/// Detects HTTP 429 rate-limit responses and provides retry-after delays.
/// Tracks active rate limit windows so subsequent requests can queue rather
/// than immediately hitting the API.
/// </summary>
public static class RateLimitHelper
{
    private const int DefaultRetryAfterMs = 2000;
    private const int MaxRetryAfterMs = 15000;
    private static readonly object _lock = new();
    private static DateTimeOffset _rateLimitExpiresUtc = DateTimeOffset.MinValue;

    public static bool IsRateLimited(WebException ex)
    {
        if (ex.Response is HttpWebResponse httpResponse)
        {
            return (int)httpResponse.StatusCode == 429;
        }
        return false;
    }

    public static int GetRetryAfterMs(WebException ex)
    {
        if (ex.Response is HttpWebResponse httpResponse)
        {
            string? retryAfter = httpResponse.Headers["Retry-After"];
            if (!string.IsNullOrWhiteSpace(retryAfter) &&
                int.TryParse(retryAfter, out int seconds) &&
                seconds > 0)
            {
                return Math.Min(seconds * 1000, MaxRetryAfterMs);
            }
        }
        return DefaultRetryAfterMs;
    }

    public static string FormatRateLimitMessage(WebException ex, int retriesRemaining)
    {
        int waitSeconds = GetRetryAfterMs(ex) / 1000;
        if (retriesRemaining > 0)
        {
            return "Rate limited by provider. Retrying in " + waitSeconds + "s...";
        }
        return "Rate limited by provider. Try again in a few seconds.";
    }

    /// <summary>
    /// Sleeps for the retry-after duration. Returns false if cancelled.
    /// Also records the rate limit window for queuing.
    /// </summary>
    public static bool WaitForRetry(WebException ex, CancellationToken ct = default)
    {
        int delayMs = GetRetryAfterMs(ex);
        RecordRateLimitWindow(delayMs);
        try
        {
            if (ct.CanBeCanceled)
            {
                ct.WaitHandle.WaitOne(delayMs);
                return !ct.IsCancellationRequested;
            }
            Thread.Sleep(delayMs);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Records that a rate limit is active for the given duration.
    /// Subsequent calls to WaitIfRateLimited will delay until the window expires.
    /// </summary>
    public static void RecordRateLimitWindow(int durationMs)
    {
        bool opened;
        lock (_lock)
        {
            var newExpiry = DateTimeOffset.UtcNow.AddMilliseconds(durationMs);
            opened = _rateLimitExpiresUtc < DateTimeOffset.UtcNow;
            if (newExpiry > _rateLimitExpiresUtc)
                _rateLimitExpiresUtc = newExpiry;
        }
        if (opened)
        {
            BrokerDiagnostics.Info("RateLimit: window opened duration_ms=" + durationMs);
        }
        else
        {
            BrokerDiagnostics.Info("RateLimit: window extended by " + durationMs + "ms");
        }
    }

    /// <summary>
    /// Returns true if a rate limit window is currently active.
    /// </summary>
    public static bool IsInRateLimitWindow()
    {
        lock (_lock)
        {
            return DateTimeOffset.UtcNow < _rateLimitExpiresUtc;
        }
    }

    /// <summary>
    /// Returns the remaining milliseconds in the current rate limit window, or 0 if none.
    /// </summary>
    public static int GetRemainingWindowMs()
    {
        lock (_lock)
        {
            var remaining = _rateLimitExpiresUtc - DateTimeOffset.UtcNow;
            return remaining.TotalMilliseconds > 0 ? (int)remaining.TotalMilliseconds : 0;
        }
    }

    /// <summary>
    /// If a rate limit window is active, waits until it expires before proceeding.
    /// Returns true if the wait completed, false if cancelled.
    /// </summary>
    public static bool WaitIfRateLimited(CancellationToken ct = default)
    {
        int remainingMs = GetRemainingWindowMs();
        if (remainingMs <= 0)
            return true;

        try
        {
            if (ct.CanBeCanceled)
            {
                ct.WaitHandle.WaitOne(remainingMs);
                return !ct.IsCancellationRequested;
            }
            Thread.Sleep(remainingMs);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Clears the rate limit window. Used for testing.
    /// </summary>
    public static void ResetWindow()
    {
        bool wasOpen;
        lock (_lock)
        {
            wasOpen = DateTimeOffset.UtcNow < _rateLimitExpiresUtc;
            _rateLimitExpiresUtc = DateTimeOffset.MinValue;
        }
        if (wasOpen)
        {
            BrokerDiagnostics.Info("RateLimit: window closed (reset)");
        }
    }
}
