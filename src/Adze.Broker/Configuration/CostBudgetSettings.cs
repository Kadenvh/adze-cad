using System;

namespace Adze.Broker.Configuration;

public sealed class CostBudgetSettings
{
    public int MaxSessionTokens { get; set; } = 500000;

    public int MaxDailyTokens { get; set; } = 2000000;

    public int WarningThresholdPercent { get; set; } = 80;

    public static CostBudgetSettings LoadFromEnvironment()
    {
        return new CostBudgetSettings
        {
            MaxSessionTokens = ReadInteger(
                Environment.GetEnvironmentVariable("SOLIDWORKS_AI_MAX_SESSION_TOKENS"),
                500000),
            MaxDailyTokens = ReadInteger(
                Environment.GetEnvironmentVariable("SOLIDWORKS_AI_MAX_DAILY_TOKENS"),
                2000000),
            WarningThresholdPercent = ReadInteger(
                Environment.GetEnvironmentVariable("SOLIDWORKS_AI_BUDGET_WARNING_PERCENT"),
                80)
        };
    }

    private static int ReadInteger(string? value, int fallback)
    {
        return int.TryParse(value, out int parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}

public sealed class BudgetStatus
{
    public int SessionTokensUsed { get; set; }

    public int SessionTokenLimit { get; set; }

    public int DailyTokensUsed { get; set; }

    public int DailyTokenLimit { get; set; }

    public bool SessionLimitReached => SessionTokensUsed >= SessionTokenLimit;

    public bool DailyLimitReached => DailyTokensUsed >= DailyTokenLimit;

    public bool IsOverBudget => SessionLimitReached || DailyLimitReached;

    public bool IsNearLimit(int warningPercent)
    {
        double sessionRatio = SessionTokenLimit > 0 ? (double)SessionTokensUsed / SessionTokenLimit * 100 : 0;
        double dailyRatio = DailyTokenLimit > 0 ? (double)DailyTokensUsed / DailyTokenLimit * 100 : 0;
        return sessionRatio >= warningPercent || dailyRatio >= warningPercent;
    }

    public string FormatSummary()
    {
        string sessionPct = SessionTokenLimit > 0
            ? ((double)SessionTokensUsed / SessionTokenLimit * 100).ToString("0.0") + "%"
            : "n/a";
        string dailyPct = DailyTokenLimit > 0
            ? ((double)DailyTokensUsed / DailyTokenLimit * 100).ToString("0.0") + "%"
            : "n/a";

        return "Session: " + SessionTokensUsed + "/" + SessionTokenLimit + " (" + sessionPct + ")" +
               " | Daily: " + DailyTokensUsed + "/" + DailyTokenLimit + " (" + dailyPct + ")";
    }
}
