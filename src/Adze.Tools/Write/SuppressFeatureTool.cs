using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.Tools.Write;

public sealed class SuppressFeatureTool : IWriteTool<SuppressFeatureParameters>
{
    public WritePreview Preview(SessionContext context, SuppressFeatureParameters parameters)
    {
        string configSuffix = string.IsNullOrWhiteSpace(parameters.ConfigurationName)
            ? ""
            : " in configuration \"" + parameters.ConfigurationName + "\"";
        var preview = new WritePreview
        {
            ToolName = Contracts.Tooling.ToolNames.SuppressFeature,
            Summary = "Suppress feature \"" + parameters.FeatureName + "\"" + configSuffix
        };

        FeatureNode? feature = context.FeatureTree.Features
            .FirstOrDefault(f =>
                string.Equals(f.Name, parameters.FeatureName, StringComparison.OrdinalIgnoreCase));

        if (feature != null)
        {
            if (feature.State == "suppressed")
            {
                preview.Warnings.Add("Feature \"" + parameters.FeatureName + "\" is already suppressed.");
            }

            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = feature.Name,
                BeforeValue = feature.State,
                AfterValue = "suppressed"
            });

            // Dependency analysis is now handled by DependencyAnalyzer below
        }
        else
        {
            preview.Warnings.Add("Feature \"" + parameters.FeatureName + "\" was not found in the feature tree.");
            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = parameters.FeatureName,
                BeforeValue = "(unknown)",
                AfterValue = "suppressed"
            });
        }

        // Enrich with dependency analysis
        DependencyPreview depPreview = DependencyAnalyzer.AnalyzeSuppression(context, parameters.FeatureName);
        foreach (string warning in depPreview.Warnings)
        {
            if (!preview.Warnings.Contains(warning))
                preview.Warnings.Add(warning);
        }

        if (context.Document?.IsReadOnly == true)
        {
            preview.Warnings.Add("Document is read-only. The suppression change may not be saved.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, SuppressFeatureParameters parameters)
    {
        try
        {
            dynamic swApp = application;
            dynamic modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "No active document."
                };
            }

            dynamic feature = modelDoc.FeatureByName(parameters.FeatureName);
            if (feature == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Feature \"" + parameters.FeatureName + "\" not found."
                };
            }

            // SetSuppression2: 0 = Suppressed, 1 = Resolved
            // swInConfigurationOpts: 1 = This configuration, 2 = All configurations
            int configOption = string.IsNullOrWhiteSpace(parameters.ConfigurationName) ? 2 : 1;
            string[]? configNames = string.IsNullOrWhiteSpace(parameters.ConfigurationName)
                ? null
                : new[] { parameters.ConfigurationName };

            bool result = feature.SetSuppression2(
                0, // swFeatureSuppressionAction_e.swSuppressFeature
                configOption,
                configNames);

            if (!result)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "SOLIDWORKS refused to suppress feature \"" + parameters.FeatureName + "\"."
                };
            }

            modelDoc.ForceRebuild3(false);

            string undoLabel = "Adze: suppress " + parameters.FeatureName;

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = undoLabel,
                AppliedValues = new Dictionary<string, string>
                {
                    [parameters.FeatureName] = "suppressed"
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Failed to suppress feature: " + ex.Message
            };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        var verification = new WriteVerification();

        if (!applyResult.Success)
        {
            verification.ChangeConfirmed = false;
            verification.RebuildSucceeded = false;
            return verification;
        }

        verification.RebuildSucceeded = refreshedContext.Diagnostics.RebuildState != "failed";
        if (refreshedContext.Diagnostics.Warnings.Count > 0)
        {
            verification.RebuildWarnings.AddRange(refreshedContext.Diagnostics.Warnings);
        }

        foreach (KeyValuePair<string, string> applied in applyResult.AppliedValues)
        {
            FeatureNode? feature = refreshedContext.FeatureTree.Features
                .FirstOrDefault(f =>
                    string.Equals(f.Name, applied.Key, StringComparison.OrdinalIgnoreCase));

            if (feature != null && feature.State == "suppressed")
            {
                verification.ChangeConfirmed = true;
                verification.ObservedChanges.Add(new StateDiffItem
                {
                    Path = applied.Key,
                    BeforeValue = "active",
                    AfterValue = "suppressed"
                });
            }
            else
            {
                verification.ChangeConfirmed = false;
            }
        }

        return verification;
    }

    public string BuildUndoLabel(SuppressFeatureParameters parameters)
    {
        return "Adze: suppress " + parameters.FeatureName;
    }
}

public sealed class UnsuppressFeatureTool : IWriteTool<UnsuppressFeatureParameters>
{
    public WritePreview Preview(SessionContext context, UnsuppressFeatureParameters parameters)
    {
        string unsupConfigSuffix = string.IsNullOrWhiteSpace(parameters.ConfigurationName)
            ? ""
            : " in configuration \"" + parameters.ConfigurationName + "\"";
        var preview = new WritePreview
        {
            ToolName = Contracts.Tooling.ToolNames.UnsuppressFeature,
            Summary = "Unsuppress feature \"" + parameters.FeatureName + "\"" + unsupConfigSuffix
        };

        FeatureNode? feature = context.FeatureTree.Features
            .FirstOrDefault(f =>
                string.Equals(f.Name, parameters.FeatureName, StringComparison.OrdinalIgnoreCase));

        if (feature != null)
        {
            if (feature.State == "active")
            {
                preview.Warnings.Add("Feature \"" + parameters.FeatureName + "\" is already active.");
            }

            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = feature.Name,
                BeforeValue = feature.State,
                AfterValue = "active"
            });
        }
        else
        {
            preview.Warnings.Add("Feature \"" + parameters.FeatureName + "\" was not found in the feature tree.");
            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = parameters.FeatureName,
                BeforeValue = "(unknown)",
                AfterValue = "active"
            });
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, UnsuppressFeatureParameters parameters)
    {
        try
        {
            dynamic swApp = application;
            dynamic modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "No active document."
                };
            }

            dynamic feature = modelDoc.FeatureByName(parameters.FeatureName);
            if (feature == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Feature \"" + parameters.FeatureName + "\" not found."
                };
            }

            // SetSuppression2: 1 = Resolved (unsuppressed)
            int configOption = string.IsNullOrWhiteSpace(parameters.ConfigurationName) ? 2 : 1;
            string[]? configNames = string.IsNullOrWhiteSpace(parameters.ConfigurationName)
                ? null
                : new[] { parameters.ConfigurationName };

            bool result = feature.SetSuppression2(
                1, // swFeatureSuppressionAction_e.swUnSuppressFeature
                configOption,
                configNames);

            if (!result)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "SOLIDWORKS refused to unsuppress feature \"" + parameters.FeatureName + "\"."
                };
            }

            modelDoc.ForceRebuild3(false);

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = "Adze: unsuppress " + parameters.FeatureName,
                AppliedValues = new Dictionary<string, string>
                {
                    [parameters.FeatureName] = "active"
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Failed to unsuppress feature: " + ex.Message
            };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        var verification = new WriteVerification();

        if (!applyResult.Success)
        {
            verification.ChangeConfirmed = false;
            verification.RebuildSucceeded = false;
            return verification;
        }

        verification.RebuildSucceeded = refreshedContext.Diagnostics.RebuildState != "failed";

        foreach (KeyValuePair<string, string> applied in applyResult.AppliedValues)
        {
            FeatureNode? feature = refreshedContext.FeatureTree.Features
                .FirstOrDefault(f =>
                    string.Equals(f.Name, applied.Key, StringComparison.OrdinalIgnoreCase));

            if (feature != null && feature.State == "active")
            {
                verification.ChangeConfirmed = true;
                verification.ObservedChanges.Add(new StateDiffItem
                {
                    Path = applied.Key,
                    BeforeValue = "suppressed",
                    AfterValue = "active"
                });
            }
            else
            {
                verification.ChangeConfirmed = false;
            }
        }

        return verification;
    }

    public string BuildUndoLabel(UnsuppressFeatureParameters parameters)
    {
        return "Adze: unsuppress " + parameters.FeatureName;
    }
}
