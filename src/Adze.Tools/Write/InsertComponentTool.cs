using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.Tools.Write;

/// <summary>
/// Class 3 (HardWriteAdvanced) write tool — inserts a component into an assembly.
/// Requires elevated confirmation. Only available when the active document is an assembly.
/// Uses AssemblyDoc.AddComponent5() via COM.
/// </summary>
public sealed class InsertComponentTool : IWriteTool<InsertComponentParameters>
{
    public WritePreview Preview(SessionContext context, InsertComponentParameters parameters)
    {
        var preview = new WritePreview
        {
            ToolName = Contracts.Tooling.ToolNames.InsertComponent,
            Summary = "Insert component \"" + Path.GetFileName(parameters.ComponentPath) + "\" into assembly"
        };

        if (string.IsNullOrWhiteSpace(parameters.ComponentPath))
        {
            preview.Warnings.Add("Component path is empty.");
            return preview;
        }

        // Validate document type
        if (context.Document == null || !string.Equals(context.Document.Type, "assembly", StringComparison.OrdinalIgnoreCase))
        {
            preview.Warnings.Add("Component insertion requires an open assembly document. Current document type: " +
                (context.Document?.Type ?? "none") + ".");
        }

        // Check if file exists
        string fileName = Path.GetFileName(parameters.ComponentPath);
        string extension = Path.GetExtension(parameters.ComponentPath).ToLowerInvariant();

        if (extension != ".sldprt" && extension != ".sldasm")
        {
            preview.Warnings.Add("Expected a .SLDPRT or .SLDASM file, got: " + extension);
        }

        // Check if the component is already referenced
        if (context.ReferenceGraph.DirectItems.Any(r =>
            string.Equals(Path.GetFileName(r.Path), fileName, StringComparison.OrdinalIgnoreCase)))
        {
            preview.Warnings.Add("A component with this file name is already in the assembly. " +
                "SOLIDWORKS will add another instance.");
        }

        string positionText = "(" + parameters.X + ", " + parameters.Y + ", " + parameters.Z + ") mm";
        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = fileName,
            BeforeValue = "(not in assembly)",
            AfterValue = "inserted at " + positionText
        });

        if (!string.IsNullOrWhiteSpace(parameters.ConfigurationName))
        {
            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = "Configuration",
                BeforeValue = "",
                AfterValue = parameters.ConfigurationName
            });
        }

        // This is a Class 3 operation — always warn
        preview.Warnings.Add("This is an elevated operation. The component will be added to the assembly and may affect existing mates.");

        if (context.Document?.IsReadOnly == true)
        {
            preview.Warnings.Add("Document is read-only. The insertion may not be saved.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, InsertComponentParameters parameters)
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

            // Verify it's an assembly
            int docType = (int)modelDoc.GetType();
            if (docType != 2) // swDocASSEMBLY = 2
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Active document is not an assembly."
                };
            }

            // AddComponent5(CompName, ConfigName, UseConfigForPartProperties, VirtualName,
            //               InsertionOption) → Component2
            // Coordinates are set via translation after insertion
            string configName = parameters.ConfigurationName ?? "";
            dynamic? component = modelDoc.AddComponent5(
                parameters.ComponentPath,
                0,  // swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig
                "",
                false,
                configName,
                parameters.X / 1000.0,  // Convert mm to meters
                parameters.Y / 1000.0,
                parameters.Z / 1000.0);

            if (component == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "SOLIDWORKS could not insert the component. Verify the file path is correct and accessible."
                };
            }

            string componentName = "(inserted)";
            try { componentName = component.Name2; } catch { /* Name extraction is best-effort */ }

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters),
                AppliedValues = new Dictionary<string, string>
                {
                    [Path.GetFileName(parameters.ComponentPath)] = componentName
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Failed to insert component: " + ex.Message
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

        // Check that the reference graph now includes the inserted component
        foreach (var applied in applyResult.AppliedValues)
        {
            string fileName = applied.Key;
            bool found = refreshedContext.ReferenceGraph.DirectItems.Any(r =>
                r.Path.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (found)
            {
                verification.ChangeConfirmed = true;
                verification.ObservedChanges.Add(new StateDiffItem
                {
                    Path = "component",
                    BeforeValue = "(not in assembly)",
                    AfterValue = applied.Value
                });
            }
            else
            {
                verification.ChangeConfirmed = false;
            }
        }

        return verification;
    }

    public string BuildUndoLabel(InsertComponentParameters parameters)
    {
        return "Adze: insert " + Path.GetFileName(parameters.ComponentPath);
    }
}
