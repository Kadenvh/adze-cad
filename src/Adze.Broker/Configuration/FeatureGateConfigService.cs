using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Adze.Broker.Configuration;

/// <summary>
/// Loads and saves the user-facing feature-gate configuration at
/// <c>%LOCALAPPDATA%\Adze\config.json</c>. The config is a flat map of
/// gate-env-var-name -> bool. Missing keys fall through to baked-in defaults
/// (see <see cref="FeatureGateRegistry.GetDefault"/>). Environment variables
/// set at launch time override everything here.
///
/// The service is intentionally dependency-free (no System.Text.Json on this
/// target framework). A tiny hand-rolled JSON writer keeps the file
/// human-readable so users can edit it by hand if they prefer.
/// </summary>
public static class FeatureGateConfigService
{
    private const string ConfigFileName = "config.json";
    private const string ConfigDirName = "Adze";

    /// <summary>
    /// Returns the resolved absolute path to the config file. Creates the
    /// parent directory on demand so Save never needs to.
    /// </summary>
    public static string GetConfigPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(root, ConfigDirName);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return Path.Combine(dir, ConfigFileName);
    }

    /// <summary>
    /// Loads the config from disk. Returns an empty dictionary if the file is
    /// missing, malformed, or unreadable — callers must fall through to the
    /// baked-in defaults in <see cref="FeatureGateRegistry.GetDefault"/>.
    /// </summary>
    public static Dictionary<string, bool> Load()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string path = GetConfigPath();
            if (!File.Exists(path)) return result;

            string content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content)) return result;

            Parse(content, result);
        }
        catch
        {
            // Corrupted / unreadable config is non-fatal — treat as empty and
            // let defaults apply. A future run that calls Save will overwrite.
        }

        return result;
    }

    /// <summary>
    /// Saves the provided config to disk, overwriting any existing file.
    /// </summary>
    public static void Save(Dictionary<string, bool> config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        string path = GetConfigPath();
        string json = Serialize(config);

        // Write atomically: write to a sibling, then move over the target.
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        File.Move(temp, path);
    }

    /// <summary>
    /// Convenience helper that loads the current config, sets or clears a
    /// single key, then saves.
    /// </summary>
    public static void SetGate(string gateName, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(gateName)) throw new ArgumentException("gateName", nameof(gateName));

        Dictionary<string, bool> config = Load();
        config[gateName] = enabled;
        Save(config);
    }

    // --- Serialization helpers --------------------------------------------

    private static string Serialize(Dictionary<string, bool> config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        int i = 0;
        foreach (KeyValuePair<string, bool> entry in config)
        {
            sb.Append("  \"");
            AppendEscaped(sb, entry.Key);
            sb.Append("\": ");
            sb.Append(entry.Value ? "true" : "false");
            if (i < config.Count - 1) sb.Append(",");
            sb.AppendLine();
            i++;
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendEscaped(StringBuilder sb, string value)
    {
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
    }

    // --- Parser (tolerant, tiny) ------------------------------------------

    private static void Parse(string content, Dictionary<string, bool> result)
    {
        int i = 0;
        SkipWhitespace(content, ref i);
        if (i >= content.Length || content[i] != '{') return;
        i++; // consume '{'

        while (i < content.Length)
        {
            SkipWhitespace(content, ref i);
            if (i < content.Length && content[i] == '}') break;

            // Key
            if (i >= content.Length || content[i] != '"') return;
            string key = ReadString(content, ref i);
            SkipWhitespace(content, ref i);
            if (i >= content.Length || content[i] != ':') return;
            i++; // consume ':'

            // Value — only bool supported
            SkipWhitespace(content, ref i);
            if (i >= content.Length) return;

            if (Match(content, i, "true"))
            {
                result[key] = true;
                i += 4;
            }
            else if (Match(content, i, "false"))
            {
                result[key] = false;
                i += 5;
            }
            else
            {
                // Unsupported value type — skip the entry rather than abort.
                while (i < content.Length && content[i] != ',' && content[i] != '}') i++;
            }

            SkipWhitespace(content, ref i);
            if (i < content.Length && content[i] == ',') i++;
        }
    }

    private static string ReadString(string content, ref int i)
    {
        if (content[i] != '"') return string.Empty;
        i++; // consume opening quote
        var sb = new StringBuilder();
        while (i < content.Length && content[i] != '"')
        {
            if (content[i] == '\\' && i + 1 < content.Length)
            {
                char next = content[i + 1];
                switch (next)
                {
                    case '"': sb.Append('"'); i += 2; break;
                    case '\\': sb.Append('\\'); i += 2; break;
                    case 'n': sb.Append('\n'); i += 2; break;
                    case 't': sb.Append('\t'); i += 2; break;
                    case 'r': sb.Append('\r'); i += 2; break;
                    default: sb.Append(content[i]); i++; break;
                }
            }
            else
            {
                sb.Append(content[i]);
                i++;
            }
        }
        if (i < content.Length && content[i] == '"') i++; // consume closing quote
        return sb.ToString();
    }

    private static void SkipWhitespace(string content, ref int i)
    {
        while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
    }

    private static bool Match(string content, int i, string token)
    {
        if (i + token.Length > content.Length) return false;
        for (int j = 0; j < token.Length; j++)
        {
            if (content[i + j] != token[j]) return false;
        }
        return true;
    }
}
