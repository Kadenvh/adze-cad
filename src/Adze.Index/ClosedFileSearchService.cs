using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Models;

namespace Adze.Index;

/// <summary>
/// Queries a previously built closed-file index by property values, file type, and path patterns.
/// </summary>
public sealed class ClosedFileSearchService
{
    private readonly ClosedFileIndexer _indexer;

    public ClosedFileSearchService(ClosedFileIndexer indexer)
    {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
    }

    public List<ClosedFileSearchResult> Search(string rootFolderPath, ClosedFileSearchQuery query)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        List<ClosedFileRecord> records = _indexer.LoadIndex(rootFolderPath);
        if (records.Count == 0) return new List<ClosedFileSearchResult>();

        IEnumerable<ClosedFileRecord> filtered = records;

        // Filter by file type
        if (!string.IsNullOrWhiteSpace(query.FileType))
        {
            filtered = filtered.Where(r =>
                string.Equals(r.FileType, query.FileType, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by path pattern (substring match)
        if (!string.IsNullOrWhiteSpace(query.PathPattern))
        {
            filtered = filtered.Where(r =>
                r.FilePath.IndexOf(query.PathPattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Filter by property name/value
        if (!string.IsNullOrWhiteSpace(query.PropertyName))
        {
            filtered = filtered.Where(r =>
            {
                if (!r.CustomProperties.TryGetValue(query.PropertyName!, out string? value))
                    return false;

                if (string.IsNullOrWhiteSpace(query.PropertyValue))
                    return true; // Just check existence

                return value != null && value.IndexOf(query.PropertyValue!, StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        // Filter by keyword (searches across all string fields)
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            string kw = query.Keyword!;
            filtered = filtered.Where(r =>
                ContainsIgnoreCase(r.FileName, kw) ||
                ContainsIgnoreCase(r.Title, kw) ||
                ContainsIgnoreCase(r.Author, kw) ||
                ContainsIgnoreCase(r.Keywords, kw) ||
                ContainsIgnoreCase(r.Comments, kw) ||
                r.CustomProperties.Values.Any(v => ContainsIgnoreCase(v, kw)));
        }

        return filtered
            .Take(query.MaxResults)
            .Select(r => new ClosedFileSearchResult
            {
                FilePath = r.FilePath,
                FileName = r.FileName,
                FileType = r.FileType,
                MatchReason = BuildMatchReason(r, query),
                MatchedProperties = new Dictionary<string, string>(r.CustomProperties)
            })
            .ToList();
    }

    private static string BuildMatchReason(ClosedFileRecord record, ClosedFileSearchQuery query)
    {
        var reasons = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.FileType))
            reasons.Add("type=" + record.FileType);

        if (!string.IsNullOrWhiteSpace(query.PropertyName) &&
            record.CustomProperties.TryGetValue(query.PropertyName!, out string? propVal))
            reasons.Add(query.PropertyName + "=" + propVal);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
            reasons.Add("keyword match: " + query.Keyword);

        if (!string.IsNullOrWhiteSpace(query.PathPattern))
            reasons.Add("path match: " + query.PathPattern);

        return reasons.Count > 0 ? string.Join(", ", reasons) : "matched";
    }

    private static bool ContainsIgnoreCase(string? source, string value)
    {
        return source != null && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
