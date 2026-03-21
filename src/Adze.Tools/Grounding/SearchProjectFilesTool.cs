using System;
using System.Collections.Generic;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Index;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class SearchProjectFilesTool : IReadOnlyTool<SearchProjectFilesParameters>
{
    public string ToolName => ToolNames.SearchProjectFiles;

    public ToolResult Execute(SessionContext context, SearchProjectFilesParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = false,
            Summary = "Search not executed."
        };

        if (string.IsNullOrWhiteSpace(parameters.RootFolderPath))
        {
            result.Summary = "root_folder_path is required.";
            return result;
        }

        try
        {
            var indexer = new ClosedFileIndexer();

            // Build or refresh the index for this folder
            IndexRunResult indexResult = indexer.BuildIndex(parameters.RootFolderPath);

            if (indexResult.Errors > 0 && indexResult.FilesIndexed == 0)
            {
                result.Summary = "Index build failed: " + string.Join("; ", indexResult.ErrorMessages);
                result.Warnings = indexResult.ErrorMessages;
                return result;
            }

            // Search the index
            var query = new ClosedFileSearchQuery
            {
                FileType = parameters.FileType,
                PathPattern = parameters.PathPattern,
                PropertyName = parameters.PropertyName,
                PropertyValue = parameters.PropertyValue,
                Keyword = parameters.Keyword,
                MaxResults = parameters.MaxResults > 0 ? parameters.MaxResults : 20
            };

            var searchService = new ClosedFileSearchService(indexer);
            List<ClosedFileSearchResult> matches = searchService.Search(parameters.RootFolderPath, query);

            result.Success = true;
            result.Summary = matches.Count == 0
                ? "No matching files found."
                : $"Found {matches.Count} matching file(s).";

            result.Data["files_indexed"] = indexResult.FilesIndexed;
            result.Data["files_scanned"] = indexResult.FilesScanned;
            result.Data["match_count"] = matches.Count;
            result.Data["root_folder"] = parameters.RootFolderPath;

            var matchData = new List<Dictionary<string, object?>>();
            foreach (ClosedFileSearchResult match in matches)
            {
                matchData.Add(new Dictionary<string, object?>
                {
                    ["file_path"] = match.FilePath,
                    ["file_name"] = match.FileName,
                    ["file_type"] = match.FileType,
                    ["match_reason"] = match.MatchReason,
                    ["properties"] = match.MatchedProperties
                });
            }

            result.Data["matches"] = matchData;

            if (indexResult.Errors > 0)
            {
                result.Warnings = indexResult.ErrorMessages;
            }
        }
        catch (Exception ex)
        {
            result.Summary = "Search failed: " + ex.Message;
        }

        return result;
    }
}
