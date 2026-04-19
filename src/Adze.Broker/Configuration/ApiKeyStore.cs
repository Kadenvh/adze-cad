using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Adze.Broker.Configuration;

/// <summary>
/// Stores the active AI provider selection and its API key in a DPAPI-encrypted
/// file under <c>%LOCALAPPDATA%\Adze\keys.dat</c>. DPAPI user-scope encryption
/// means the file is readable only by the same Windows user on the same
/// machine — if the user copies the file elsewhere, it will not decrypt.
///
/// Format (plaintext, pre-encryption):
///   provider=openai
///   key=sk-...
///
/// Callers should prefer environment variables (which win) but fall through
/// to this store when env is empty. The UI Settings panel writes here.
/// </summary>
public static class ApiKeyStore
{
    private const string KeysFileName = "keys.dat";
    private const string ConfigDirName = "Adze";

    // Static entropy ties the encryption to this application so that another
    // DPAPI consumer running as the same user cannot trivially decrypt our file.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Adze.SOLIDWORKS.ApiKeyStore.v1");

    /// <summary>Returns the absolute path to the keys file. Ensures the parent directory exists.</summary>
    public static string GetKeysPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(root, ConfigDirName);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, KeysFileName);
    }

    /// <summary>
    /// Saves an active provider and its API key. Overwrites any prior selection.
    /// </summary>
    public static void Save(string providerName, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("providerName", nameof(providerName));
        if (apiKey == null) apiKey = string.Empty;

        string plaintext = "provider=" + providerName.Trim() + "\n" + "key=" + apiKey.Trim() + "\n";
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);

        byte[] encrypted = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);

        string path = GetKeysPath();
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }

    /// <summary>
    /// Loads the stored provider+key. Returns (null, null) when the file is
    /// missing or unreadable. A corrupted file is treated as missing — the
    /// caller falls through to env vars / defaults.
    /// </summary>
    public static (string? Provider, string? Key) Load()
    {
        try
        {
            string path = GetKeysPath();
            if (!File.Exists(path)) return (null, null);

            byte[] encrypted = File.ReadAllBytes(path);
            if (encrypted.Length == 0) return (null, null);

            byte[] plainBytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            string plaintext = Encoding.UTF8.GetString(plainBytes);

            string? provider = null;
            string? key = null;
            foreach (string line in plaintext.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                if (k.Equals("provider", StringComparison.OrdinalIgnoreCase)) provider = v;
                else if (k.Equals("key", StringComparison.OrdinalIgnoreCase)) key = v;
            }

            return (provider, key);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Convenience — returns the API key if one is stored for the given
    /// provider name. Returns empty string otherwise.
    /// </summary>
    public static string GetKey(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName)) return string.Empty;
        (string? storedProvider, string? storedKey) = Load();
        if (storedProvider == null || storedKey == null) return string.Empty;
        return string.Equals(storedProvider, providerName, StringComparison.OrdinalIgnoreCase)
            ? storedKey
            : string.Empty;
    }

    /// <summary>Returns the configured provider name or null if nothing stored.</summary>
    public static string? GetConfiguredProvider()
    {
        (string? storedProvider, _) = Load();
        return storedProvider;
    }

    /// <summary>Deletes the stored keys file if it exists.</summary>
    public static void Clear()
    {
        try
        {
            string path = GetKeysPath();
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort clear — failure is not fatal to callers.
        }
    }

    /// <summary>Returns true if a provider and key are both stored.</summary>
    public static bool HasStoredKey()
    {
        (string? provider, string? key) = Load();
        return !string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(key);
    }
}
