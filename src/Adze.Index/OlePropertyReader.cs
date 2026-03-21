using System;
using System.Collections.Generic;
using System.IO;
using Adze.Contracts.Models;
using OpenMcdf;

namespace Adze.Index;

/// <summary>
/// Reads custom properties and summary information from SOLIDWORKS files
/// using OLE Structured Storage (no SOLIDWORKS dependency required).
/// </summary>
public static class OlePropertyReader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sldprt", ".sldasm", ".slddrw"
    };

    public static bool IsSupportedFile(string filePath)
    {
        return !string.IsNullOrEmpty(filePath) &&
               SupportedExtensions.Contains(Path.GetExtension(filePath));
    }

    public static string GetFileType(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".sldprt" => "part",
            ".sldasm" => "assembly",
            ".slddrw" => "drawing",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Reads metadata and custom properties from a SOLIDWORKS file via OLE Structured Storage.
    /// Returns null if the file cannot be read.
    /// </summary>
    public static ClosedFileRecord? ReadFile(string filePath)
    {
        if (!File.Exists(filePath) || !IsSupportedFile(filePath))
            return null;

        var fileInfo = new FileInfo(filePath);
        var record = new ClosedFileRecord
        {
            FilePath = fileInfo.FullName,
            FileName = fileInfo.Name,
            FileType = GetFileType(filePath),
            FileSizeBytes = fileInfo.Length,
            LastWriteUtc = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
            IndexedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            using var cf = new CompoundFile(filePath, CFSUpdateMode.ReadOnly, CFSConfiguration.Default);
            ReadCustomProperties(cf, record);
        }
        catch
        {
            // File may be locked or corrupt — return what we have from filesystem metadata
        }

        return record;
    }

    private static void ReadCustomProperties(CompoundFile cf, ClosedFileRecord record)
    {
        // SOLIDWORKS stores custom properties in a stream called "\005HkrCustomDocumentProperties"
        // or under the DocumentSummaryInformation user-defined properties.
        // Try the SOLIDWORKS-specific stream first.
        try
        {
            CFStream? customStream = TryGetStream(cf.RootStorage, "\x0005HkrCustomDocumentProperties");
            if (customStream != null)
            {
                byte[] data = customStream.GetData();
                ParseSolidWorksCustomProperties(data, record);
            }
        }
        catch
        {
            // Not all files have this stream
        }

        // Try to read summary information properties
        try
        {
            CFStream? summaryStream = TryGetStream(cf.RootStorage, "\x0005SummaryInformation");
            if (summaryStream != null)
            {
                byte[] data = summaryStream.GetData();
                ParseSummaryInformation(data, record);
            }
        }
        catch
        {
            // Summary stream may not exist
        }

        // Try DocumentSummaryInformation for company and other metadata
        try
        {
            CFStream? docSummaryStream = TryGetStream(cf.RootStorage, "\x0005DocumentSummaryInformation");
            if (docSummaryStream != null)
            {
                byte[] data = docSummaryStream.GetData();
                ParseDocumentSummaryInformation(data, record);
            }
        }
        catch
        {
            // Doc summary stream may not exist
        }
    }

    private static CFStream? TryGetStream(CFStorage storage, string name)
    {
        try
        {
            return storage.GetStream(name);
        }
        catch
        {
            return null;
        }
    }

    private static void ParseSolidWorksCustomProperties(byte[] data, ClosedFileRecord record)
    {
        // SOLIDWORKS custom properties are stored in OLE property set format.
        // The format is a serialized property set with string name/value pairs.
        // We use a simplified parser for the most common encoding.
        if (data == null || data.Length < 48) return;

        try
        {
            var properties = OlePropertySetParser.ParsePropertySet(data);
            foreach (var kvp in properties)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    record.CustomProperties[kvp.Key] = kvp.Value;
                }
            }
        }
        catch
        {
            // Parsing failure — leave custom properties empty
        }
    }

    private static void ParseSummaryInformation(byte[] data, ClosedFileRecord record)
    {
        if (data == null || data.Length < 48) return;

        try
        {
            var properties = OlePropertySetParser.ParsePropertySet(data);

            // Standard SummaryInformation property IDs:
            // PID 2 = Title, PID 3 = Subject, PID 4 = Author, PID 5 = Keywords, PID 6 = Comments
            if (properties.TryGetValue("_PID_2", out string? title)) record.Title = title;
            if (properties.TryGetValue("_PID_3", out string? subject)) record.Subject = subject;
            if (properties.TryGetValue("_PID_4", out string? author)) record.Author = author;
            if (properties.TryGetValue("_PID_5", out string? keywords)) record.Keywords = keywords;
            if (properties.TryGetValue("_PID_6", out string? comments)) record.Comments = comments;
        }
        catch
        {
            // Parsing failure
        }
    }

    private static void ParseDocumentSummaryInformation(byte[] data, ClosedFileRecord record)
    {
        if (data == null || data.Length < 48) return;

        try
        {
            var properties = OlePropertySetParser.ParsePropertySet(data);

            // PID 15 = Company in DocumentSummaryInformation
            if (properties.TryGetValue("_PID_15", out string? company)) record.Company = company;
        }
        catch
        {
            // Parsing failure
        }
    }
}
