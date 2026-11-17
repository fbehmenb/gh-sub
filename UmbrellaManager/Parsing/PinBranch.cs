namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public static class PinBranch
{
    public static string? FromTips(string? configured, string? pinSha, IReadOnlyList<BranchTip> tips)
    {
        if (string.IsNullOrEmpty(pinSha))
        {
            return configured;
        }

        var heads = new List<string>();
        foreach (var tip in tips)
        {
            if (!Sha.Same(tip.TipSha, pinSha) || heads.Contains(tip.Name, StringComparer.Ordinal))
            {
                continue;
            }

            heads.Add(tip.Name);
        }

        // branch= aus .gitmodules kann hinter dem Pin zurückliegen.
        // Die Spalte nennt dann die Branch-Spitze, die der Pin wirklich ist.
        return JoinHeads(configured, heads);
    }

    public static string? FromGraph(IReadOnlyList<GraphRow> rows, string? pinSha, string? configured)
    {
        foreach (var row in rows)
        {
            if (!Sha.Same(row.Sha, pinSha) || row.Names is not { Count: > 0 } names)
            {
                continue;
            }

            return JoinHeads(configured, names);
        }

        return null;
    }

    private static string? JoinHeads(string? configured, IReadOnlyList<string> heads)
    {
        if (heads.Count == 0)
        {
            return configured;
        }

        if (string.IsNullOrEmpty(configured) || !heads.Contains(configured, StringComparer.Ordinal))
        {
            return string.Join(", ", heads);
        }

        if (heads.Count == 1)
        {
            return configured;
        }

        var ordered = new List<string> { configured };
        foreach (var name in heads)
        {
            if (!name.Equals(configured, StringComparison.Ordinal))
            {
                ordered.Add(name);
            }
        }

        return string.Join(", ", ordered);
    }
}
