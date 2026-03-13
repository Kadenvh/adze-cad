using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Host.Services;

internal static class SessionContextBuilder
{
    public static SessionContext Build(ISldWorks? application)
    {
        var context = new SessionContext
        {
            Session = new SessionInfo
            {
                RequestId = "host-status",
                TimestampUtc = DateTimeOffset.UtcNow,
                ApprovalState = ApprovalState.Completed,
                UserMode = "interactive"
            },
            Environment = new EnvironmentInfo
            {
                SolidWorksVersion = application?.RevisionNumber() ?? "R2026x",
                AddInVersion = typeof(SessionContextBuilder).Assembly.GetName().Version?.ToString() ?? "0.1.0",
                MachineName = System.Environment.MachineName,
                DocumentManagerAvailable = false,
                DiagnosticsMode = true
            },
            Document = null,
            Selection = new SelectionInfo(),
            FeatureTree = new FeatureTreeInfo
            {
                Anchor = null,
                Radius = 0,
                Features = new List<FeatureNode>()
            },
            Configurations = new ConfigurationsInfo(),
            Dimensions = new DimensionsInfo(),
            Mates = new MatesInfo(),
            ReferenceGraph = new ReferenceGraphInfo(),
            Properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            Diagnostics = new DiagnosticsInfo
            {
                RebuildState = "unknown"
            },
            Policy = new PolicyInfo
            {
                EnabledTools = new List<string>
                {
                    ToolNames.GetActiveDocument,
                    ToolNames.GetDocumentSummary,
                    ToolNames.GetSelectionContext,
                    ToolNames.GetFeatureTreeSlice,
                    ToolNames.GetDimensions,
                    ToolNames.GetConfigurations,
                    ToolNames.GetCustomProperties,
                    ToolNames.GetMates,
                    ToolNames.GetRebuildDiagnostics,
                    ToolNames.GetReferenceGraph
                },
                ToolUnlockTier = ToolUnlockTier.Baseline,
                ExplorationPercent = 0
            }
        };

        if (application == null)
        {
            return context;
        }

        ModelDoc2? model = application.IActiveDoc2;
        if (model == null)
        {
            return context;
        }

        context.Document = new DocumentInfo
        {
            Type = MapDocumentType(model.GetType()),
            Title = model.GetTitle() ?? string.Empty,
            Path = model.GetPathName() ?? string.Empty,
            ActiveConfiguration = GetActiveConfigurationName(model),
            Units = GetUnitsSummary(model),
            IsDirty = model.GetSaveFlag(),
            IsReadOnly = IsReadOnly(model.GetPathName())
        };

        SelectionMgr? selectionManager = model.ISelectionManager;
        int selectionCount = selectionManager?.GetSelectedObjectCount2(-1) ?? 0;
        context.Selection = new SelectionInfo
        {
            Count = selectionCount
        };

        if (selectionManager != null)
        {
            int previewCount = Math.Min(selectionCount, 3);
            for (int index = 1; index <= previewCount; index++)
            {
                object? selected = selectionManager.GetSelectedObject6(index, -1);
                int selectedType = selectionManager.GetSelectedObjectType3(index, -1);
                context.Selection.Items.Add(new SelectionItem
                {
                    Kind = "type_" + selectedType,
                    Name = selected?.ToString() ?? "<unknown>",
                    Owner = string.Empty
                });
            }
        }

        context.Configurations = BuildConfigurations(model, context.Document.ActiveConfiguration);
        context.FeatureTree = BuildFeatureTree(model, context.Selection);
        context.Dimensions = BuildDimensions(model);
        context.Mates = BuildMates(model);
        context.ReferenceGraph = BuildReferenceGraph(model);
        context.Diagnostics = BuildDiagnostics(model, context.Document, context.ReferenceGraph);

        context.Properties["document_title"] = context.Document.Title;
        context.Properties["document_path"] = context.Document.Path;
        context.Properties["document_type"] = context.Document.Type;
        context.Properties["active_configuration"] = context.Document.ActiveConfiguration;
        context.Properties["units"] = context.Document.Units;
        context.Properties["selection_count"] = context.Selection.Count;
        context.Properties["feature_preview_count"] = context.FeatureTree.Features.Count;
        context.Properties["configuration_count"] = context.Configurations.Count;
        context.Properties["dimension_count"] = context.Dimensions.Count;
        context.Properties["mate_count"] = context.Mates.Count;
        context.Properties["reference_direct_count"] = context.ReferenceGraph.DirectCount;
        context.Properties["reference_transitive_count"] = context.ReferenceGraph.TransitiveCount;
        context.Properties["reference_broken_count"] = context.ReferenceGraph.BrokenReferenceCount;
        context.Properties["rebuild_state"] = context.Diagnostics.RebuildState;

        PopulateFileProperties(context.Properties, context.Document.Path);
        PopulateCustomProperties(model, context.Document.ActiveConfiguration, context.Properties);

        return context;
    }

    private static ConfigurationsInfo BuildConfigurations(ModelDoc2 model, string activeConfigurationName)
    {
        string[] names = GetConfigurationNames(model);
        var items = new List<ConfigurationItem>();
        foreach (string name in names)
        {
            items.Add(new ConfigurationItem
            {
                Name = name,
                IsActive = string.Equals(name, activeConfigurationName, StringComparison.OrdinalIgnoreCase)
            });
        }

        return new ConfigurationsInfo
        {
            ActiveName = activeConfigurationName,
            Count = items.Count,
            Items = items
        };
    }

    private static FeatureTreeInfo BuildFeatureTree(ModelDoc2 model, SelectionInfo selection)
    {
        const int previewLimit = 12;
        var featureTree = new FeatureTreeInfo
        {
            Radius = previewLimit
        };

        Feature? feature = model.IFirstFeature();
        while (feature != null && featureTree.Features.Count < previewLimit)
        {
            featureTree.Features.Add(new FeatureNode
            {
                Name = feature.Name ?? "<unnamed>",
                Kind = feature.GetTypeName2() ?? feature.GetTypeName() ?? "<unknown>",
                State = GetFeatureState(feature)
            });

            feature = feature.IGetNextFeature();
        }

        if (selection.Items.Count > 0)
        {
            featureTree.Anchor = selection.Items[0].Name;
        }
        else if (featureTree.Features.Count > 0)
        {
            featureTree.Anchor = featureTree.Features[0].Name;
        }

        return featureTree;
    }

    private static ReferenceGraphInfo BuildReferenceGraph(ModelDoc2 model)
    {
        var referenceGraph = new ReferenceGraphInfo();

        try
        {
            ModelDocExtension? extension = model.Extension;
            if (extension == null)
            {
                return referenceGraph;
            }

            referenceGraph.DirectItems = ParseDependencySet(extension.GetDependencies(false, true, true, true, true));
            referenceGraph.TransitiveItems = ParseDependencySet(extension.GetDependencies(true, true, true, true, true));
            referenceGraph.DirectCount = referenceGraph.DirectItems.Count;
            referenceGraph.TransitiveCount = referenceGraph.TransitiveItems.Count;
            referenceGraph.BrokenReferenceCount = referenceGraph.TransitiveItems.Count(item => item.IsBroken);
        }
        catch
        {
        }

        return referenceGraph;
    }

    private static MatesInfo BuildMates(ModelDoc2 model)
    {
        const int maxMates = 150;
        var mates = new MatesInfo();
        if (model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            return mates;
        }

        Feature? feature = model.IFirstFeature();
        while (feature != null && mates.Items.Count < maxMates)
        {
            if (string.Equals(feature.GetTypeName2(), "MateGroup", StringComparison.OrdinalIgnoreCase))
            {
                Feature? subFeature = feature.IGetFirstSubFeature();
                while (subFeature != null && mates.Items.Count < maxMates)
                {
                    MateNode? mate = TryBuildMateNode(subFeature);
                    if (mate != null)
                    {
                        mates.Items.Add(mate);
                    }

                    subFeature = subFeature.IGetNextSubFeature();
                }
            }

            feature = feature.IGetNextFeature();
        }

        mates.Count = mates.Items.Count;
        return mates;
    }

    private static DimensionsInfo BuildDimensions(ModelDoc2 model)
    {
        const int maxDimensions = 150;
        var dimensions = new DimensionsInfo();

        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool restoreFeatureDimensions = false;
            try
            {
                if (!model.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayFeatureDimensions))
                {
                    model.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayFeatureDimensions, true);
                    restoreFeatureDimensions = true;
                }

                Feature? feature = model.IFirstFeature();
                while (feature != null && dimensions.Items.Count < maxDimensions)
                {
                    object currentDisplay = feature.GetFirstDisplayDimension();
                    while (currentDisplay != null && dimensions.Items.Count < maxDimensions)
                    {
                        if (currentDisplay is DisplayDimension displayDimension)
                        {
                            TryAddDimensionNode(dimensions, seen, displayDimension, model);
                        }

                        currentDisplay = feature.GetNextDisplayDimension(currentDisplay);
                    }

                    feature = feature.IGetNextFeature();
                }
            }
            finally
            {
                if (restoreFeatureDimensions)
                {
                    model.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayFeatureDimensions, false);
                }
            }
        }
        catch
        {
        }

        dimensions.Count = dimensions.Items.Count;
        return dimensions;
    }

    private static void TryAddDimensionNode(
        DimensionsInfo dimensions,
        ISet<string> seen,
        DisplayDimension displayDimension,
        ModelDoc2 model)
    {
        try
        {
            Dimension? dimension = displayDimension.IGetDimension();
            string fullName = dimension?.FullName ?? string.Empty;
            string selectionName = displayDimension.GetNameForSelection() ?? string.Empty;
            string name = !string.IsNullOrWhiteSpace(selectionName)
                ? selectionName
                : fullName;

            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            {
                return;
            }

            double value = 0;
            try
            {
                value = dimension?.GetUserValueIn(model) ?? 0;
            }
            catch
            {
            }

            dimensions.Items.Add(new DimensionNode
            {
                Name = name,
                FullName = fullName,
                Value = value,
                UnitSource = "document"
            });
        }
        catch
        {
        }
    }

    private static DiagnosticsInfo BuildDiagnostics(ModelDoc2 model, DocumentInfo document, ReferenceGraphInfo referenceGraph)
    {
        var diagnostics = new DiagnosticsInfo
        {
            RebuildState = GetRebuildState(model)
        };

        if (document.IsDirty)
        {
            diagnostics.Warnings.Add("Document has unsaved changes.");
        }

        if (!string.IsNullOrWhiteSpace(document.Path) && !File.Exists(document.Path))
        {
            diagnostics.Warnings.Add("Document path does not exist on disk.");
        }

        foreach (ReferenceNode item in referenceGraph.TransitiveItems.Where(candidate => candidate.IsBroken))
        {
            diagnostics.MissingReferences.Add(string.IsNullOrWhiteSpace(item.Path) ? item.Name : item.Path);
        }

        if (referenceGraph.BrokenReferenceCount > 0)
        {
            diagnostics.Warnings.Add("Reference graph contains unresolved dependencies.");
        }

        return diagnostics;
    }

    private static List<ReferenceNode> ParseDependencySet(object dependencyValues)
    {
        object[] rawValues = ToObjectArray(dependencyValues);
        if (rawValues.Length < 2)
        {
            return new List<ReferenceNode>();
        }

        int stride = rawValues.Length % 3 == 0 ? 3 : 2;
        var items = new List<ReferenceNode>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index + 1 < rawValues.Length; index += stride)
        {
            string name = rawValues[index]?.ToString() ?? string.Empty;
            string rawPath = rawValues[index + 1]?.ToString() ?? string.Empty;
            bool isReadOnly = stride == 3 && ToBoolean(rawValues[index + 2]);
            SplitImportedPath(rawPath, out string path, out string? importedPath);
            bool existsOnDisk = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            bool isBroken = string.IsNullOrWhiteSpace(path) || (!existsOnDisk && LooksLikeFileReference(path));
            string dedupeKey = name + "|" + path + "|" + importedPath + "|" + isReadOnly;

            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            items.Add(new ReferenceNode
            {
                Name = name,
                Path = path,
                ImportedPath = importedPath,
                IsReadOnly = isReadOnly,
                ExistsOnDisk = existsOnDisk,
                IsBroken = isBroken
            });
        }

        return items;
    }

    private static object[] ToObjectArray(object values)
    {
        if (values is object[] objectValues)
        {
            return objectValues;
        }

        if (values is string[] stringValues)
        {
            return stringValues.Cast<object>().ToArray();
        }

        return Array.Empty<object>();
    }

    private static bool ToBoolean(object? value)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }

        return bool.TryParse(value?.ToString(), out bool parsed) && parsed;
    }

    private static void SplitImportedPath(string rawPath, out string path, out string? importedPath)
    {
        string[] parts = (rawPath ?? string.Empty).Split(new[] { '|' }, 2, StringSplitOptions.None);
        path = parts[0];
        importedPath = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
            ? parts[1]
            : null;
    }

    private static bool LooksLikeFileReference(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return path.IndexOf('\\') >= 0 ||
               path.IndexOf(':') >= 0 ||
               path.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".slddrw", StringComparison.OrdinalIgnoreCase);
    }

    private static MateNode? TryBuildMateNode(Feature feature)
    {
        try
        {
            object specificFeature = feature.GetSpecificFeature2();
            if (specificFeature is not Mate2 mate)
            {
                return null;
            }

            var components = new List<string>();
            int entityCount = mate.GetMateEntityCount();
            for (int index = 0; index < entityCount; index++)
            {
                try
                {
                    MateEntity2? entity = mate.MateEntity(index);
                    Component2? component = entity?.ReferenceComponent;
                    string componentName = component?.Name2 ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(componentName) &&
                        !components.Contains(componentName, StringComparer.OrdinalIgnoreCase))
                    {
                        components.Add(componentName);
                    }
                }
                catch
                {
                }
            }

            return new MateNode
            {
                Name = feature.Name ?? "<unnamed mate>",
                Kind = feature.GetTypeName2() ?? feature.GetTypeName() ?? "Mate",
                EntityCount = entityCount,
                Components = components
            };
        }
        catch
        {
            return null;
        }
    }

    private static void PopulateFileProperties(IDictionary<string, object?> properties, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var fileInfo = new FileInfo(path);
        properties["file_name"] = fileInfo.Name;
        properties["file_directory"] = fileInfo.DirectoryName ?? string.Empty;
        properties["file_extension"] = fileInfo.Extension;
        properties["file_size_bytes"] = fileInfo.Length;
        properties["file_last_write_utc"] = fileInfo.LastWriteTimeUtc.ToString("u");
        properties["file_is_read_only"] = fileInfo.IsReadOnly;
    }

    private static void PopulateCustomProperties(ModelDoc2 model, string activeConfigurationName, IDictionary<string, object?> properties)
    {
        try
        {
            ModelDocExtension? extension = model.Extension;
            if (extension == null)
            {
                return;
            }

            AddCustomProperties(extension.get_CustomPropertyManager(string.Empty), "document_custom.", properties);
            if (!string.IsNullOrWhiteSpace(activeConfigurationName))
            {
                AddCustomProperties(
                    extension.get_CustomPropertyManager(activeConfigurationName),
                    "configuration_custom." + activeConfigurationName + ".",
                    properties);
            }
        }
        catch
        {
        }
    }

    private static void AddCustomProperties(CustomPropertyManager? manager, string prefix, IDictionary<string, object?> properties)
    {
        if (manager == null)
        {
            return;
        }

        foreach (string propertyName in GetPropertyNames(manager))
        {
            try
            {
                manager.Get6(propertyName, true, out string rawValue, out string resolvedValue, out bool wasResolved, out bool linkToProperty);
                string value = !string.IsNullOrWhiteSpace(resolvedValue)
                    ? resolvedValue
                    : rawValue;
                properties[prefix + propertyName] = value;

                if (wasResolved)
                {
                    properties[prefix + propertyName + ".__resolved"] = true;
                }

                if (linkToProperty)
                {
                    properties[prefix + propertyName + ".__linked"] = true;
                }
            }
            catch
            {
            }
        }
    }

    private static IEnumerable<string> GetPropertyNames(CustomPropertyManager manager)
    {
        try
        {
            object names = manager.GetNames();
            if (names is string[] stringValues)
            {
                return stringValues.Where(value => !string.IsNullOrWhiteSpace(value));
            }

            if (names is object[] objectValues)
            {
                return objectValues
                    .Select(value => value?.ToString() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value));
            }
        }
        catch
        {
        }

        return Array.Empty<string>();
    }

    private static string GetActiveConfigurationName(ModelDoc2 model)
    {
        try
        {
            ConfigurationManager? configurationManager = model.ConfigurationManager;
            if (configurationManager?.ActiveConfiguration != null)
            {
                return configurationManager.ActiveConfiguration.Name ?? string.Empty;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string[] GetConfigurationNames(ModelDoc2 model)
    {
        try
        {
            object values = model.GetConfigurationNames();
            if (values is string[] stringValues)
            {
                return stringValues;
            }

            if (values is object[] objectValues)
            {
                return objectValues
                    .Select(value => value?.ToString() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }
        }
        catch
        {
        }

        return Array.Empty<string>();
    }

    private static string GetUnitsSummary(ModelDoc2 model)
    {
        try
        {
            object units = model.GetUnits();
            if (units is short[] shortValues)
            {
                return "[" + string.Join(", ", shortValues.Select(value => value.ToString())) + "]";
            }

            if (units is int[] intValues)
            {
                return "[" + string.Join(", ", intValues.Select(value => value.ToString())) + "]";
            }

            if (units is object[] objectValues)
            {
                return "[" + string.Join(", ", objectValues.Select(value => value?.ToString() ?? "<null>")) + "]";
            }

            return units?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetFeatureState(Feature feature)
    {
        try
        {
            object state = feature.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null);
            if (state is bool boolState)
            {
                return boolState ? "suppressed" : "resolved";
            }

            if (state is object[] stateValues && stateValues.Length > 0 && stateValues[0] is bool firstValue)
            {
                return firstValue ? "suppressed" : "resolved";
            }
        }
        catch
        {
        }

        try
        {
            return feature.IsSuppressed() ? "suppressed" : "resolved";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetRebuildState(ModelDoc2 model)
    {
        try
        {
            ModelDocExtension? extension = model.Extension;
            if (extension != null)
            {
                return extension.NeedsRebuild2 != 0 ? "needs_rebuild" : "current";
            }
        }
        catch
        {
        }

        return "connected";
    }

    private static bool IsReadOnly(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               File.Exists(path) &&
               new FileInfo(path).IsReadOnly;
    }

    private static string MapDocumentType(int value)
    {
        return value switch
        {
            1 => "part",
            2 => "assembly",
            3 => "drawing",
            _ => "unknown"
        };
    }
}
