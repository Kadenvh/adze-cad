using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.Tools.Write;

public sealed class SetCustomPropertyTool : IWriteTool<SetCustomPropertyParameters>
{
    public WritePreview Preview(SessionContext context, SetCustomPropertyParameters parameters)
    {
        string scope = string.IsNullOrWhiteSpace(parameters.Scope) ? "document" : parameters.Scope;
        string configName = parameters.ConfigurationName ?? context.Document?.ActiveConfiguration ?? "Default";

        string propertyKey = scope == "configuration"
            ? "configuration_custom." + configName + "." + parameters.PropertyName
            : "document_custom." + parameters.PropertyName;

        string currentValue = string.Empty;
        if (context.Properties.TryGetValue(propertyKey, out object? val) && val != null)
        {
            currentValue = val.ToString() ?? string.Empty;
        }

        var preview = new WritePreview
        {
            ToolName = Contracts.Tooling.ToolNames.SetCustomProperty,
            Summary = "Set " + scope + " property \"" + parameters.PropertyName + "\" to \"" + parameters.PropertyValue + "\""
        };

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = parameters.PropertyName + " (" + scope + ")",
            BeforeValue = string.IsNullOrEmpty(currentValue) ? "(not set)" : currentValue,
            AfterValue = parameters.PropertyValue
        });

        if (context.Document?.IsReadOnly == true)
        {
            preview.Warnings.Add("Document is read-only. The property change may not be saved.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, SetCustomPropertyParameters parameters)
    {
        // COM execution — application is ISldWorks, cast at call site in Host
        // This method will be called on the UI thread via IWriteExecutionCoordinator
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

            string scope = string.IsNullOrWhiteSpace(parameters.Scope) ? "document" : parameters.Scope;
            dynamic customPropMgr;

            if (scope == "configuration")
            {
                string configName = parameters.ConfigurationName
                    ?? (string)modelDoc.GetActiveConfiguration().Name;
                customPropMgr = modelDoc.Extension.CustomPropertyManager[configName];
            }
            else
            {
                customPropMgr = modelDoc.Extension.CustomPropertyManager[""];
            }

            // Try to set existing property first, then add if it doesn't exist
            int setResult = customPropMgr.Set2(parameters.PropertyName, parameters.PropertyValue);
            if (setResult != 0)
            {
                // Property doesn't exist, add it
                int addResult = customPropMgr.Add3(
                    parameters.PropertyName,
                    30, // swCustomInfoText
                    parameters.PropertyValue,
                    1); // swCustomPropertyReplaceValue
            }

            string undoLabel = "Adze: set property " + parameters.PropertyName + " = " + parameters.PropertyValue;

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = undoLabel,
                AppliedValues = new Dictionary<string, string>
                {
                    [parameters.PropertyName] = parameters.PropertyValue
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Failed to set custom property: " + ex.Message
            };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        var verification = new WriteVerification
        {
            RebuildSucceeded = true // Custom properties don't require rebuild
        };

        if (!applyResult.Success)
        {
            verification.ChangeConfirmed = false;
            return verification;
        }

        foreach (KeyValuePair<string, string> applied in applyResult.AppliedValues)
        {
            // Check if the property value matches in the refreshed context
            bool found = false;
            foreach (KeyValuePair<string, object?> prop in refreshedContext.Properties)
            {
                if (prop.Key.EndsWith("." + applied.Key, StringComparison.OrdinalIgnoreCase))
                {
                    string actualValue = prop.Value?.ToString() ?? string.Empty;
                    if (actualValue == applied.Value)
                    {
                        verification.ChangeConfirmed = true;
                        verification.ObservedChanges.Add(new StateDiffItem
                        {
                            Path = applied.Key,
                            BeforeValue = string.Empty,
                            AfterValue = actualValue
                        });
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                verification.ChangeConfirmed = false;
            }
        }

        return verification;
    }

    public string BuildUndoLabel(SetCustomPropertyParameters parameters)
    {
        return "Adze: set property " + parameters.PropertyName + " = " + parameters.PropertyValue;
    }
}
