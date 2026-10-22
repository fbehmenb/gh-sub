namespace UmbrellaManager.Parsing;

using System.Text.Json;

using UmbrellaManager.Model;

public static class FileTree
{
    public static ParsedTree Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var truncated = root.TryGetProperty("truncated", out var flag) && flag.ValueKind == JsonValueKind.True;
        if (!root.TryGetProperty("tree", out var tree) || tree.ValueKind != JsonValueKind.Array)
        {
            return new ParsedTree([], truncated);
        }

        var entries = new List<(string Path, bool IsFolder)>();
        foreach (var element in tree.EnumerateArray())
        {
            var path = element.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            var type = element.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            entries.Add((path, type == "tree"));
        }

        return new ParsedTree(Build(entries), truncated);
    }

    public static IReadOnlyList<FileNode> Build(IEnumerable<(string Path, bool IsFolder)> entries)
    {
        var roots = new List<FileNode>();
        foreach (var entry in entries.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            var parts = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var level = roots;
            var walked = "";
            for (var i = 0; i < parts.Length; i++)
            {
                walked = walked.Length == 0 ? parts[i] : walked + "/" + parts[i];
                var leaf = i == parts.Length - 1;
                var existing = level.FirstOrDefault(node => node.Name == parts[i]);
                if (existing is null)
                {
                    existing = new FileNode
                    {
                        Name = parts[i],
                        Path = walked,
                        IsFolder = !leaf || entry.IsFolder,
                    };
                    level.Add(existing);
                }
                else if (!leaf || entry.IsFolder)
                {
                    existing.IsFolder = true;
                }

                level = existing.Children;
            }
        }

        Sort(roots);
        return roots;
    }

    public static HashSet<string> DeeperFolders(IReadOnlyList<FileNode> roots)
    {
        var collapsed = new HashSet<string>(StringComparer.Ordinal);
        void Walk(FileNode node, int depth)
        {
            if (node.IsFolder && depth > 0)
            {
                collapsed.Add(node.Path);
            }

            foreach (var child in node.Children)
            {
                Walk(child, depth + 1);
            }
        }

        foreach (var root in roots)
        {
            Walk(root, 0);
        }

        return collapsed;
    }

    public static IReadOnlyList<FileRow> Flatten(IReadOnlyList<FileNode> roots, IReadOnlySet<string> collapsed)
    {
        var rows = new List<FileRow>();
        void Walk(FileNode node, int depth)
        {
            var expanded = !node.IsFolder || !collapsed.Contains(node.Path);
            rows.Add(new FileRow(node.Path, node.Name, depth, node.IsFolder, expanded));
            if (!node.IsFolder || !expanded)
            {
                return;
            }

            foreach (var child in node.Children)
            {
                Walk(child, depth + 1);
            }
        }

        foreach (var root in roots)
        {
            Walk(root, 0);
        }

        return rows;
    }

    private static void Sort(List<FileNode> nodes)
    {
        nodes.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        foreach (var node in nodes)
        {
            Sort(node.Children);
        }
    }
}
