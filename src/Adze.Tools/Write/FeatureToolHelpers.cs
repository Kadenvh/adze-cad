using System;
using System.Collections.Generic;
using Adze.Contracts.Models;

namespace Adze.Tools.Write;

/// <summary>
/// Shared SW COM glue for the Phase 11 Wave 2 feature creation tools
/// (extrude, revolve, and the primitives that follow). Mirrors the role
/// SketchToolHelpers plays for the sketch authoring tools.
/// </summary>
internal static class FeatureToolHelpers
{
    /// <summary>
    /// Resolves which sketch a feature tool should consume. When
    /// <paramref name="sketchName"/> is empty, uses the active sketch —
    /// capturing its name and exiting sketch mode so it can be selected as a
    /// profile. Returns null if no sketch can be resolved.
    /// </summary>
    public static string? ResolveProfileSketch(dynamic modelDoc, string? sketchName)
    {
        if (!string.IsNullOrWhiteSpace(sketchName))
        {
            // If the named sketch is currently open for editing, close it —
            // a sketch cannot be selected as a profile while active.
            try
            {
                dynamic? activeSketch = modelDoc.SketchManager.ActiveSketch;
                if (activeSketch != null)
                {
                    string activeName = (string)activeSketch.GetFeature().Name;
                    if (string.Equals(activeName, sketchName, StringComparison.Ordinal))
                    {
                        modelDoc.SketchManager.InsertSketch(true);
                    }
                }
            }
            catch
            {
            }
            return sketchName;
        }

        try
        {
            dynamic? active = modelDoc.SketchManager.ActiveSketch;
            if (active == null)
                return null;
            string name = (string)active.GetFeature().Name;
            // Exit sketch mode (true = rebuild) so the sketch is selectable.
            modelDoc.SketchManager.InsertSketch(true);
            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the SW-assigned name off a freshly created feature and applies
    /// the caller's requested name when one was provided. Never throws —
    /// rename failure keeps the default name.
    /// </summary>
    public static string FinalizeFeatureName(dynamic feature, string? requestedName)
    {
        string actualName = "(unnamed)";
        try
        {
            actualName = (string)feature.Name;
            if (!string.IsNullOrWhiteSpace(requestedName)
                && !string.Equals(actualName, requestedName, StringComparison.Ordinal))
            {
                try
                {
                    feature.Name = requestedName!;
                    actualName = requestedName!;
                }
                catch
                {
                    // Rename failed; keep the SW default name.
                }
            }
        }
        catch
        {
            // Introspection failed; feature still exists.
        }
        return actualName;
    }

    /// <summary>
    /// Appends the named edges/faces to the current selection, trying the
    /// given SelectByID2 type hints in order per entity. Returns the count
    /// of entities that selected successfully.
    /// </summary>
    public static int SelectEntities(dynamic modelDoc, IEnumerable<string> entityNames, string[] typesToTry, int mark)
    {
        int selected = 0;
        foreach (string name in entityNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            foreach (string swType in typesToTry)
            {
                try
                {
                    bool ok = (bool)modelDoc.Extension.SelectByID2(
                        name, swType, 0, 0, 0, true, mark, null, 0);
                    if (ok)
                    {
                        selected++;
                        break;
                    }
                }
                catch
                {
                }
            }
        }
        return selected;
    }

    /// <summary>
    /// Spot-checks the refreshed feature tree for the applied feature name.
    /// Same tolerance as the sketch tools: an unconfirmed change is not a
    /// loud failure because the refreshed snapshot may race the apply.
    /// </summary>
    public static WriteVerification VerifyFeatureInTree(SessionContext refreshedContext, WriteApplyResult applyResult, string featureKindLabel)
    {
        var verification = new WriteVerification { RebuildSucceeded = true };
        if (!applyResult.Success)
        {
            verification.ChangeConfirmed = false;
            return verification;
        }

        if (applyResult.AppliedValues.TryGetValue("feature_name", out string? name))
        {
            foreach (FeatureNode feature in refreshedContext?.FeatureTree?.Features ?? new List<FeatureNode>())
            {
                if (string.Equals(feature.Name, name, StringComparison.Ordinal))
                {
                    verification.ChangeConfirmed = true;
                    verification.ObservedChanges.Add(new StateDiffItem
                    {
                        Path = "feature_tree." + name,
                        BeforeValue = "(missing)",
                        AfterValue = featureKindLabel
                    });
                    return verification;
                }
            }
        }

        verification.ChangeConfirmed = false;
        return verification;
    }
}
