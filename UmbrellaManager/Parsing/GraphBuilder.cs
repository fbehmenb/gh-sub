namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public static class GraphBuilder
{
    public const int MaxExtraLanes = 8;

    public static IReadOnlyList<BranchTip> SelectExtraLanes(
        BranchTip standard,
        bool pinOnStandard,
        IReadOnlyList<BranchTip> branches,
        IReadOnlySet<string> standardShas,
        string? keepName = null)
    {
        var selected = branches
            .Where(branch => !branch.Name.Equals(standard.Name, StringComparison.Ordinal))
            .Where(branch => Keep(branch, keepName) || Include(branch, standard, pinOnStandard, standardShas))
            .OrderByDescending(branch => Keep(branch, keepName))
            .ThenByDescending(branch => branch.PinIsOnBranch)
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

        var names = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var bySha = new Dictionary<string, (CommitInfo Commit, bool[] Dots)>(StringComparer.OrdinalIgnoreCase);
        for (var lane = 0; lane < lanes.Count; lane++)
        {
            CommitInfo? tip = null;
            foreach (var commit in lanes[lane].Commits)
            {
                if (tip is null || commit.Date > tip.Value.Date)
                {
                    tip = commit;
                }

                if (!bySha.TryGetValue(commit.Sha, out var existing))
                {
                    existing = (commit, new bool[lanes.Count]);
                    bySha[commit.Sha] = existing;
                }

                existing.Dots[lane] = true;
                if (commit.Date > existing.Commit.Date)
                {
                    var chosen = commit.Parents is not { Count: > 0 } && existing.Commit.Parents is { Count: > 0 }
                        ? commit with { Parents = existing.Commit.Parents }
                        : commit;
                    bySha[commit.Sha] = (chosen, existing.Dots);
                }
                else if (existing.Commit.Parents is not { Count: > 0 } && commit.Parents is { Count: > 0 })
                {
                    bySha[commit.Sha] = (existing.Commit with { Parents = commit.Parents }, existing.Dots);
                }
            }

            if (tip is not null)
            {
                if (!names.TryGetValue(tip.Value.Sha, out var list))
                {
                    list = [];
                    names[tip.Value.Sha] = list;
                }

                list.Add(lanes[lane].Name);
            }
        }

        var rows = bySha.Values
            .OrderByDescending(item => item.Commit.Date)
            .ThenBy(item => item.Commit.Sha, StringComparer.Ordinal)
            .Select(item => new GraphRow(
                item.Commit.Sha,
                item.Commit.Subject,
                item.Commit.Date,
                item.Dots,
                Tone(item.Commit.Sha, pinSha, pendingSha),
                Parents: item.Commit.Parents,
                Names: names.TryGetValue(item.Commit.Sha, out var found) ? found : null))
            .ToList();
        return CommitGraph.Apply(rows);
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

    public static IReadOnlyList<GraphRow> Include(
        IReadOnlyList<GraphRow> rows,
        IReadOnlyList<CommitInfo> extra,
        string? pinSha,
        string? pendingSha)
    {
        var list = rows.ToList();
        var lanes = list.Count == 0 ? 0 : list[0].Dots.Length;
        foreach (var commit in extra)
        {
            if (list.Any(row => Sha.Same(row.Sha, commit.Sha)))
            {
                continue;
            }

            list.Add(new GraphRow(
                commit.Sha,
                commit.Subject,
                commit.Date,
                new bool[lanes],
                Tone(commit.Sha, pinSha, pendingSha),
                Parents: commit.Parents));
        }

        return CommitGraph.Apply(list
            .OrderByDescending(row => row.Date)
            .ThenBy(row => row.Sha, StringComparer.Ordinal)
            .ToList());
    }

    public static IReadOnlyList<GraphRow> MarkPromotion(
        IReadOnlyList<GraphRow> rows,
        string? productionSha,
        string? stagingSha,
        IReadOnlySet<string> promotable)
    {
        return rows.Select(row =>
        {
            var production = Sha.Same(row.Sha, productionSha);
            var staging = Sha.Same(row.Sha, stagingSha);
            var mark = production
                ? staging ? PinMark.Both : PinMark.Production
                : staging ? PinMark.Staging : PinMark.None;
            var canPromote = !production && promotable.Any(sha => Sha.Same(sha, row.Sha));
            return row with { Mark = mark, Promotable = canPromote };
        }).ToList();
    }

    public static IReadOnlyList<GraphRow> ProductionTones(IReadOnlyList<GraphRow> rows)
    {
        return rows.Select(row => row.Mark switch
        {
            PinMark.Both => row with { Tone = CommitTone.DarkGreen },
            PinMark.Staging => row with { Tone = CommitTone.LightGreen },
            _ => row with { Tone = CommitTone.Gray },
        }).ToList();
    }

    public static string Stamp(CommitTone tone)
    {
        return tone switch
        {
            CommitTone.DarkGreen => "pin ",
            CommitTone.LightGreen => "neu ",
            _ => "    ",
        };
    }

    public static string Badge(PinMark mark)
    {
        return mark switch
        {
            PinMark.Both => "production = staging",
            PinMark.Production => "production",
            PinMark.Staging => "staging",
            _ => "",
        };
    }

    private static bool Keep(BranchTip branch, string? keepName)
    {
        return keepName is not null && branch.Name.Equals(keepName, StringComparison.Ordinal);
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
