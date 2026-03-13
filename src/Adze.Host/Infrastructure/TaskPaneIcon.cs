using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Adze.Host.Infrastructure;

internal static class TaskPaneIcon
{
    public static string Ensure()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Adze");
        string path = Path.Combine(directory, "taskpane.ico");

        if (File.Exists(path))
        {
            return path;
        }

        Directory.CreateDirectory(directory);

        using FileStream stream = File.Create(path);
        SystemIcons.Application.Save(stream);
        return path;
    }
}
