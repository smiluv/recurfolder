using RecurFolder.Models;

namespace RecurFolder.Services;

public readonly record struct ScanProgress(int Found, int Skipped);

public sealed class ScanResult
{
    public required List<FileEntry> Entries { get; init; }

    public required int Skipped { get; init; }

    public required int LinksNotFollowed { get; init; }

    public required bool Cancelled { get; init; }

    public required int FileCount { get; init; }

    public required int FolderCount { get; init; }
}
