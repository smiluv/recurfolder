namespace RecurFolder.Models;

public sealed class FileEntry
{
    public required string FolderName { get; init; }

    public required string FileName { get; init; }

    public required string FilePath { get; init; }

    public required string Kind { get; init; }
}
