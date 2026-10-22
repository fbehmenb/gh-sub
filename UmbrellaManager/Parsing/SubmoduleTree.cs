namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public static class SubmoduleTree
{
    public static IReadOnlyList<SubmoduleEntry> Merge(string gitmodules, string lsTree)
    {
        var map = new Dictionary<string, SubmoduleEntry>(StringComparer.Ordinal);
        foreach (var entry in ParseModules(gitmodules))
        {
            map[entry.Path] = entry;
        }

        foreach (var link in ParseLinks(lsTree))
        {
            if (map.TryGetValue(link.Path, out var existing))
            {
                map[link.Path] = existing with { Sha = link.Sha };
            }
            else
            {
                map[link.Path] = link;
            }
        }

        return map.Values.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<TreeNode> Build(IEnumerable<SubmoduleEntry> modules)
    {
        var roots = new List<TreeNode>();
        foreach (var module in modules.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            var parts = module.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
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
                    existing = new TreeNode { Name = parts[i], Path = walked };
                    level.Add(existing);
                }

                if (leaf)
                {
                    existing.IsModule = true;
                    existing.Sha = module.Sha;
                    existing.Url = module.Url;
                    existing.Branch = module.Branch;
                }

                level = existing.Children;
            }
        }

        Sort(roots);
        return roots;
    }

    public static IReadOnlyList<VisibleRow> Flatten(IReadOnlyList<TreeNode> roots, IReadOnlySet<string> collapsed)
    {
        var rows = new List<VisibleRow>();
        void Walk(TreeNode node, int depth)
        {
            var isFolder = node.Children.Count > 0;
            var expanded = !collapsed.Contains(node.Path);
            rows.Add(new VisibleRow(
                node.Path,
                node.Name,
                depth,
                isFolder,
                expanded,
                node.IsModule ? node.Sha : null,
                node.IsModule ? node.Branch : null));
            if (!isFolder || !expanded)
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

    public static SubmoduleEntry? Find(IReadOnlyList<TreeNode> roots, string path)
    {
        foreach (var node in WalkAll(roots))
        {
            if (node.IsModule && node.Path == path)
            {
                return new SubmoduleEntry(node.Path, node.Sha, node.Url, node.Branch);
            }
        }

        return null;
    }

    private static IEnumerable<TreeNode> WalkAll(IEnumerable<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in WalkAll(node.Children))
            {
                yield return child;
            }
        }
    }

    private static void Sort(List<TreeNode> nodes)
    {
        nodes.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        foreach (var node in nodes)
        {
            Sort(node.Children);
        }
    }

    private static IEnumerable<SubmoduleEntry> ParseModules(string text)
    {
        string? section = null;
        string? path = null;
        string? url = null;
        string? branch = null;
        foreach (var raw in text.Split(['\r', '\n']))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith("[submodule ", StringComparison.Ordinal) && line.EndsWith(']'))
            {
                if (section is not null)
                {
                    yield return Make(section, path, url, branch);
                }

                section = line["[submodule ".Length..^1].Trim().Trim('"');
                path = null;
                url = null;
                branch = null;
                continue;
            }

            var split = line.IndexOf('=');
            if (section is null || split < 0)
            {
                continue;
            }

            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (key == "path")
            {
                path = value;
            }
            else if (key == "url")
            {
                url = value;
            }
            else if (key == "branch")
            {
                branch = value;
            }
        }

        if (section is not null)
        {
            yield return Make(section, path, url, branch);
        }
    }

    private static SubmoduleEntry Make(string section, string? path, string? url, string? branch)
    {
        return new SubmoduleEntry(string.IsNullOrEmpty(path) ? section : path, null, url, branch);
    }

    private static IEnumerable<SubmoduleEntry> ParseLinks(string text)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            var meta = line[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (meta.Length < 3 || meta[0] != "160000")
            {
                continue;
            }

            yield return new SubmoduleEntry(line[(tab + 1)..], meta[2], null, null);
        }
    }
}
