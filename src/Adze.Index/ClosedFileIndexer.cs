using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;
using Adze.Contracts.Models;

namespace Adze.Index;

/// <summary>
/// Scans a folder for SOLIDWORKS files, reads metadata via OLE Structured Storage,
/// and persists the index as JSON under %LOCALAPPDATA%\Adze\index\.
/// </summary>
public sealed class ClosedFileIndexer
{
    private static readonly string IndexRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Adze", "index");

    /// <summary>
    /// Scans <paramref name="rootFolderPath"/> recursively for .SLDPRT, .SLDASM, .SLDDRW files,
    /// reads their OLE metadata, and stores the index.
    /// </summary>
    public IndexRunResult BuildIndex(string rootFolderPath)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new IndexRunResult();
        var records = new List<ClosedFileRecord>();

        if (!Directory.Exists(rootFolderPath))
        {
            result.Errors = 1;
            result.ErrorMessages.Add("Directory does not exist: " + rootFolderPath);
            result.Duration = stopwatch.Elapsed;
            return result;
        }

        string[] extensions = { "*.sldprt", "*.sldasm", "*.slddrw" };

        foreach (string pattern in extensions)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(rootFolderPath, pattern, SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add("Error scanning for " + pattern + ": " + ex.Message);
                continue;
            }

            foreach (string file in files)
            {
                result.FilesScanned++;
                try
                {
                    ClosedFileRecord? record = OlePropertyReader.ReadFile(file);
                    if (record != null)
                    {
                        records.Add(record);
                        result.FilesIndexed++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    if (result.ErrorMessages.Count < 10)
                    {
                        result.ErrorMessages.Add(Path.GetFileName(file) + ": " + ex.Message);
                    }
                }
            }
        }

        // Persist the index
        try
        {
            PersistIndex(rootFolderPath, records);
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.ErrorMessages.Add("Index persistence failed: " + ex.Message);
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Loads a previously built index for the given folder, or returns an empty list.
    /// </summary>
    public List<ClosedFileRecord> LoadIndex(string rootFolderPath)
    {
        string indexPath = GetIndexPath(rootFolderPath);
        if (!File.Exists(indexPath))
            return new List<ClosedFileRecord>();

        try
        {
            string json = File.ReadAllText(indexPath);
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var records = serializer.Deserialize<List<ClosedFileRecord>>(json);
            return records ?? new List<ClosedFileRecord>();
        }
        catch
        {
            return new List<ClosedFileRecord>();
        }
    }

    private void PersistIndex(string rootFolderPath, List<ClosedFileRecord> records)
    {
        string indexPath = GetIndexPath(rootFolderPath);
        string? dir = Path.GetDirectoryName(indexPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        string json = serializer.Serialize(records);
        File.WriteAllText(indexPath, json);
    }

    private static string GetIndexPath(string rootFolderPath)
    {
        string hash = ComputePathHash(rootFolderPath);
        return Path.Combine(IndexRoot, hash, "index.json");
    }

    private static string ComputePathHash(string path)
    {
        string normalized = path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
        return BitConverter.ToString(hashBytes, 0, 8).Replace("-", "").ToLowerInvariant();
    }
}
