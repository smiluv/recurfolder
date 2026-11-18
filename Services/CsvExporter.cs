using System.IO;
using System.Text;
using RecurFolder.Models;

namespace RecurFolder.Services;

public static class CsvExporter
{
    public static void Export(string filePath, IReadOnlyList<FileEntry> rows)
    {
        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.NewLine = "\r\n";
        writer.WriteLine("Folder name,Filename,File path,Type");

        foreach (var row in rows)
        {
            writer.Write(Escape(row.FolderName));
            writer.Write(',');
            writer.Write(Escape(row.FileName));
            writer.Write(',');
            writer.Write(Escape(row.FilePath));
            writer.Write(',');
            writer.Write(Escape(row.Kind));
            writer.WriteLine();
        }
    }

    private static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t')
            value = "'" + value;

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }
}
