using System.Collections.Generic;
using NUnit.Framework;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Grounding;
using Adze.Tests.Helpers;

namespace Adze.Tests.Tools;

[TestFixture]
public class LargeAssemblyPaginationTests
{
    // --- Dimensions pagination edge cases ---

    [Test]
    public void GetDimensions_LargeAssembly_DefaultPagination_Returns50()
    {
        SessionContext context = SessionContextFactory.CreateLargeAssembly(dimensionCount: 500);
        var tool = new GetDimensionsTool();
        var parameters = new GetDimensionsParameters { Scope = "document" };

        ToolResult result = tool.Execute(context, parameters);

        Assert.IsTrue(result.Success);
        var data = result.Data as Dictionary<string, object?>;
        Assert.AreEqual(500, data!["total_count"]);
        Assert.AreEqual(50, data["returned_count"]);
        Assert.AreEqual(true, data["has_more"]);
    }

    [Test]
    public void GetDimensions_LargeAssembly_OffsetBeyondTotal_ReturnsEmpty()
    {
        SessionContext context = SessionContextFactory.CreateLargeAssembly(dimensionCount: 100);
        var tool = new GetDimensionsTool();
        var parameters = new GetDimensionsParameters
        {
            Scope = "document",
            Offset = 200,
            Limit = 50
        };

        ToolResult result = tool.Execute(context, parameters);

        Assert.IsTrue(result.Success);
        var data = result.Data as Dictionary<string, object?>;
        Assert.AreEqual(100, data!["total_count"]);
        Assert.AreEqual(0, data["returned_count"]);
        Assert.AreEqual(false, data["has_more"]);
    }

    [Test]
    public void GetDimensions_LargeAssembly_LimitCappedAt200()
    {
        SessionContext context = SessionContextFactory.CreateLargeAssembly(dimensionCount: 500);
        var tool = new GetDimensionsTool();
        var parameters = new GetDimensionsParameters
        {
            Scope = "document",
            Limit = 999
        };

        ToolResult result = tool.Execute(context, parameters);

        Assert.IsTrue(result.Success);
        var data = result.Data as Dictionary<string, object?>;
        int returnedCount = (int)data!["returned_count"]!;
        Assert.That(returnedCount, Is.LessThanOrEqualTo(200));
    }

    [Test]
    public void GetDimensions_LargeAssembly_NegativeOffset_TreatedAsZero()
    {
        SessionContext context = SessionContextFactory.CreateLargeAssembly(dimensionCount: 100);
        var tool = new GetDimensionsTool();
        var parameters = new GetDimensionsParameters
        {
            Scope = "document",
            Offset = -5,
            Limit = 10
        };

        ToolResult result = tool.Execute(context, parameters);

        Assert.IsTrue(result.Success);
        var data = result.Data as Dictionary<string, object?>;
        Assert.AreEqual(0, data!["offset"]);
        Assert.AreEqual(10, data["returned_count"]);
    }

    [Test]
    public void GetDimensions_LargeAssembly_Limit1_ReturnsSingleItem()
    {
        SessionContext context = SessionContextFactory.CreateLargeAssembly(dimensionCount: 500);
        var tool = new GetDimensionsTool();
        var parameters = new GetDimensionsParameters
        {
            Scope = "document",
            Limit = 1
        };

        ToolResult result = tool.Execute(context, parameters);

        Assert.IsTrue(result.Success);
        var data = result.Data as Dictionary<string, object?>;
        Assert.AreEqual(1, data!["returned_count"]);
        Assert.AreEqual(true, data["has_more"]);
    }

    [Test]
    public void GetDimensions_LargeAssembly_FullPaginationWalk_CoversAllItems()
    {
        SessionContext context = SessionContextFactory.CreateLargeAssembly(dimensionCount: 125);
        var tool = new GetDimensionsTool();
        int totalCollected = 0;
        int offset = 0;
        int limit = 50;

        while (true)
        {
            var parameters = new GetDimensionsParameters
            {
                Scope = "document",
                Offset = offset,
                Limit = limit
            };

            ToolResult result = tool.Execute(context, parameters);
            Assert.IsTrue(result.Success);
            var data = result.Data as Dictionary<string, object?>;
            int returned = (int)data!["returned_count"]!;
            totalCollected += returned;

            bool hasMore = (bool)data["has_more"]!;
            if (!hasMore) break;

            offset += returned;
        }

        Assert.AreEqual(125, totalCollected);
    }

    // --- Mates pagination edge cases ---

    [Test]
    public void GetMates_LargeAssembly_DefaultPagination_Returns50()
    {
        SessionContext context = SessionContextFactory.CreateLargeAssembly(mateCount: 200);
        var tool = new GetMatesTool();
        var parameters = new GetMatesParameters { Scope = "document" };

        ToolResult result = tool.Execute(context, parameters);

        Assert.IsTrue(result.Success);
        var data = result.Data as Dictionary<string, object?>;
        Assert.AreEqual(200, data!["total_count"]);
        Assert.AreEqual(50, data["returned_count"]);
        Assert.AreEqual(true, data["has_more"]);
    }

    [Test]
    public void GetMates_LargeAssembly_FullPaginationWalk_CoversAllItems()
    {
        SessionContext context = SessionContextFactory.CreateLargeAssembly(mateCount: 83);
        var tool = new GetMatesTool();
        int totalCollected = 0;
        int offset = 0;

        while (true)
        {
            var parameters = new GetMatesParameters
            {
                Scope = "document",
                Offset = offset,
                Limit = 25
            };

            ToolResult result = tool.Execute(context, parameters);
            Assert.IsTrue(result.Success);
            var data = result.Data as Dictionary<string, object?>;
            int returned = (int)data!["returned_count"]!;
            totalCollected += returned;

            bool hasMore = (bool)data["has_more"]!;
            if (!hasMore) break;

            offset += returned;
        }

        Assert.AreEqual(83, totalCollected);
    }

    // --- Reference graph limiting ---

    [Test]
    public void GetReferenceGraph_LargeAssembly_DefaultLimit_Clips()
    {
        SessionContext context = SessionContextFactory.CreateLargeAssembly(referenceCount: 150);
        var tool = new GetReferenceGraphTool();
        var parameters = new GetReferenceGraphParameters();

        ToolResult result = tool.Execute(context, parameters);

        Assert.IsTrue(result.Success);
        var data = result.Data as Dictionary<string, object?>;
        int returnedCount = (int)data!["returned_count"]!;
        Assert.That(returnedCount, Is.LessThanOrEqualTo(100));
    }
}
