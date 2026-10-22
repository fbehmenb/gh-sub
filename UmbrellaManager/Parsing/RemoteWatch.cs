namespace UmbrellaManager.Parsing;

public static class RemoteWatch
{
    public static IReadOnlyList<string> Stale(
        IReadOnlyDictionary<string, string> loaded,
        IReadOnlyDictionary<string, string> remote)
    {
        var stale = new List<string>();
        foreach (var pair in loaded)
        {
            if (remote.TryGetValue(pair.Key, out var sha)
                && !pair.Value.Equals(sha, StringComparison.OrdinalIgnoreCase))
            {
                stale.Add(pair.Key);
            }
        }

        return stale;
    }

    public static IReadOnlyList<string> Unlisted(IEnumerable<string> known, IEnumerable<string> remoteNames, string hidePattern)
    {
        var have = new HashSet<string>(known, StringComparer.Ordinal);
        return BranchOrder.Apply(remoteNames.Where(name => !have.Contains(name)), hidePattern);
    }
}
