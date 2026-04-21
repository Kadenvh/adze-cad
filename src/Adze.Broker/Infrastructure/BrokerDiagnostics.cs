using System;

namespace Adze.Broker.Infrastructure;

/// <summary>
/// Diagnostic log channel for broker-layer classes that cannot reference
/// <c>Adze.Host.Infrastructure.FileLogger</c> directly. The host subscribes
/// to <see cref="OnInfo"/> once at <c>ConnectToSW</c> and pipes every event
/// to host.log. Broker / Tools / Index classes call <see cref="Info"/>
/// without needing any host dependency.
///
/// <para>
/// Local-only — nothing here is transmitted off the machine. All output
/// lands in <c>%LOCALAPPDATA%\Adze\logs\host.log</c> per the existing
/// privacy contract.
/// </para>
/// </summary>
public static class BrokerDiagnostics
{
    /// <summary>
    /// Fired for every Info-level diagnostic event. Subscribers receive
    /// the formatted message string. Exceptions in subscribers are caught
    /// so a buggy listener cannot cascade a broker-path failure.
    /// </summary>
    public static event Action<string>? OnInfo;

    /// <summary>
    /// Emit a diagnostic message. No-op if nothing is subscribed. Message
    /// prefix conventions used across the codebase:
    /// <list type="bullet">
    /// <item><c>"Settings: ..."</c> — Settings panel actions</item>
    /// <item><c>"Policy: ..."</c> — AgentPolicyEngine denials</item>
    /// <item><c>"Budget: ..."</c> — cost-budget warning/exceeded</item>
    /// <item><c>"RateLimit: ..."</c> — rate-limit window events</item>
    /// <item><c>"HealthCheck: ..."</c> — local provider readiness</item>
    /// <item><c>"ToolProbe: ..."</c> — tool-call capability probe</item>
    /// <item><c>"Streaming: ..."</c> — SSE streaming lifecycle</item>
    /// <item><c>"Clarification: ..."</c> — pre-prompt intent detection</item>
    /// </list>
    /// </summary>
    public static void Info(string message)
    {
        if (message == null) return;
        var handler = OnInfo;
        if (handler == null) return;
        try
        {
            handler(message);
        }
        catch
        {
            // Subscriber exceptions must not cascade to broker callers.
        }
    }
}
