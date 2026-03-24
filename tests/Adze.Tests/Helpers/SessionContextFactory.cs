using System;
using System.Collections.Generic;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tests.Helpers;

internal static class SessionContextFactory
{
    private static readonly string[] AllToolNames =
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
    };

    public static SessionContext CreateMinimal()
    {
        return new SessionContext
        {
            Session = new SessionInfo
            {
                RequestId = Guid.NewGuid().ToString("N"),
                TimestampUtc = DateTimeOffset.UtcNow
            },
            Policy = new PolicyInfo
            {
                EnabledTools = new List<string>(AllToolNames),
                ToolUnlockTier = ToolUnlockTier.Baseline
            }
        };
    }

    public static SessionContext CreateWithPart(string title = "TestPart", string path = @"C:\test\TestPart.SLDPRT")
    {
        SessionContext context = CreateMinimal();
        context.Document = new DocumentInfo
        {
            Type = "part",
            Title = title,
            Path = path,
            ActiveConfiguration = "Default",
            Units = "mm",
            IsDirty = false,
            IsReadOnly = false
        };
        context.Configurations = new ConfigurationsInfo
        {
            ActiveName = "Default",
            Count = 1,
            Items = new List<ConfigurationItem>
            {
                new ConfigurationItem { Name = "Default", IsActive = true }
            }
        };
        context.Diagnostics = new DiagnosticsInfo
        {
            RebuildState = "clean"
        };
        return context;
    }

    public static SessionContext CreateWithAssembly(string title = "TestAssembly", string path = @"C:\test\TestAssembly.SLDASM")
    {
        SessionContext context = CreateMinimal();
        context.Document = new DocumentInfo
        {
            Type = "assembly",
            Title = title,
            Path = path,
            ActiveConfiguration = "Default",
            Units = "mm",
            IsDirty = false,
            IsReadOnly = false
        };
        context.Configurations = new ConfigurationsInfo
        {
            ActiveName = "Default",
            Count = 1,
            Items = new List<ConfigurationItem>
            {
                new ConfigurationItem { Name = "Default", IsActive = true }
            }
        };
        context.Diagnostics = new DiagnosticsInfo
        {
            RebuildState = "clean"
        };
        context.ReferenceGraph = new ReferenceGraphInfo
        {
            DirectCount = 2,
            TransitiveCount = 3,
            DirectItems = new List<ReferenceNode>
            {
                new ReferenceNode { Name = "Part1.SLDPRT", Path = @"C:\test\Part1.SLDPRT", ExistsOnDisk = true },
                new ReferenceNode { Name = "Part2.SLDPRT", Path = @"C:\test\Part2.SLDPRT", ExistsOnDisk = true }
            }
        };
        context.Mates = new MatesInfo
        {
            Count = 2,
            Items = new List<MateNode>
            {
                new MateNode { Name = "Coincident1", Kind = "Coincident", EntityCount = 2, Components = new List<string> { "Part1-1", "Part2-1" } },
                new MateNode { Name = "Concentric1", Kind = "Concentric", EntityCount = 2, Components = new List<string> { "Part1-1", "Part2-1" } }
            }
        };
        return context;
    }

    public static SessionContext CreateWithSelection()
    {
        SessionContext context = CreateWithPart();
        context.Selection = new SelectionInfo
        {
            Count = 2,
            Items = new List<SelectionItem>
            {
                new SelectionItem { Kind = "Face", Name = "Face1", Owner = "Boss-Extrude1" },
                new SelectionItem { Kind = "Edge", Name = "Edge1", Owner = "Boss-Extrude1" }
            }
        };
        return context;
    }

    public static SessionContext CreateWithFeatures()
    {
        SessionContext context = CreateWithPart();
        context.FeatureTree = new FeatureTreeInfo
        {
            Anchor = "Boss-Extrude1",
            Radius = 8,
            Features = new List<FeatureNode>
            {
                new FeatureNode { Name = "Origin", Kind = "OriginProfileFeature", State = "active" },
                new FeatureNode { Name = "Top Plane", Kind = "RefPlane", State = "active" },
                new FeatureNode { Name = "Front Plane", Kind = "RefPlane", State = "active" },
                new FeatureNode { Name = "Right Plane", Kind = "RefPlane", State = "active" },
                new FeatureNode { Name = "Sketch1", Kind = "ProfileFeature", State = "active" },
                new FeatureNode { Name = "Boss-Extrude1", Kind = "Extrusion", State = "active" },
                new FeatureNode { Name = "Sketch2", Kind = "ProfileFeature", State = "active" },
                new FeatureNode { Name = "Cut-Extrude1", Kind = "Cut", State = "active" },
                new FeatureNode { Name = "Fillet1", Kind = "Fillet", State = "suppressed" }
            }
        };
        return context;
    }

    public static SessionContext CreateWithDimensions()
    {
        SessionContext context = CreateWithPart();
        context.Dimensions = new DimensionsInfo
        {
            Count = 3,
            Items = new List<DimensionNode>
            {
                new DimensionNode { Name = "D1", FullName = "D1@Sketch1", Value = 50.0, UnitSource = "document" },
                new DimensionNode { Name = "D2", FullName = "D2@Sketch1", Value = 25.0, UnitSource = "document" },
                new DimensionNode { Name = "D3", FullName = "D3@Boss-Extrude1", Value = 10.0, UnitSource = "document" }
            }
        };
        return context;
    }

    public static SessionContext CreateWithDiagnosticIssues()
    {
        SessionContext context = CreateWithPart();
        context.Diagnostics = new DiagnosticsInfo
        {
            RebuildState = "needs_rebuild",
            Warnings = new List<string> { "Feature Boss-Extrude1 has a rebuild warning." },
            MissingReferences = new List<string> { @"C:\missing\ref.SLDPRT" }
        };
        return context;
    }

    public static SessionContext CreateReadOnly()
    {
        SessionContext context = CreateWithPart();
        context.Document!.IsReadOnly = true;
        return context;
    }

    public static SessionContext CreateUnsaved()
    {
        SessionContext context = CreateWithPart();
        context.Document!.Path = string.Empty;
        return context;
    }

    public static SessionContext CreateWithCustomProperties()
    {
        SessionContext context = CreateWithPart();
        context.Properties = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_custom.Material"] = "Steel",
            ["document_custom.Author"] = "TestUser",
            ["configuration_custom.Default.Weight"] = "1.5 kg",
            ["configuration_custom.Default.Finish"] = "Brushed"
        };
        return context;
    }

    public static SessionContext CreateWithBrokenReferences()
    {
        SessionContext context = CreateWithAssembly();
        context.ReferenceGraph.BrokenReferenceCount = 1;
        context.ReferenceGraph.DirectItems.Add(new ReferenceNode
        {
            Name = "MissingPart.SLDPRT",
            Path = @"C:\test\MissingPart.SLDPRT",
            ExistsOnDisk = false,
            IsBroken = true
        });
        return context;
    }

    public static SessionContext CreateLargeAssembly(int dimensionCount = 500, int mateCount = 200, int referenceCount = 150)
    {
        SessionContext context = CreateWithAssembly("LargeAssembly", @"C:\test\LargeAssembly.SLDASM");

        var dims = new List<DimensionNode>();
        for (int i = 0; i < dimensionCount; i++)
        {
            dims.Add(new DimensionNode
            {
                Name = $"D{i + 1}",
                FullName = $"D{i + 1}@Feature{i / 5 + 1}",
                Value = 10.0 + i * 0.5,
                UnitSource = "document"
            });
        }
        context.Dimensions = new DimensionsInfo { Count = dimensionCount, Items = dims };

        var mates = new List<MateNode>();
        for (int i = 0; i < mateCount; i++)
        {
            mates.Add(new MateNode
            {
                Name = $"Coincident{i + 1}",
                Kind = i % 3 == 0 ? "Coincident" : i % 3 == 1 ? "Concentric" : "Distance",
                EntityCount = 2,
                Components = new List<string> { $"Part{i / 2 + 1}-1", $"Part{i / 2 + 2}-1" }
            });
        }
        context.Mates = new MatesInfo { Count = mateCount, Items = mates };

        var refs = new List<ReferenceNode>();
        for (int i = 0; i < referenceCount; i++)
        {
            refs.Add(new ReferenceNode
            {
                Name = $"Component{i + 1}.SLDPRT",
                Path = $@"C:\test\parts\Component{i + 1}.SLDPRT",
                ExistsOnDisk = true
            });
        }
        context.ReferenceGraph = new ReferenceGraphInfo
        {
            DirectCount = referenceCount,
            TransitiveCount = referenceCount,
            DirectItems = refs
        };

        return context;
    }

    public static SessionContext CreateWithDrawing(string title = "TestDrawing", string path = @"C:\test\TestDrawing.SLDDRW")
    {
        SessionContext context = CreateMinimal();
        context.Document = new DocumentInfo
        {
            Type = "drawing",
            Title = title,
            Path = path,
            ActiveConfiguration = "Default",
            Units = "mm",
            IsDirty = false,
            IsReadOnly = false
        };
        context.Configurations = new ConfigurationsInfo
        {
            ActiveName = "Default",
            Count = 1,
            Items = new List<ConfigurationItem>
            {
                new ConfigurationItem { Name = "Default", IsActive = true }
            }
        };
        context.Diagnostics = new DiagnosticsInfo
        {
            RebuildState = "clean"
        };
        context.ReferenceGraph = new ReferenceGraphInfo
        {
            DirectCount = 1,
            DirectItems = new List<ReferenceNode>
            {
                new ReferenceNode { Name = "Part1.SLDPRT", Path = @"C:\test\Part1.SLDPRT", ExistsOnDisk = true }
            }
        };
        return context;
    }
}
