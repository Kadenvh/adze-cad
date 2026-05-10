using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Mesh.Analysis;
using Adze.Mesh.Io;
using Adze.Mesh.Models;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Tools.Write;

/// <summary>
/// Imports an STL file into SOLIDWORKS as a part document. First-pass strategy
/// is <c>graphics_body</c> — opens the file via the standard Open dialog path
/// and lets SW's built-in mesh importer create the part with a graphics body.
/// Higher-fidelity strategies (mesh BREP, ScanTo3D surface fit, Adze
/// reverse-engineering) are reserved for follow-on work and currently fall
/// back to <c>graphics_body</c>.
///
/// Class: HardWriteAdvanced (Class 3) — opens a new document, elevated
/// confirmation required. Apply lives entirely on the SW UI thread (the host
/// COM dispatcher marshals before invoking).
/// </summary>
public sealed class ImportMeshTool : IWriteTool<ImportMeshParameters>
{
    public WritePreview Preview(SessionContext context, ImportMeshParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.ImportMesh };

        if (parameters == null || string.IsNullOrWhiteSpace(parameters.FilePath))
        {
            preview.Summary = "Import mesh: file_path is required.";
            preview.Warnings.Add("file_path is required.");
            return preview;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(parameters.FilePath);
        }
        catch (Exception ex)
        {
            preview.Summary = "Import mesh: invalid file path.";
            preview.Warnings.Add("Invalid path: " + ex.Message);
            return preview;
        }

        if (!File.Exists(fullPath))
        {
            preview.Summary = "Import mesh: file not found.";
            preview.Warnings.Add("File not found: " + fullPath);
            return preview;
        }

        string ext = Path.GetExtension(fullPath);
        if (!string.Equals(ext, ".stl", StringComparison.OrdinalIgnoreCase))
        {
            preview.Summary = "Import mesh: unsupported format (" + ext + "). Only .stl is supported in this version.";
            preview.Warnings.Add("Unsupported extension: " + ext);
            return preview;
        }

        // Up-front analysis lets the preview card show the user (and the agent)
        // exactly what they'll get before approving.
        try
        {
            AdzeMesh mesh = StlReader.ReadFromFile(fullPath);
            MeshAnalysis analysis = MeshAnalyzer.Analyze(mesh);

            string strategy = ResolveStrategy(parameters.Strategy, analysis.RecommendedStrategy);

            preview.Summary = "Import "
                + Path.GetFileName(fullPath) + " ("
                + analysis.Statistics.TriangleCount.ToString("N0", CultureInfo.InvariantCulture) + " triangles, "
                + (analysis.Statistics.IsClosed ? "closed" : "open") + ") via " + strategy;

            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = "Active document",
                BeforeValue = context.Document?.Title ?? "(no active document)",
                AfterValue = "New part — " + Path.GetFileNameWithoutExtension(fullPath)
            });

            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = "Body type",
                BeforeValue = "(none)",
                AfterValue = strategy == "graphics_body" ? "Graphics body" : "Pending: " + strategy
            });

            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = "Bounding box",
                BeforeValue = "(none)",
                AfterValue = FormatBoundingBox(analysis.Statistics.BoundingBox)
            });

            foreach (string advisory in analysis.Advisories)
            {
                preview.Warnings.Add(advisory);
            }

            // Strategy advisories — be honest about what we don't yet do.
            if (strategy != "graphics_body")
            {
                preview.Warnings.Add(
                    "Strategy '" + strategy
                    + "' is reserved for future capability; this import will produce a graphics body.");
            }

            if (analysis.Convertibility == MeshSolidConvertibility.NotConvertible)
            {
                preview.Warnings.Add("Mesh has serious topology issues — direct solid conversion will not be feasible after import.");
            }
        }
        catch (Exception ex)
        {
            preview.Summary = "Import mesh: failed to read STL.";
            preview.Warnings.Add("STL read failed: " + ex.Message);
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, ImportMeshParameters parameters)
    {
        try
        {
            if (parameters == null || string.IsNullOrWhiteSpace(parameters.FilePath))
            {
                return new WriteApplyResult { Success = false, ErrorMessage = "file_path is required." };
            }

            string fullPath = Path.GetFullPath(parameters.FilePath);
            if (!File.Exists(fullPath))
            {
                return new WriteApplyResult { Success = false, ErrorMessage = "File not found: " + fullPath };
            }

            // SW interop — open the STL via OpenDoc6. SW recognizes .stl and
            // 3D Interconnect produces a part document containing a graphics
            // body for the mesh. Constants are taken from SOLIDWORKS interop:
            //   swDocumentTypes_e.swDocPART = 1
            //   swOpenDocOptions_e.swOpenDocOptions_Silent = 1
            //   error/warning out-params are populated by SW.
            dynamic swApp = application
                ?? throw new InvalidOperationException("ISldWorks application is required.");

            const int swDocPart = 1;
            const int swOpenDocOptions_Silent = 1;
            int errors = 0;
            int warnings = 0;

            dynamic? doc = swApp.OpenDoc6(
                fullPath,
                swDocPart,
                swOpenDocOptions_Silent,
                string.Empty,
                ref errors,
                ref warnings);

            if (doc == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "OpenDoc6 returned null (errors=" + errors + ", warnings=" + warnings + ")."
                };
            }

            string undoLabel = BuildUndoLabel(parameters);
            string title;
            try { title = (string)doc.GetTitle(); }
            catch { title = Path.GetFileNameWithoutExtension(fullPath); }

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = undoLabel,
                AppliedValues = new Dictionary<string, string>
                {
                    ["imported_path"] = fullPath,
                    ["new_document_title"] = title,
                    ["sw_open_errors"] = errors.ToString(CultureInfo.InvariantCulture),
                    ["sw_open_warnings"] = warnings.ToString(CultureInfo.InvariantCulture)
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Mesh import failed: " + ex.Message
            };
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

        // Verification is best-effort: the refreshed context should now point at
        // the newly opened part. We confirm there IS an active document of part
        // type — the body-type ground-truth requires a deeper inspection that
        // the current SessionContext doesn't expose.
        DocumentInfo? doc = refreshedContext?.Document;
        if (doc == null)
        {
            verification.ChangeConfirmed = false;
            return verification;
        }

        if (!string.Equals(doc.Type, "Part", StringComparison.OrdinalIgnoreCase))
        {
            verification.ChangeConfirmed = false;
            verification.RebuildWarnings.Add("Active document after import is not a part (type=" + doc.Type + ").");
            return verification;
        }

        verification.ChangeConfirmed = true;
        verification.ObservedChanges.Add(new StateDiffItem
        {
            Path = "active_document",
            BeforeValue = string.Empty,
            AfterValue = doc.Title ?? string.Empty
        });

        return verification;
    }

    public string BuildUndoLabel(ImportMeshParameters parameters)
    {
        string fileName = string.IsNullOrWhiteSpace(parameters?.FilePath)
            ? "(unknown)"
            : Path.GetFileName(parameters.FilePath);
        return "Adze: import mesh " + fileName;
    }

    private static string ResolveStrategy(string requested, ImportStrategy recommended)
    {
        if (string.IsNullOrWhiteSpace(requested)) return "graphics_body";
        string normalized = requested.Trim().ToLowerInvariant();
        return normalized switch
        {
            "graphics_body" or "graphics" => "graphics_body",
            "convert_to_solid" or "graphics_then_convert" => "graphics_then_convert",
            "scan_to_3d" or "scanto3d" => "scan_to_3d",
            "reverse_engineer" or "adze_reverse" => "reverse_engineer",
            _ => "graphics_body"
        };
    }

    private static string FormatBoundingBox(AxisAlignedBoundingBox bbox)
    {
        if (bbox.IsEmpty) return "(empty)";
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.###} × {1:0.###} × {2:0.###}",
            bbox.Size.X, bbox.Size.Y, bbox.Size.Z);
    }
}
