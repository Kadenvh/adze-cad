using System;
using System.Collections.Generic;
using System.Text;

namespace Adze.Index;

/// <summary>
/// Minimal parser for OLE Property Set streams (MS-OLEPS format).
/// Extracts string property values from SummaryInformation, DocumentSummaryInformation,
/// and user-defined property sets commonly found in SOLIDWORKS files.
/// </summary>
public static class OlePropertySetParser
{
    /// <summary>
    /// Parses an OLE property set byte stream and returns string properties.
    /// Named properties use their actual name; numbered properties use "_PID_N" format.
    /// </summary>
    public static Dictionary<string, string> ParsePropertySet(byte[] data)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (data == null || data.Length < 28) return result;

        try
        {
            // Property set header:
            // Offset 0: byte order (2 bytes)
            // Offset 2: format version (2 bytes)
            // Offset 4: OS version (4 bytes)
            // Offset 8: class ID (16 bytes)
            // Offset 24: number of property sets (4 bytes)

            int numSections = ReadInt32(data, 24);
            if (numSections < 1 || numSections > 2) return result;

            // Section 0 header starts at offset 28:
            // Offset 28: FMTID (16 bytes)
            // Offset 44: section offset (4 bytes)
            int sectionOffset = ReadInt32(data, 44);
            if (sectionOffset < 0 || sectionOffset >= data.Length) return result;

            ParseSection(data, sectionOffset, result);

            // If there's a second section (user-defined properties), parse it too
            if (numSections >= 2 && data.Length >= 68)
            {
                int section2Offset = ReadInt32(data, 64);
                if (section2Offset > 0 && section2Offset < data.Length)
                {
                    ParseSection(data, section2Offset, result);
                }
            }
        }
        catch
        {
            // Return whatever we managed to parse
        }

        return result;
    }

    private static void ParseSection(byte[] data, int sectionOffset, Dictionary<string, string> result)
    {
        if (sectionOffset + 8 > data.Length) return;

        int propertyCount = ReadInt32(data, sectionOffset + 4);

        if (propertyCount < 0 || propertyCount > 1000) return;

        // Property name dictionary (PID 0) may contain name mappings
        var nameDict = new Dictionary<int, string>();

        // First pass: read property ID/offset pairs
        var propertyEntries = new List<(int pid, int offset)>();
        for (int i = 0; i < propertyCount; i++)
        {
            int entryOffset = sectionOffset + 8 + (i * 8);
            if (entryOffset + 8 > data.Length) break;

            int pid = ReadInt32(data, entryOffset);
            int propOffset = ReadInt32(data, entryOffset + 4);
            propertyEntries.Add((pid, propOffset));
        }

        // Look for the dictionary property (PID 0) first
        foreach (var (pid, offset) in propertyEntries)
        {
            if (pid == 0)
            {
                int absoluteOffset = sectionOffset + offset;
                ParseNameDictionary(data, absoluteOffset, nameDict);
                break;
            }
        }

        // Second pass: read property values
        foreach (var (pid, offset) in propertyEntries)
        {
            if (pid == 0 || pid == 1) continue; // Skip dictionary and codepage

            int absoluteOffset = sectionOffset + offset;
            if (absoluteOffset + 4 > data.Length) continue;

            string? value = ReadPropertyValue(data, absoluteOffset);
            if (value == null) continue;

            string key = nameDict.TryGetValue(pid, out string? name) ? name : "_PID_" + pid;
            result[key] = value;
        }
    }

    private static string? ReadPropertyValue(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return null;

        int vt = ReadInt32(data, offset); // VARTYPE
        int vtType = vt & 0xFFFF;

        return vtType switch
        {
            // VT_LPSTR (30)
            30 => ReadLpstr(data, offset + 4),
            // VT_LPWSTR (31)
            31 => ReadLpwstr(data, offset + 4),
            // VT_I4 (3) — integer
            3 => ReadInt32(data, offset + 4).ToString(),
            // VT_R8 (5) — double
            5 when offset + 12 <= data.Length => BitConverter.ToDouble(data, offset + 4).ToString("G"),
            // VT_BOOL (11)
            11 => ReadInt32(data, offset + 4) != 0 ? "true" : "false",
            _ => null
        };
    }

    private static string? ReadLpstr(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return null;

        int length = ReadInt32(data, offset);
        if (length <= 0 || length > 65536 || offset + 4 + length > data.Length) return null;

        // Length includes null terminator
        int strLen = length - 1;
        if (strLen < 0) strLen = 0;

        return Encoding.Default.GetString(data, offset + 4, strLen).TrimEnd('\0');
    }

    private static string? ReadLpwstr(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return null;

        int charCount = ReadInt32(data, offset);
        if (charCount <= 0 || charCount > 65536) return null;

        int byteCount = charCount * 2;
        if (offset + 4 + byteCount > data.Length) return null;

        return Encoding.Unicode.GetString(data, offset + 4, byteCount).TrimEnd('\0');
    }

    private static void ParseNameDictionary(byte[] data, int offset, Dictionary<int, string> nameDict)
    {
        if (offset + 4 > data.Length) return;

        int count = ReadInt32(data, offset);
        if (count <= 0 || count > 1000) return;

        int pos = offset + 4;
        for (int i = 0; i < count; i++)
        {
            if (pos + 8 > data.Length) break;

            int pid = ReadInt32(data, pos);
            int nameLen = ReadInt32(data, pos + 4);
            pos += 8;

            if (nameLen <= 0 || nameLen > 65536 || pos + nameLen > data.Length) break;

            string name = Encoding.Default.GetString(data, pos, nameLen).TrimEnd('\0');
            nameDict[pid] = name;

            // Align to 4-byte boundary
            pos += nameLen;
            pos = (pos + 3) & ~3;
        }
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return 0;
        return BitConverter.ToInt32(data, offset);
    }
}
