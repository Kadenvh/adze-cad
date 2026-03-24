using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.Tools.Write;

public sealed class RenameObjectTool : IWriteTool<RenameObjectParameters>
{
    public WritePreview Preview(SessionContext context, RenameObjectParameters parameters)
    {
        var preview = new WritePreview
        {
            ToolName = Contracts.Tooling.ToolNames.RenameObject,
            Summary = "Rename " + parameters.ObjectType + " \"" + parameters.CurrentName + "\" to \"" + parameters.NewName + "\""
        };

        if (string.IsNullOrWhiteSpace(parameters.CurrentName))
        {
            preview.Warnings.Add("Current name is empty.");
            return preview;
        }

        if (string.IsNullOrWhiteSpace(parameters.NewName))
        {
            preview.Warnings.Add("New name is empty.");
            return preview;
        }

        if (string.Equals(parameters.CurrentName, parameters.NewName, StringComparison.OrdinalIgnoreCase))
        {
            preview.Warnings.Add("The new name is the same as the current name.");
        }

        // Validate the object exists in context
        string objectType = (parameters.ObjectType ?? "feature").ToLowerInvariant();

        if (objectType == "feature")
        {
            FeatureNode? feature = context.FeatureTree.Features
                .FirstOrDefault(f => string.Equals(f.Name, parameters.CurrentName, StringComparison.OrdinalIgnoreCase));

            if (feature != null)
            {
                preview.Changes.Add(new WriteChangeItem
                {
                    TargetLabel = feature.Name + " (" + feature.Kind + ")",
                    BeforeValue = feature.Name,
                    AfterValue = parameters.NewName
                });

                // Check for dimension references
                var affectedDims = context.Dimensions.Items
                    .Where(d => d.FullName.IndexOf(parameters.CurrentName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                if (affectedDims.Count > 0)
                {
                    preview.Warnings.Add(affectedDims.Count + " dimension(s) reference this feature by name " +
                        "(e.g., " + affectedDims[0].FullName + "). SOLIDWORKS will update these references automatically.");
                }
            }
            else
            {
                preview.Warnings.Add("Feature \"" + parameters.CurrentName + "\" was not found in the feature tree.");
                preview.Changes.Add(new WriteChangeItem
                {
                    TargetLabel = parameters.CurrentName,
                    BeforeValue = parameters.CurrentName,
                    AfterValue = parameters.NewName
                });
            }

            // Check for name collision
            FeatureNode? collision = context.FeatureTree.Features
                .FirstOrDefault(f => string.Equals(f.Name, parameters.NewName, StringComparison.OrdinalIgnoreCase));
            if (collision != null)
            {
                preview.Warnings.Add("A feature named \"" + parameters.NewName + "\" already exists. SOLIDWORKS may reject or modify this name.");
            }
        }
        else if (objectType == "dimension")
        {
            // Dimensions can't really be renamed through the API in the same way
            preview.Warnings.Add("Dimension renaming is not supported. Use set_dimension_value to change values.");
        }
        else
        {
            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = parameters.CurrentName,
                BeforeValue = parameters.CurrentName,
                AfterValue = parameters.NewName
            });
        }

        if (context.Document?.IsReadOnly == true)
        {
            preview.Warnings.Add("Document is read-only. The rename may not be saved.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, RenameObjectParameters parameters)
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

            string objectType = (parameters.ObjectType ?? "feature").ToLowerInvariant();

            if (objectType == "feature")
            {
                dynamic? feature = modelDoc.FeatureByName(parameters.CurrentName);
                if (feature == null)
                {
                    return new WriteApplyResult
                    {
                        Success = false,
                        ErrorMessage = "Feature \"" + parameters.CurrentName + "\" not found."
                    };
                }

                feature.Name = parameters.NewName;

                return new WriteApplyResult
                {
                    Success = true,
                    UndoLabel = BuildUndoLabel(parameters),
                    AppliedValues = new Dictionary<string, string>
                    {
                        [parameters.CurrentName] = parameters.NewName
                    }
                };
            }

            return new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Renaming " + objectType + " objects is not yet supported."
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Failed to rename: " + ex.Message
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

        foreach (var applied in applyResult.AppliedValues)
        {
            string newName = applied.Value;
            FeatureNode? feature = refreshedContext.FeatureTree.Features
                .FirstOrDefault(f => string.Equals(f.Name, newName, StringComparison.OrdinalIgnoreCase));

            if (feature != null)
            {
                verification.ChangeConfirmed = true;
                verification.ObservedChanges.Add(new StateDiffItem
                {
                    Path = "feature.name",
                    BeforeValue = applied.Key,
                    AfterValue = newName
                });
            }
            else
            {
                verification.ChangeConfirmed = false;
            }
        }

        return verification;
    }

    public string BuildUndoLabel(RenameObjectParameters parameters)
    {
        return "Adze: rename " + parameters.CurrentName + " → " + parameters.NewName;
    }
}
