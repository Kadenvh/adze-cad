using System;
using System.Net;
using System.Threading;

namespace Adze.Broker.Clients;

/// <summary>
/// Detects HTTP 429 rate-limit responses and provides retry-after delays.
/// </summary>
public static class RateLimitHelper
{
    private const int DefaultRetryAfterMs = 2000;
    private const int MaxRetryAfterMs = 15000;

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
    /// </summary>
    public static bool WaitForRetry(WebException ex, CancellationToken ct = default)
    {
        int delayMs = GetRetryAfterMs(ex);
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
}
