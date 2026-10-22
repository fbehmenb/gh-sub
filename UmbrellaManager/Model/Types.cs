namespace UmbrellaManager.Model;

public sealed record SubmoduleEntry(string Path, string? Sha, string? Url, string? Branch);

public sealed class TreeNode
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public bool IsModule { get; set; }

    public string? Sha { get; set; }

    public string? Url { get; set; }

    public string? Branch { get; set; }

    public List<TreeNode> Children { get; } = [];
}

public readonly record struct VisibleRow(string Path, string Name, int Depth, bool IsFolder, bool Expanded, string? Sha, string? Branch);

public readonly record struct CommitInfo(string Sha, string Subject, DateTimeOffset Date, string? TreeSha = null, IReadOnlyList<string>? Parents = null);

public readonly record struct BranchTip(string Name, string TipSha, DateTimeOffset? TipDate, bool PinIsOnBranch);

public enum CommitTone
{
    Gray,
    DarkGreen,
    LightGreen,
}

public enum PinMark
{
    None,
    Production,
    Staging,
    Both,
}

public readonly record struct GraphCell(char Symbol, int Lane);

public sealed record GraphRow(
    string Sha,
    string Subject,
    DateTimeOffset Date,
    bool[] Dots,
    CommitTone Tone,
    string? TreeSha = null,
    PinMark Mark = PinMark.None,
    bool Promotable = false,
    IReadOnlyList<string>? Parents = null,
    IReadOnlyList<string>? Names = null,
    IReadOnlyList<GraphCell>? Cells = null);

public sealed class FileNode
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public bool IsFolder { get; set; }

    public List<FileNode> Children { get; } = [];
}

public readonly record struct FileRow(string Path, string Name, int Depth, bool IsFolder, bool Expanded);

public readonly record struct ParsedTree(IReadOnlyList<FileNode> Roots, bool Truncated);

public sealed record FileSnapshot(IReadOnlyList<FileNode> Roots, string Message);

public sealed record GraphSnapshot(IReadOnlyList<string> Lanes, IReadOnlyList<GraphRow> Rows, string Message);

public static class Sha
{
    public static string Short(string? sha)
    {
        if (string.IsNullOrEmpty(sha))
        {
            return "?";
        }

        return sha.Length <= 7 ? sha : sha[..7];
    }

    public static bool Same(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        var length = Math.Min(left.Length, right.Length);
        if (length < 7)
        {
            return left.Equals(right, StringComparison.OrdinalIgnoreCase);
        }

        return left.AsSpan(0, length).Equals(right.AsSpan(0, length), StringComparison.OrdinalIgnoreCase);
    }
}
