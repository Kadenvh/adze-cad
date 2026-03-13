using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;

namespace Adze.Trace.Serialization;

public static class ModelJsonMapper
{
    public static Dictionary<string, object?> ToJson(ProgressionState state)
    {
        return new Dictionary<string, object?>
        {
            ["user_id"] = state.UserId,
            ["updated_utc"] = state.UpdatedUtc.ToString("o"),
            ["tool_unlock_tier"] = SerializeToolUnlockTier(state.ToolUnlockTier),
            ["exploration_percent"] = state.ExplorationPercent,
            ["unlocked_tools"] = state.UnlockedTools.ToArray(),
            ["achievements"] = state.Achievements
                .Select(achievement => new Dictionary<string, object?>
                {
                    ["achievement_id"] = achievement.AchievementId,
                    ["title"] = achievement.Title,
                    ["unlocked_utc"] = achievement.UnlockedUtc.ToString("o"),
                    ["source_trace_id"] = achievement.SourceTraceId
                })
                .ToArray()
        };
    }

    public static ProgressionState ToProgressionState(Dictionary<string, object> payload, string fallbackUserId)
    {
        var state = new ProgressionState
        {
            UserId = GetString(payload, "user_id", fallbackUserId),
            UpdatedUtc = GetDateTimeOffset(payload, "updated_utc", DateTimeOffset.UtcNow),
            ToolUnlockTier = ParseToolUnlockTier(GetString(payload, "tool_unlock_tier", "baseline")),
            ExplorationPercent = GetDouble(payload, "exploration_percent", 0),
            UnlockedTools = GetStringList(payload, "unlocked_tools")
        };

        foreach (Dictionary<string, object> achievementPayload in GetObjectList(payload, "achievements"))
        {
            state.Achievements.Add(new AchievementState
            {
                AchievementId = GetString(achievementPayload, "achievement_id", string.Empty),
                Title = GetString(achievementPayload, "title", string.Empty),
                UnlockedUtc = GetDateTimeOffset(achievementPayload, "unlocked_utc", DateTimeOffset.UtcNow),
                SourceTraceId = GetNullableString(achievementPayload, "source_trace_id")
            });
        }

        return state;
    }

    public static Dictionary<string, object?> ToJson(TraceEvent traceEvent)
    {
        return new Dictionary<string, object?>
        {
            ["trace_id"] = traceEvent.TraceId,
            ["timestamp_utc"] = traceEvent.TimestampUtc.ToString("o"),
            ["intent"] = traceEvent.Intent,
            ["approval_state"] = SerializeApprovalState(traceEvent.ApprovalState),
            ["tool_sequence"] = traceEvent.ToolSequence.ToArray(),
            ["result"] = new Dictionary<string, object?>
            {
                ["status"] = traceEvent.Result.Status,
                ["summary"] = traceEvent.Result.Summary,
                ["warnings"] = traceEvent.Result.Warnings.ToArray()
            },
            ["achievement_events"] = traceEvent.AchievementEvents.ToArray(),
            ["exploration_percent"] = traceEvent.ExplorationPercent,
            ["tool_unlock_tier"] = SerializeToolUnlockTier(traceEvent.ToolUnlockTier)
        };
    }

    public static Dictionary<string, object?> ToJson(RecipeCandidate candidate)
    {
        return new Dictionary<string, object?>
        {
            ["recipe_id"] = candidate.RecipeId,
            ["title"] = candidate.Title,
            ["intent"] = candidate.Intent,
            ["source_trace_ids"] = candidate.SourceTraceIds.ToArray(),
            ["tool_sequence"] = candidate.ToolSequence.ToArray(),
            ["promotion_state"] = candidate.PromotionState,
            ["reliability_score"] = candidate.ReliabilityScore,
            ["required_unlock_tier"] = SerializeToolUnlockTier(candidate.RequiredUnlockTier),
            ["review_notes"] = candidate.ReviewNotes.ToArray()
        };
    }

    public static Dictionary<string, object?> ToJson(GroundingSnapshotRecord snapshot)
    {
        return new Dictionary<string, object?>
        {
            ["reason"] = snapshot.Reason,
            ["timestamp_utc"] = snapshot.TimestampUtc.ToString("o"),
            ["context"] = ToJson(snapshot.Context),
            ["tool_results"] = snapshot.ToolResults.Select(ToJson).ToArray(),
            ["achievement_count"] = snapshot.AchievementCount,
            ["review_ready_recipe_count"] = snapshot.ReviewReadyRecipeCount,
            ["latest_achievement_title"] = snapshot.LatestAchievementTitle
        };
    }

    public static Dictionary<string, object?> ToJson(SessionContext context)
    {
        return new Dictionary<string, object?>
        {
            ["session"] = new Dictionary<string, object?>
            {
                ["request_id"] = context.Session.RequestId,
                ["timestamp_utc"] = context.Session.TimestampUtc.ToString("o"),
                ["approval_state"] = SerializeApprovalState(context.Session.ApprovalState),
                ["user_mode"] = context.Session.UserMode
            },
            ["environment"] = new Dictionary<string, object?>
            {
                ["solidworks_version"] = context.Environment.SolidWorksVersion,
                ["addin_version"] = context.Environment.AddInVersion,
                ["machine_name"] = context.Environment.MachineName,
                ["document_manager_available"] = context.Environment.DocumentManagerAvailable,
                ["diagnostics_mode"] = context.Environment.DiagnosticsMode
            },
            ["document"] = context.Document == null
                ? null
                : new Dictionary<string, object?>
                {
                    ["type"] = context.Document.Type,
                    ["title"] = context.Document.Title,
                    ["path"] = context.Document.Path,
                    ["active_configuration"] = context.Document.ActiveConfiguration,
                    ["units"] = context.Document.Units,
                    ["is_dirty"] = context.Document.IsDirty,
                    ["is_read_only"] = context.Document.IsReadOnly
                },
            ["selection"] = new Dictionary<string, object?>
            {
                ["count"] = context.Selection.Count,
                ["items"] = context.Selection.Items
                    .Select(item => new Dictionary<string, object?>
                    {
                        ["kind"] = item.Kind,
                        ["name"] = item.Name,
                        ["owner"] = item.Owner
                    })
                    .ToArray()
            },
            ["feature_tree"] = new Dictionary<string, object?>
            {
                ["anchor"] = context.FeatureTree.Anchor,
                ["radius"] = context.FeatureTree.Radius,
                ["features"] = context.FeatureTree.Features
                    .Select(feature => new Dictionary<string, object?>
                    {
                        ["name"] = feature.Name,
                        ["kind"] = feature.Kind,
                        ["state"] = feature.State
                    })
                    .ToArray()
            },
            ["configurations"] = new Dictionary<string, object?>
            {
                ["active_name"] = context.Configurations.ActiveName,
                ["count"] = context.Configurations.Count,
                ["items"] = context.Configurations.Items
                    .Select(item => new Dictionary<string, object?>
                    {
                        ["name"] = item.Name,
                        ["is_active"] = item.IsActive
                    })
                    .ToArray()
            },
            ["dimensions"] = new Dictionary<string, object?>
            {
                ["count"] = context.Dimensions.Count,
                ["items"] = context.Dimensions.Items
                    .Select(item => new Dictionary<string, object?>
                    {
                        ["name"] = item.Name,
                        ["full_name"] = item.FullName,
                        ["value"] = item.Value,
                        ["unit_source"] = item.UnitSource
                    })
                    .ToArray()
            },
            ["mates"] = new Dictionary<string, object?>
            {
                ["count"] = context.Mates.Count,
                ["items"] = context.Mates.Items
                    .Select(item => new Dictionary<string, object?>
                    {
                        ["name"] = item.Name,
                        ["kind"] = item.Kind,
                        ["entity_count"] = item.EntityCount,
                        ["components"] = item.Components.ToArray()
                    })
                    .ToArray()
            },
            ["reference_graph"] = new Dictionary<string, object?>
            {
                ["direct_count"] = context.ReferenceGraph.DirectCount,
                ["transitive_count"] = context.ReferenceGraph.TransitiveCount,
                ["broken_reference_count"] = context.ReferenceGraph.BrokenReferenceCount,
                ["direct_items"] = context.ReferenceGraph.DirectItems
                    .Select(item => new Dictionary<string, object?>
                    {
                        ["name"] = item.Name,
                        ["path"] = item.Path,
                        ["imported_path"] = item.ImportedPath,
                        ["is_read_only"] = item.IsReadOnly,
                        ["exists_on_disk"] = item.ExistsOnDisk,
                        ["is_broken"] = item.IsBroken
                    })
                    .ToArray(),
                ["transitive_items"] = context.ReferenceGraph.TransitiveItems
                    .Select(item => new Dictionary<string, object?>
                    {
                        ["name"] = item.Name,
                        ["path"] = item.Path,
                        ["imported_path"] = item.ImportedPath,
                        ["is_read_only"] = item.IsReadOnly,
                        ["exists_on_disk"] = item.ExistsOnDisk,
                        ["is_broken"] = item.IsBroken
                    })
                    .ToArray()
            },
            ["properties"] = context.Properties,
            ["diagnostics"] = new Dictionary<string, object?>
            {
                ["rebuild_state"] = context.Diagnostics.RebuildState,
                ["warnings"] = context.Diagnostics.Warnings.ToArray(),
                ["missing_references"] = context.Diagnostics.MissingReferences.ToArray()
            },
            ["policy"] = new Dictionary<string, object?>
            {
                ["enabled_tools"] = context.Policy.EnabledTools.ToArray(),
                ["tool_unlock_tier"] = SerializeToolUnlockTier(context.Policy.ToolUnlockTier),
                ["exploration_percent"] = context.Policy.ExplorationPercent
            }
        };
    }

    public static Dictionary<string, object?> ToJson(ToolResult result)
    {
        return new Dictionary<string, object?>
        {
            ["tool_name"] = result.ToolName,
            ["success"] = result.Success,
            ["summary"] = result.Summary,
            ["warnings"] = result.Warnings.ToArray(),
            ["data"] = result.Data
        };
    }

    public static RecipeCandidate ToRecipeCandidate(Dictionary<string, object> payload, string fallbackRecipeId)
    {
        return new RecipeCandidate
        {
            RecipeId = GetString(payload, "recipe_id", fallbackRecipeId),
            Title = GetString(payload, "title", fallbackRecipeId),
            Intent = GetString(payload, "intent", string.Empty),
            SourceTraceIds = GetStringList(payload, "source_trace_ids"),
            ToolSequence = GetStringList(payload, "tool_sequence"),
            PromotionState = GetString(payload, "promotion_state", "candidate"),
            ReliabilityScore = GetDouble(payload, "reliability_score", 0),
            RequiredUnlockTier = ParseToolUnlockTier(GetString(payload, "required_unlock_tier", "baseline")),
            ReviewNotes = GetStringList(payload, "review_notes")
        };
    }

    private static string GetString(Dictionary<string, object> payload, string key, string fallback)
    {
        if (!payload.TryGetValue(key, out object? value) || value == null)
        {
            return fallback;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback;
    }

    private static string? GetNullableString(Dictionary<string, object> payload, string key)
    {
        if (!payload.TryGetValue(key, out object? value) || value == null)
        {
            return null;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static double GetDouble(Dictionary<string, object> payload, string key, double fallback)
    {
        if (!payload.TryGetValue(key, out object? value) || value == null)
        {
            return fallback;
        }

        return value switch
        {
            double doubleValue => doubleValue,
            decimal decimalValue => (double)decimalValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            _ => double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : fallback
        };
    }

    private static DateTimeOffset GetDateTimeOffset(Dictionary<string, object> payload, string key, DateTimeOffset fallback)
    {
        string raw = GetString(payload, key, string.Empty);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : fallback;
    }

    private static List<string> GetStringList(Dictionary<string, object> payload, string key)
    {
        if (!payload.TryGetValue(key, out object? value) || value == null)
        {
            return new List<string>();
        }

        if (value is object[] array)
        {
            return array
                .Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList();
        }

        return new List<string>();
    }

    private static List<Dictionary<string, object>> GetObjectList(Dictionary<string, object> payload, string key)
    {
        if (!payload.TryGetValue(key, out object? value) || value == null)
        {
            return new List<Dictionary<string, object>>();
        }

        if (value is object[] array)
        {
            return array
                .OfType<Dictionary<string, object>>()
                .ToList();
        }

        return new List<Dictionary<string, object>>();
    }

    private static string SerializeApprovalState(ApprovalState state)
    {
        return state switch
        {
            ApprovalState.Draft => "draft",
            ApprovalState.PreviewReady => "preview_ready",
            ApprovalState.AwaitingConfirmation => "awaiting_confirmation",
            ApprovalState.Approved => "approved",
            ApprovalState.Executing => "executing",
            ApprovalState.Verifying => "verifying",
            ApprovalState.Completed => "completed",
            ApprovalState.RolledBack => "rolled_back",
            ApprovalState.Failed => "failed",
            _ => "draft"
        };
    }

    private static string SerializeToolUnlockTier(ToolUnlockTier tier)
    {
        return tier switch
        {
            ToolUnlockTier.Baseline => "baseline",
            ToolUnlockTier.Assisted => "assisted",
            ToolUnlockTier.Reviewed => "reviewed",
            ToolUnlockTier.Trusted => "trusted",
            _ => "baseline"
        };
    }

    private static ToolUnlockTier ParseToolUnlockTier(string value)
    {
        return value switch
        {
            "baseline" => ToolUnlockTier.Baseline,
            "assisted" => ToolUnlockTier.Assisted,
            "reviewed" => ToolUnlockTier.Reviewed,
            "trusted" => ToolUnlockTier.Trusted,
            _ => ToolUnlockTier.Baseline
        };
    }
}
