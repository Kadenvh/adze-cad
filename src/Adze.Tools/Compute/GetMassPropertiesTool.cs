using System;
using System.Collections.Generic;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Compute;

/// <summary>
/// Returns volume, surface area, mass, center of mass, and (optionally)
/// principal axes / moments of inertia for the active part or assembly.
/// Equivalent to the Evaluate tab's "Mass Properties" command — the agent
/// uses this for sanity checks ("is this part roughly the mass it should
/// be?"), comparisons, and as inputs to downstream tools (e.g. inserting
/// a component near the COM).
///
/// Implementation calls <c>IModelDocExtension.CreateMassProperty2()</c> via
/// dynamic on the COM <c>ISldWorks</c> reference. Configuration scoping
/// honored via <c>SetIncludeConfigurationProperties</c>.
/// </summary>
public sealed class GetMassPropertiesTool : IComputeTool<GetMassPropertiesParameters>
{
    public string ToolName => ToolNames.GetMassProperties;

    public ToolResult Execute(SessionContext context, object? application, GetMassPropertiesParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = false,
            Summary = "Mass properties not computed."
        };

        if (application == null)
        {
            result.Summary = "SOLIDWORKS is not connected — mass properties require a live application reference.";
            return result;
        }

        if (context?.Document == null || context.Document.Type == "none")
        {
            result.Summary = "No active document.";
            return result;
        }

        try
        {
            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
            {
                result.Summary = "No active document.";
                return result;
            }

            // CreateMassProperty2 is preferred over CreateMassProperty because
            // it accepts a config-scoped option struct. Returns IMassProperty2
            // which exposes Mass, Volume, SurfaceArea, CenterOfMass[], and
            // PrincipalAxesOfInertia[] / PrincipalMomentsOfInertia[].
            dynamic? massPropMgr = modelDoc.Extension.CreateMassProperty2();
            if (massPropMgr == null)
            {
                result.Summary = "CreateMassProperty2 returned null.";
                return result;
            }

            string? configName = parameters?.ConfigurationName;
            if (!string.IsNullOrWhiteSpace(configName))
            {
                try { massPropMgr.SetIncludeConfigurationProperties(configName); }
                catch
                {
                    // Older SW or non-part doc — ignore and use active config.
                }
            }

            double mass = 0;
            double volume = 0;
            double surfaceArea = 0;
            double[]? center = null;

            try { mass = (double)massPropMgr.Mass; } catch { /* keep zero */ }
            try { volume = (double)massPropMgr.Volume; } catch { }
            try { surfaceArea = (double)massPropMgr.SurfaceArea; } catch { }
            try
            {
                object com = massPropMgr.CenterOfMass;
                center = com as double[];
            }
            catch { }

            result.Success = true;
            result.Summary = "Mass: "
                + mass.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + " kg, Volume: "
                + (volume * 1e9).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm³, Area: "
                + (surfaceArea * 1e6).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm².";

            result.Data["mass_kg"] = mass;
            result.Data["volume_m3"] = volume;
            result.Data["volume_mm3"] = volume * 1e9;
            result.Data["surface_area_m2"] = surfaceArea;
            result.Data["surface_area_mm2"] = surfaceArea * 1e6;

            if (center != null && center.Length >= 3)
            {
                result.Data["center_of_mass_m"] = new Dictionary<string, object?>
                {
                    ["x"] = center[0],
                    ["y"] = center[1],
                    ["z"] = center[2]
                };
                result.Data["center_of_mass_mm"] = new Dictionary<string, object?>
                {
                    ["x"] = center[0] * 1000.0,
                    ["y"] = center[1] * 1000.0,
                    ["z"] = center[2] * 1000.0
                };
            }

            if (parameters?.IncludePrincipalAxes == true)
            {
                TryAddPrincipalAxes(massPropMgr, result);
            }

            if (!string.IsNullOrWhiteSpace(configName))
            {
                result.Data["configuration"] = configName;
            }
        }
        catch (Exception ex)
        {
            result.Summary = "Mass properties failed: " + ex.Message;
        }

        return result;
    }

    private static void TryAddPrincipalAxes(dynamic massPropMgr, ToolResult result)
    {
        try
        {
            object axes = massPropMgr.PrincipalAxesOfInertia;
            if (axes is double[] axesArr && axesArr.Length >= 9)
            {
                result.Data["principal_axes"] = new Dictionary<string, object?>
                {
                    ["axis1"] = new[] { axesArr[0], axesArr[1], axesArr[2] },
                    ["axis2"] = new[] { axesArr[3], axesArr[4], axesArr[5] },
                    ["axis3"] = new[] { axesArr[6], axesArr[7], axesArr[8] }
                };
            }
        }
        catch
        {
            // Inertia data unavailable for assemblies in some SW builds; not fatal.
        }

        try
        {
            object moments = massPropMgr.PrincipalMomentsOfInertia;
            if (moments is double[] momentsArr && momentsArr.Length >= 3)
            {
                result.Data["principal_moments_kg_m2"] = new[] { momentsArr[0], momentsArr[1], momentsArr[2] };
            }
        }
        catch
        {
        }
    }
}
