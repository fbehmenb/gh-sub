using System.Text.RegularExpressions;

namespace UmbrellaManager.Parsing;

public static partial class BranchOrder
{
    public static IReadOnlyList<string> Apply(IEnumerable<string> branches, string hidePattern)
    {
        var hide = Glob(hidePattern);
        return branches
            .Where(branch => branch.Length > 0 && !hide.IsMatch(branch))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Rank)
            .ThenBy(branch => branch, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> ParseRemoteHeads(string text)
    {
        var names = new List<string>();
        foreach (var line in Lines(text))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            const string prefix = "refs/heads/";
            var reference = line[(tab + 1)..];
            if (reference.StartsWith(prefix, StringComparison.Ordinal) && !reference.EndsWith("^{}", StringComparison.Ordinal))
            {
                names.Add(reference[prefix.Length..]);
            }
        }

        return names;
    }

    public static IReadOnlyList<string> ParseLocalRemoteBranches(string text)
    {
        var names = new List<string>();
        foreach (var line in Lines(text))
        {
            if (line.Contains("HEAD", StringComparison.Ordinal) || !line.StartsWith("origin/", StringComparison.Ordinal))
            {
                continue;
            }

            names.Add(line["origin/".Length..]);
        }

        return names;
    }

    public static bool IsProduction(string? branch)
    {
        return branch?.StartsWith("production/", StringComparison.Ordinal) == true;
    }

    private static int Rank(string branch)
    {
        if (branch.StartsWith("dev/", StringComparison.Ordinal))
        {
            return 0;
        }

        if (branch.StartsWith("staging/", StringComparison.Ordinal))
        {
            return 1;
        }

        if (branch.StartsWith("production/", StringComparison.Ordinal))
        {
            return 2;
        }

        return 3;
    }

    private static Regex Glob(string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return new Regex(regex, RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> Lines(string text)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
