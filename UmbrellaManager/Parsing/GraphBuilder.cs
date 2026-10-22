namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public static class GraphBuilder
{
    public const int MaxExtraLanes = 8;

    public static IReadOnlyList<BranchTip> SelectExtraLanes(
        BranchTip standard,
        bool pinOnStandard,
        IReadOnlyList<BranchTip> branches,
        IReadOnlySet<string> standardShas)
    {
        var selected = branches
            .Where(branch => !branch.Name.Equals(standard.Name, StringComparison.Ordinal))
            .Where(branch => Include(branch, standard, pinOnStandard, standardShas))
            .OrderByDescending(branch => branch.PinIsOnBranch)
            .ThenByDescending(branch => branch.TipDate ?? DateTimeOffset.MinValue)
            .ThenBy(branch => branch.Name, StringComparer.Ordinal)
            .Take(MaxExtraLanes)
            .ToList();
        return selected;
    }

    public static IReadOnlyList<GraphRow> Build(
        string standardBranch,
        IReadOnlyList<CommitInfo> standardCommits,
        IReadOnlyList<(string Name, IReadOnlyList<CommitInfo> Commits)> extraLanes,
        string? pinSha,
        string? pendingSha)
    {
        var lanes = new List<(string Name, IReadOnlyList<CommitInfo> Commits)>
        {
            (standardBranch, standardCommits),
        };
        lanes.AddRange(extraLanes);

        var bySha = new Dictionary<string, (CommitInfo Commit, bool[] Dots)>(StringComparer.OrdinalIgnoreCase);
        for (var lane = 0; lane < lanes.Count; lane++)
        {
            foreach (var commit in lanes[lane].Commits)
            {
                if (!bySha.TryGetValue(commit.Sha, out var existing))
                {
                    existing = (commit, new bool[lanes.Count]);
                    bySha[commit.Sha] = existing;
                }

                existing.Dots[lane] = true;
                if (commit.Date > existing.Commit.Date)
                {
                    bySha[commit.Sha] = (commit, existing.Dots);
                }
            }
        }

        return bySha.Values
            .OrderByDescending(item => item.Commit.Date)
            .ThenBy(item => item.Commit.Sha, StringComparer.Ordinal)
            .Select(item => new GraphRow(
                item.Commit.Sha,
                item.Commit.Subject,
                item.Commit.Date,
                item.Dots,
                Tone(item.Commit.Sha, pinSha, pendingSha)))
            .ToList();
    }

    public static CommitTone Tone(string sha, string? pinSha, string? pendingSha)
    {
        if (pendingSha is not null && Sha.Same(sha, pendingSha) && !Sha.Same(sha, pinSha))
        {
            return CommitTone.LightGreen;
        }

        if (Sha.Same(sha, pinSha))
        {
            return CommitTone.DarkGreen;
        }

        return CommitTone.Gray;
    }

    private static bool Include(BranchTip branch, BranchTip standard, bool pinOnStandard, IReadOnlySet<string> standardShas)
    {
        if (!pinOnStandard && branch.PinIsOnBranch)
        {
            return true;
        }

        var newer = branch.TipDate is not null
            && standard.TipDate is not null
            && branch.TipDate > standard.TipDate
            && !standardShas.Contains(branch.TipSha);
        return newer;
    }
}
