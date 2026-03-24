using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Models;

namespace Adze.Tools.Write;

/// <summary>
/// Analyzes feature dependencies for cascade-sensitive write operations.
/// Uses feature tree ordering, type heuristics, and dimension data to
/// identify features that may be affected by suppression or modification.
/// </summary>
public static class DependencyAnalyzer
{
    /// <summary>
    /// Analyzes which features may be affected by suppressing a given feature.
    /// Returns a DependencyPreview with affected features and cascade warnings.
    /// </summary>
    public static DependencyPreview AnalyzeSuppression(
        SessionContext context,
        string featureName)
    {
        var preview = new DependencyPreview { TargetFeatureName = featureName };

        FeatureNode? target = context.FeatureTree.Features
            .FirstOrDefault(f => string.Equals(f.Name, featureName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            preview.Warnings.Add("Feature \"" + featureName + "\" not found in the feature tree.");
            return preview;
        }

        int targetIndex = context.FeatureTree.Features.IndexOf(target);
        preview.TargetKind = target.Kind;
        preview.TargetState = target.State;

        // Find subsequent features that are likely dependents
        var subsequent = context.FeatureTree.Features
            .Skip(targetIndex + 1)
            .Where(f => f.State == "active")
            .ToList();

        // Categorize by dependency likelihood
        foreach (var feature in subsequent)
        {
            string reason = InferDependencyReason(target, feature);
            if (!string.IsNullOrEmpty(reason))
            {
                preview.AffectedFeatures.Add(new AffectedFeature
                {
                    Name = feature.Name,
                    Kind = feature.Kind,
                    Relationship = reason
                });
            }
        }

        // Check if the target is a sketch that other features reference
        if (IsSketchLike(target.Kind))
        {
            int sketchDependents = subsequent.Count(f => IsFeatureBuiltOnSketch(f.Kind));
            if (sketchDependents > 0)
            {
                preview.Warnings.Add("This sketch may be used by " + sketchDependents +
                    " subsequent feature(s). Suppressing it will likely suppress dependent features too.");
                preview.CascadeRisk = CascadeRisk.High;
            }
        }
        // Base features (first few in tree) have high cascade risk
        else if (targetIndex < 3 && subsequent.Count > 3)
        {
            preview.Warnings.Add("This is an early feature in the tree. Suppressing it may cause a cascade " +
                "of errors in " + subsequent.Count + " subsequent features.");
            preview.CascadeRisk = CascadeRisk.High;
        }
        // Features with many dependents
        else if (preview.AffectedFeatures.Count > 3)
        {
            preview.Warnings.Add(preview.AffectedFeatures.Count +
                " features may be affected by this operation.");
            preview.CascadeRisk = CascadeRisk.Medium;
        }
        else
        {
            preview.CascadeRisk = preview.AffectedFeatures.Count > 0
                ? CascadeRisk.Low
                : CascadeRisk.None;
        }

        // Check if target is referenced in dimensions
        foreach (var dim in context.Dimensions.Items)
        {
            if (dim.FullName.IndexOf(featureName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                preview.AffectedDimensions.Add(dim.FullName);
            }
        }
        if (preview.AffectedDimensions.Count > 0)
        {
            preview.Warnings.Add(preview.AffectedDimensions.Count +
                " dimension(s) reference this feature and will become inaccessible.");
        }

        // Check for mates referencing this feature (assembly context)
        foreach (var mate in context.Mates.Items)
        {
            if (mate.Components.Any(c =>
                c.IndexOf(featureName, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                preview.AffectedMates.Add(mate.Name);
            }
        }
        if (preview.AffectedMates.Count > 0)
        {
            preview.Warnings.Add(preview.AffectedMates.Count +
                " mate(s) reference this feature and may become broken.");
            preview.CascadeRisk = CascadeRisk.High;
        }

        return preview;
    }

    /// <summary>
    /// Analyzes which features may be affected by changing a dimension value.
    /// </summary>
    public static DependencyPreview AnalyzeDimensionChange(
        SessionContext context,
        string dimensionFullName)
    {
        var preview = new DependencyPreview { TargetFeatureName = dimensionFullName };

        // Parse the feature name from dimension full name (e.g., "D1@Sketch1" → "Sketch1")
        string? featurePart = ExtractFeatureName(dimensionFullName);
        if (featurePart == null)
        {
            preview.CascadeRisk = CascadeRisk.Low;
            return preview;
        }

        FeatureNode? ownerFeature = context.FeatureTree.Features
            .FirstOrDefault(f => string.Equals(f.Name, featurePart, StringComparison.OrdinalIgnoreCase));

        if (ownerFeature == null)
        {
            preview.CascadeRisk = CascadeRisk.Low;
            return preview;
        }

        int featureIndex = context.FeatureTree.Features.IndexOf(ownerFeature);
        var subsequent = context.FeatureTree.Features
            .Skip(featureIndex + 1)
            .Where(f => f.State == "active")
            .ToList();

        // Dimension changes propagate through rebuild — dependent features will update
        if (subsequent.Count > 5)
        {
            preview.CascadeRisk = CascadeRisk.Medium;
            preview.Warnings.Add("This dimension affects a feature with " + subsequent.Count +
                " subsequent dependent features. All will rebuild.");
        }
        else if (subsequent.Count > 0)
        {
            preview.CascadeRisk = CascadeRisk.Low;
        }
        else
        {
            preview.CascadeRisk = CascadeRisk.None;
        }

        return preview;
    }

    private static string InferDependencyReason(FeatureNode target, FeatureNode candidate)
    {
        string ck = candidate.Kind ?? string.Empty;
        string tk = target.Kind ?? string.Empty;

        // Fillets/chamfers on extrusions
        if (KindMatches(ck, "fillet", "chamfer") && KindMatches(tk, "extrusion", "extrude", "cut", "boss"))
            return "edge dependency (fillet/chamfer on extrusion)";

        // Patterns referencing features
        if (KindMatches(ck, "pattern"))
            return "pattern may reference this feature";

        // Mirror features
        if (KindMatches(ck, "mirror"))
            return "mirror may reference this feature";

        // Shell after extrude
        if (KindMatches(ck, "shell") && KindMatches(tk, "extrusion", "extrude", "boss"))
            return "shell depends on body shape";

        return string.Empty;
    }

    private static bool KindMatches(string kind, params string[] candidates)
    {
        foreach (string c in candidates)
        {
            if (kind.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool IsSketchLike(string kind)
    {
        return kind.IndexOf("sketch", StringComparison.OrdinalIgnoreCase) >= 0 ||
               kind.IndexOf("profile", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsFeatureBuiltOnSketch(string kind)
    {
        return kind.IndexOf("extrude", StringComparison.OrdinalIgnoreCase) >= 0 ||
               kind.IndexOf("extrusion", StringComparison.OrdinalIgnoreCase) >= 0 ||
               kind.IndexOf("cut", StringComparison.OrdinalIgnoreCase) >= 0 ||
               kind.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
               kind.IndexOf("revolve", StringComparison.OrdinalIgnoreCase) >= 0 ||
               kind.IndexOf("sweep", StringComparison.OrdinalIgnoreCase) >= 0 ||
               kind.IndexOf("loft", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? ExtractFeatureName(string dimensionFullName)
    {
        int atIndex = dimensionFullName.IndexOf('@');
        if (atIndex < 0 || atIndex >= dimensionFullName.Length - 1)
            return null;
        return dimensionFullName.Substring(atIndex + 1);
    }
}

public enum CascadeRisk
{
    None,
    Low,
    Medium,
    High
}

public sealed class DependencyPreview
{
    public string TargetFeatureName { get; set; } = string.Empty;
    public string? TargetKind { get; set; }
    public string? TargetState { get; set; }
    public CascadeRisk CascadeRisk { get; set; }
    public List<AffectedFeature> AffectedFeatures { get; set; } = new();
    public List<string> AffectedDimensions { get; set; } = new();
    public List<string> AffectedMates { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class AffectedFeature
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
}
