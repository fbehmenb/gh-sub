namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public static class PinBranch
{
    public static string? FromTips(string? configured, string? pinSha, IReadOnlyList<BranchTip> tips, string? prefer = null)
    {
        if (string.IsNullOrEmpty(pinSha))
        {
            return configured;
        }

        var heads = new List<string>();
        foreach (var tip in tips)
        {
            if (tip.TipSha.Length == 0 || !Sha.Same(tip.TipSha, pinSha) || heads.Contains(tip.Name, StringComparer.Ordinal))
            {
                continue;
            }

            heads.Add(tip.Name);
        }

        // Kein Treffer bleibt leer. branch= aus .gitmodules ist dann nicht der Branch des Pins.
        return Choose(heads, prefer);
    }

    public static string? FromGraph(IReadOnlyList<string> lanes, IReadOnlyList<GraphRow> rows, string? pinSha, string? prefer)
    {
        foreach (var row in rows)
        {
            if (!Sha.Same(row.Sha, pinSha))
            {
                continue;
            }

            var heads = new List<string>();
            for (var index = 0; index < row.Dots.Length && index < lanes.Count; index++)
            {
                if (row.Dots[index] && !heads.Contains(lanes[index], StringComparer.Ordinal))
                {
                    heads.Add(lanes[index]);
                }
            }

            return Choose(heads, prefer);
        }

        return null;
    }

    private static string? Choose(IReadOnlyList<string> heads, string? prefer)
    {
        if (heads.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(prefer))
        {
            foreach (var name in heads)
            {
                if (name.Equals(prefer, StringComparison.Ordinal)
                    || name.EndsWith("/" + prefer, StringComparison.Ordinal))
                {
                    return name;
                }
            }
        }

        return string.Join(", ", heads);
    }
}
