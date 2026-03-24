using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.Tools.Write;

public sealed class SetDimensionValueTool : IWriteTool<SetDimensionValueParameters>
{
    public WritePreview Preview(SessionContext context, SetDimensionValueParameters parameters)
    {
        string configSuffix = string.IsNullOrWhiteSpace(parameters.ConfigurationName)
            ? ""
            : " in configuration \"" + parameters.ConfigurationName + "\"";
        var preview = new WritePreview
        {
            ToolName = Contracts.Tooling.ToolNames.SetDimensionValue,
            Summary = "Set dimension \"" + parameters.DimensionFullName + "\" to " + parameters.NewValue + configSuffix
        };

        DimensionNode? existingDim = context.Dimensions.Items
            .FirstOrDefault(d =>
                string.Equals(d.FullName, parameters.DimensionFullName, StringComparison.OrdinalIgnoreCase));

        if (existingDim != null)
        {
            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = existingDim.FullName,
                BeforeValue = existingDim.Value.ToString("G"),
                AfterValue = parameters.NewValue.ToString("G")
            });
        }
        else
        {
            preview.Warnings.Add("Dimension \"" + parameters.DimensionFullName + "\" was not found in the current context. The operation may fail.");
            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = parameters.DimensionFullName,
                BeforeValue = "(unknown)",
                AfterValue = parameters.NewValue.ToString("G")
            });
        }

        if (context.Document?.IsReadOnly == true)
        {
            preview.Warnings.Add("Document is read-only. The dimension change may not be saved.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, SetDimensionValueParameters parameters)
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

            // Get the dimension by full name (e.g. "D1@Sketch1")
            dynamic dimension = modelDoc.Parameter(parameters.DimensionFullName);
            if (dimension == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Dimension \"" + parameters.DimensionFullName + "\" not found."
                };
            }

            double previousValue = dimension.SystemValue;

            // SetSystemValue3 sets in meters (system units)
            // For user-facing values, use the document units conversion
            int result;
            if (!string.IsNullOrWhiteSpace(parameters.ConfigurationName))
            {
                result = dimension.SetSystemValue3(
                    parameters.NewValue,
                    1, // swSetValue_InSpecifiedConfigurations
                    parameters.ConfigurationName);
            }
            else
            {
                result = dimension.SetSystemValue3(
                    parameters.NewValue,
                    2, // swSetValue_InAllConfigurations — use active config
                    null);
            }

            // Rebuild to apply the change
            modelDoc.ForceRebuild3(false);

            string undoLabel = "Adze: set " + parameters.DimensionFullName + " = " + parameters.NewValue;

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = undoLabel,
                AppliedValues = new Dictionary<string, string>
                {
                    [parameters.DimensionFullName] = parameters.NewValue.ToString("G")
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Failed to set dimension value: " + ex.Message
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

        // Check rebuild state
        verification.RebuildSucceeded = refreshedContext.Diagnostics.RebuildState != "failed";
        if (refreshedContext.Diagnostics.Warnings.Count > 0)
        {
            verification.RebuildWarnings.AddRange(refreshedContext.Diagnostics.Warnings);
        }

        // Verify the dimension value was set
        foreach (KeyValuePair<string, string> applied in applyResult.AppliedValues)
        {
            DimensionNode? dim = refreshedContext.Dimensions.Items
                .FirstOrDefault(d =>
                    string.Equals(d.FullName, applied.Key, StringComparison.OrdinalIgnoreCase));

            if (dim != null)
            {
                string expectedStr = applied.Value;
                string actualStr = dim.Value.ToString("G");

                if (actualStr == expectedStr ||
                    Math.Abs(dim.Value - double.Parse(expectedStr)) < 1e-9)
                {
                    verification.ChangeConfirmed = true;
                    verification.ObservedChanges.Add(new StateDiffItem
                    {
                        Path = applied.Key,
                        BeforeValue = string.Empty,
                        AfterValue = actualStr
                    });
                }
                else
                {
                    verification.ChangeConfirmed = false;
                    verification.UnexpectedChanges.Add(new StateDiffItem
                    {
                        Path = applied.Key,
                        BeforeValue = expectedStr,
                        AfterValue = actualStr
                    });
                }
            }
            else
            {
                verification.ChangeConfirmed = false;
            }
        }

        return verification;
    }

    public string BuildUndoLabel(SetDimensionValueParameters parameters)
    {
        return "Adze: set " + parameters.DimensionFullName + " = " + parameters.NewValue;
    }
}
