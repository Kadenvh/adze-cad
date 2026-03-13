using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Adze.Trace.Serialization;

internal static class JsonFileStore
{
    public static void Write(string path, IDictionary<string, object?> payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var serializer = CreateSerializer();
        File.WriteAllText(path, serializer.Serialize(payload));
    }

    public static bool TryRead(string path, out Dictionary<string, object> payload)
    {
        payload = new Dictionary<string, object>();
        if (!File.Exists(path))
        {
            return false;
        }

        string content = File.ReadAllText(path);
        object? value = CreateSerializer().DeserializeObject(content);
        if (value is Dictionary<string, object> dictionary)
        {
            payload = dictionary;
            return true;
        }

        return false;
    }

    private static JavaScriptSerializer CreateSerializer()
    {
        return new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue
        };
    }
}
