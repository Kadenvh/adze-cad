using System;
using System.Collections.Generic;
using System.IO;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Creates a new SOLIDWORKS drawing document, optionally populated with
/// standard 3rd-angle views (front + top + right + isometric) of a part
/// or assembly. The "STL → drawing" flow lands here: import_mesh produces
/// a part with a graphics body, then create_drawing_document spins up a
/// drawing of that part for downstream documentation.
///
/// Class 3 (HardWriteAdvanced) — opens a new document, elevated
/// confirmation required.
/// </summary>
public sealed class CreateDrawingDocumentTool : IWriteTool<CreateDrawingDocumentParameters>
{
    public WritePreview Preview(SessionContext context, CreateDrawingDocumentParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.CreateDrawingDocument };

        string source = ResolveModelPath(context, parameters);
        if (string.IsNullOrWhiteSpace(source))
        {
            preview.Summary = "Create drawing: no model_path supplied and no active document.";
            preview.Warnings.Add("Either provide model_path or open a part/assembly first.");
            return preview;
        }

        bool isFile = File.Exists(source);
        bool isAssembly = source.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);
        bool isPart = source.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase);
        bool isUntitled = !isFile && string.IsNullOrEmpty(Path.GetExtension(source));
        bool includeViews = parameters?.IncludeStandardViews ?? true;

        preview.Summary = "Create drawing of "
            + (isUntitled ? "active document" : Path.GetFileName(source))
            + (includeViews ? " with standard views" : " (blank)");

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "New drawing document",
            BeforeValue = "(none)",
            AfterValue = isFile ? Path.GetFileNameWithoutExtension(source) + ".SLDDRW" : "(unsaved drawing)"
        });

        if (includeViews)
        {
            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = "Standard views",
                BeforeValue = "(none)",
                AfterValue = "Front + Top + Right + Isometric"
            });
        }

        if (!isFile && !isUntitled && !isAssembly && !isPart)
        {
            preview.Warnings.Add(
                "Model path is not a recognized SOLIDWORKS file (.sldprt / .sldasm).");
        }

        if (isFile && !isPart && !isAssembly)
        {
            preview.Warnings.Add("File extension is not .sldprt or .sldasm — drawing creation may fail.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, CreateDrawingDocumentParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };

            dynamic swApp = application;

            // Resolve target model path from parameters or active doc.
            string modelPath = parameters?.ModelPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                try
                {
                    dynamic? activeDoc = swApp.ActiveDoc;
                    if (activeDoc != null)
                    {
                        modelPath = (string)activeDoc.GetPathName();
                        if (string.IsNullOrWhiteSpace(modelPath))
                        {
                            return new WriteApplyResult
                            {
                                Success = false,
                                ErrorMessage = "Active document has not been saved — save the part / assembly before creating a drawing of it."
                            };
                        }
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return new WriteApplyResult { Success = false, ErrorMessage = "No model path resolved." };
            }

            // Spin up a new drawing document. NewDrawing2 takes a default size
            // index (0 = use current default) and an optional template path.
            string templatePath = parameters?.TemplatePath ?? string.Empty;
            dynamic? drawingDoc = string.IsNullOrWhiteSpace(templatePath)
                ? swApp.NewDrawing2(0, string.Empty, 12, 0.27940, 0.21590)
                : swApp.NewDocument(templatePath, 12, 0.27940, 0.21590);

            if (drawingDoc == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "NewDrawing2 returned null. Verify SW has a configured drawing template."
                };
            }

            string drawingTitle = "(unsaved drawing)";
            try { drawingTitle = (string)drawingDoc.GetTitle(); } catch { }

            bool viewsAdded = false;
            int viewCount = 0;
            if (parameters?.IncludeStandardViews ?? true)
            {
                try
                {
                    bool ok = (bool)drawingDoc.Create3rdAngleViews2(modelPath);
                    viewsAdded = ok;
                    if (ok)
                    {
                        try
                        {
                            // Count views via the active sheet's view collection.
                            dynamic? sheet = drawingDoc.GetCurrentSheet();
                            if (sheet != null)
                            {
                                object views = sheet.GetViews();
                                if (views is object[] viewArr)
                                {
                                    viewCount = viewArr.Length;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception viewEx)
                {
                    return new WriteApplyResult
                    {
                        Success = true,
                        UndoLabel = BuildUndoLabel(parameters!),
                        ErrorMessage = "Drawing created but Create3rdAngleViews2 threw: " + viewEx.Message,
                        AppliedValues = new Dictionary<string, string>
                        {
                            ["drawing_title"] = drawingTitle,
                            ["views_added"] = "0"
                        }
                    };
                }
            }

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters!),
                AppliedValues = new Dictionary<string, string>
                {
                    ["drawing_title"] = drawingTitle,
                    ["model_path"] = modelPath,
                    ["views_added"] = (viewsAdded ? viewCount : 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Create drawing failed: " + ex.Message };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        var verification = new WriteVerification { RebuildSucceeded = true };
        if (!applyResult.Success)
        {
            verification.ChangeConfirmed = false;
            return verification;
        }

        DocumentInfo? doc = refreshedContext?.Document;
        if (doc != null && string.Equals(doc.Type, "Drawing", StringComparison.OrdinalIgnoreCase))
        {
            verification.ChangeConfirmed = true;
            verification.ObservedChanges.Add(new StateDiffItem
            {
                Path = "active_document",
                BeforeValue = string.Empty,
                AfterValue = doc.Title ?? string.Empty
            });
        }
        else
        {
            verification.ChangeConfirmed = false;
        }

        return verification;
    }

    public string BuildUndoLabel(CreateDrawingDocumentParameters parameters)
    {
        string source = string.IsNullOrWhiteSpace(parameters?.ModelPath)
            ? "active document"
            : Path.GetFileName(parameters!.ModelPath!);
        return "Adze: create drawing of " + source;
    }

    private static string ResolveModelPath(SessionContext context, CreateDrawingDocumentParameters? parameters)
    {
        if (!string.IsNullOrWhiteSpace(parameters?.ModelPath))
        {
            return parameters!.ModelPath!;
        }
        return context?.Document?.Path ?? string.Empty;
    }
}
