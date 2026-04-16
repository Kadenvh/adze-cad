using System;
using System.Collections.Generic;

namespace Adze.Broker.Configuration;

public static class FeatureGateRegistry
{
    public const string AgentLoop = "SOLIDWORKS_AI_AGENT_LOOP";
    public const string FirstWaveWrites = "SOLIDWORKS_AI_FIRST_WAVE_WRITES";
    public const string Retrieval = "SOLIDWORKS_AI_RETRIEVAL";
    public const string LocalModels = "SOLIDWORKS_AI_LOCAL_MODELS";
    public const string StreamFinalText = "SOLIDWORKS_AI_STREAM_FINAL_TEXT";
    public const string RibbonTab = "SOLIDWORKS_AI_RIBBON";
    public const string ContextMenu = "SOLIDWORKS_AI_CONTEXT_MENU";

    private static readonly string[] AllGates =
    {
        AgentLoop,
        FirstWaveWrites,
        Retrieval,
        LocalModels,
        StreamFinalText,
        RibbonTab,
        ContextMenu
    };

    public static bool IsEnabled(string gateName)
    {
        string? value = Environment.GetEnvironmentVariable(gateName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "1" or "true" or "yes" or "on";
    }

    public static Dictionary<string, bool> GetAllStates()
    {
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string gate in AllGates)
        {
            states[gate] = IsEnabled(gate);
        }

        return states;
    }

    public static string FormatSummary()
    {
        var parts = new List<string>();
        foreach (string gate in AllGates)
        {
            string shortName = gate.Replace("SOLIDWORKS_AI_", "");
            parts.Add(shortName + "=" + (IsEnabled(gate) ? "on" : "off"));
        }

        return string.Join(" | ", parts);
    }
}
