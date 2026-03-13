using System;
using System.IO;

namespace Adze.Host.Infrastructure;

internal static class FileLogger
{
    private static readonly object Sync = new();

    private static string LogPath
    {
        get
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Adze",
                "logs");

            Directory.CreateDirectory(root);
            return Path.Combine(root, "host.log");
        }
    }

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception ex)
    {
        Write("ERROR", message + Environment.NewLine + ex);
    }

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.UtcNow:u}] {level} {message}{Environment.NewLine}");
        }
    }
}
