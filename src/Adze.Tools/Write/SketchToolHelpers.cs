using System;

namespace Adze.Tools.Write;

/// <summary>
/// Shared SW COM glue for the four sketch authoring tools. Centralizes the
/// "if no sketch is active, activate the named one" logic so each tool
/// doesn't reinvent it.
/// </summary>
internal static class SketchToolHelpers
{
    /// <summary>
    /// Ensures a sketch is active before sketch entities are added. When
    /// <paramref name="sketchName"/> is empty, the caller is expected to
    /// already be in sketch mode (e.g. immediately after <c>create_sketch</c>);
    /// no-op in that case.
    /// </summary>
    public static void EnsureSketchActive(dynamic modelDoc, string? sketchName)
    {
        if (string.IsNullOrWhiteSpace(sketchName))
        {
            // Already-active sketch path. Trust the caller; SW will throw on
            // CreateLine/CreateCircleByRadius if no sketch is active and the
            // outer Apply will return that error.
            return;
        }

        try
        {
            dynamic? activeSketch = null;
            try { activeSketch = modelDoc.SketchManager.ActiveSketch; } catch { }
            if (activeSketch != null)
            {
                string activeName = string.Empty;
                try { activeName = (string)activeSketch.GetFeature().Name; } catch { }
                if (string.Equals(activeName, sketchName, StringComparison.Ordinal))
                {
                    return;
                }
                // Different sketch is active — close it before opening the requested one.
                modelDoc.SketchManager.InsertSketch(true);
            }

            // Select the named sketch and re-enter sketch mode.
            modelDoc.ClearSelection2(true);
            try
            {
                modelDoc.Extension.SelectByID2(sketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
            }
            catch
            {
            }
            modelDoc.SketchManager.InsertSketch(true);
        }
        catch
        {
            // Best effort — let the actual sketch operation surface a clear error if state is wrong.
        }
    }
}
