using System.IO;
using RecurFolder.Models;

namespace RecurFolder.Services;

public static class FolderScanner
{
    public static ScanResult Scan(string root, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var entries = new List<FileEntry>(1024);
        var skipped = 0;
        var linksNotFollowed = 0;
        var fileCount = 0;
        var folderCount = 0;
        var cancelled = false;
        var lastReport = Environment.TickCount64;
        var stack = new Stack<IEnumerator<string>>();

        void Report(bool force)
        {
            var now = Environment.TickCount64;
            if (!force && now - lastReport < 150)
                return;

            lastReport = now;
            progress?.Report(new ScanProgress(entries.Count, skipped));
        }

        try
        {
            if (!TryEnumerate(root, out var rootEnumerator))
            {
                skipped++;
            }
            else
            {
                stack.Push(rootEnumerator);
            }

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var enumerator = stack.Peek();

                string current;
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        enumerator.Dispose();
                        stack.Pop();
                        continue;
                    }

                    current = enumerator.Current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    skipped++;
                    enumerator.Dispose();
                    stack.Pop();
                    continue;
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(current);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    skipped++;
                    continue;
                }

                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var isLink = (attributes & FileAttributes.ReparsePoint) != 0;

                if (isDirectory)
                {
                    folderCount++;
                    if (isLink)
                        linksNotFollowed++;
                }
                else
                {
                    fileCount++;
                }

                entries.Add(new FileEntry
                {
                    FolderName = ParentFolderName(current),
                    FileName = Path.GetFileName(current),
                    FilePath = current,
                    Kind = isDirectory ? "Folder" : "File"
                });

                if (isDirectory && !isLink)
                {
                    if (TryEnumerate(current, out var child))
                        stack.Push(child);
                    else
                        skipped++;
                }

                Report(false);
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            while (stack.Count > 0)
                stack.Pop().Dispose();
        }

        Report(true);
        return new ScanResult
        {
            Entries = entries,
            Skipped = skipped,
            LinksNotFollowed = linksNotFollowed,
            Cancelled = cancelled,
            FileCount = fileCount,
            FolderCount = folderCount
        };
    }

    private static bool TryEnumerate(string directory, out IEnumerator<string> enumerator)
    {
        try
        {
            enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            enumerator = Enumerable.Empty<string>().GetEnumerator();
            return false;
        }
    }

    private static string ParentFolderName(string fullPath)
    {
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent))
            return string.Empty;

        var trimmed = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? parent : name;
    }
}
