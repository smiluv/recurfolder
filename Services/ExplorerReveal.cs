using System.Diagnostics;
using System.IO;

namespace RecurFolder.Services;

public static class ExplorerReveal
{
    public static void Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            throw new FileNotFoundException("That item is no longer in the folder.");

        var normalized = path;
        var isDriveRoot = normalized.Length == 3
            && normalized[1] == ':'
            && (normalized[2] == '\\' || normalized[2] == '/');
        if (!isDriveRoot)
            normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "/select,\"" + normalized + "\"",
            UseShellExecute = true
        });
    }
}
