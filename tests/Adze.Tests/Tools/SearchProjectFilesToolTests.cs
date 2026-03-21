using System;
using System.Collections.Generic;
using System.IO;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Grounding;
using Adze.Broker.Abstractions;
using Adze.Broker.Formatting;
using Adze.Broker.Orchestration;
using Adze.Broker.Models;
using NUnit.Framework;

namespace Adze.Tests.Tools;

[TestFixture]
public sealed class SearchProjectFilesToolTests
{
    private SearchProjectFilesTool _tool = null!;
    private SessionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _tool = new SearchProjectFilesTool();
        _context = Helpers.SessionContextFactory.CreateMinimal();
    }

    [Test]
    public void ToolName_IsCorrect()
    {
        Assert.That(_tool.ToolName, Is.EqualTo(ToolNames.SearchProjectFiles));
    }

    [Test]
    public void Execute_EmptyRootPath_ReturnsError()
    {
        var parameters = new SearchProjectFilesParameters { RootFolderPath = "" };
        ToolResult result = _tool.Execute(_context, parameters);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Does.Contain("root_folder_path"));
    }

    [Test]
    public void Execute_NonexistentDirectory_ReturnsFailure()
    {
        var parameters = new SearchProjectFilesParameters
        {
            RootFolderPath = @"C:\nonexistent\path\for\test"
        };
        ToolResult result = _tool.Execute(_context, parameters);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Does.Contain("failed"));
    }

    [Test]
    public void Execute_EmptyDirectory_ReturnsZeroMatches()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "adze_search_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var parameters = new SearchProjectFilesParameters
            {
                RootFolderPath = tempDir
            };
            ToolResult result = _tool.Execute(_context, parameters);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Summary, Does.Contain("No matching"));
            Assert.That(result.Data["match_count"], Is.EqualTo(0));
            Assert.That(result.Data["files_indexed"], Is.EqualTo(0));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Execute_WithKeyword_ReturnsFilteredResults()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "adze_search_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var parameters = new SearchProjectFilesParameters
            {
                RootFolderPath = tempDir,
                Keyword = "nonexistent_keyword_xyz"
            };
            ToolResult result = _tool.Execute(_context, parameters);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data["match_count"], Is.EqualTo(0));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Execute_WithFileTypeFilter_ReturnsSuccess()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "adze_search_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var parameters = new SearchProjectFilesParameters
            {
                RootFolderPath = tempDir,
                FileType = "part"
            };
            ToolResult result = _tool.Execute(_context, parameters);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data.ContainsKey("matches"), Is.True);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void SearchProjectFilesParameters_Defaults_AreCorrect()
    {
        var parameters = new SearchProjectFilesParameters();
        Assert.That(parameters.RootFolderPath, Is.EqualTo(string.Empty));
        Assert.That(parameters.MaxResults, Is.EqualTo(20));
        Assert.That(parameters.FileType, Is.Null);
        Assert.That(parameters.Keyword, Is.Null);
        Assert.That(parameters.PropertyName, Is.Null);
    }

    // --- ToolDefinitionBuilder tests ---

    [Test]
    public void BuildRetrievalToolDefinitions_ContainsSearchProjectFiles()
    {
        List<AgentToolDefinition> defs = ToolDefinitionBuilder.BuildRetrievalToolDefinitions();

        Assert.That(defs.Count, Is.EqualTo(1));
        Assert.That(defs[0].Name, Is.EqualTo(ToolNames.SearchProjectFiles));
        Assert.That(defs[0].Description, Does.Contain("SOLIDWORKS"));
    }

    [Test]
    public void BuildAllToolDefinitions_WithRetrieval_IncludesSearchTool()
    {
        List<AgentToolDefinition> withRetrieval = ToolDefinitionBuilder.BuildAllToolDefinitions(includeRetrieval: true);
        List<AgentToolDefinition> withoutRetrieval = ToolDefinitionBuilder.BuildAllToolDefinitions(includeRetrieval: false);

        Assert.That(withRetrieval.Count, Is.EqualTo(withoutRetrieval.Count + 1));
        Assert.That(withRetrieval.Exists(d => d.Name == ToolNames.SearchProjectFiles), Is.True);
        Assert.That(withoutRetrieval.Exists(d => d.Name == ToolNames.SearchProjectFiles), Is.False);
    }

    [Test]
    public void BuildAllToolDefinitions_DefaultNoRetrieval()
    {
        List<AgentToolDefinition> defs = ToolDefinitionBuilder.BuildAllToolDefinitions();
        Assert.That(defs.Exists(d => d.Name == ToolNames.SearchProjectFiles), Is.False);
    }

    // --- AgentToolDispatcher tests ---

    [Test]
    public void Dispatcher_SearchProjectFiles_EmptyPath_ReturnsError()
    {
        var dispatcher = new AgentToolDispatcher();
        var args = new Dictionary<string, object?> { ["root_folder_path"] = "" };
        var execContext = new ToolExecutionContext
        {
            SessionId = "test",
            DocumentKey = "test",
            SessionContext = _context
        };

        AgentToolResult result = dispatcher.Execute(ToolNames.SearchProjectFiles, args, execContext);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("root_folder_path"));
    }

    [Test]
    public void Dispatcher_SearchProjectFiles_NonexistentPath_HandlesGracefully()
    {
        var dispatcher = new AgentToolDispatcher();
        var args = new Dictionary<string, object?>
        {
            ["root_folder_path"] = @"C:\nonexistent\test\folder",
            ["keyword"] = "test"
        };
        var execContext = new ToolExecutionContext
        {
            SessionId = "test",
            DocumentKey = "test",
            SessionContext = _context
        };

        AgentToolResult result = dispatcher.Execute(ToolNames.SearchProjectFiles, args, execContext);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("search_project_files"));
    }

    [Test]
    public void RetrievalDefinitionSchema_HasRequiredRootPath()
    {
        List<AgentToolDefinition> defs = ToolDefinitionBuilder.BuildRetrievalToolDefinitions();
        var schema = defs[0].ParameterSchema;

        Assert.That(schema.ContainsKey("required"), Is.True);
        var required = schema["required"] as List<string>;
        Assert.That(required, Is.Not.Null);
        Assert.That(required!, Does.Contain("root_folder_path"));
    }
}
