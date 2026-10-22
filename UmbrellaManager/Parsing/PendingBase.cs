namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public static class PendingBase
{
    public static IReadOnlyList<string> Moved(
        IReadOnlyDictionary<string, string> pending,
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyDictionary<string, string?> after)
    {
        var names = new List<string>();
        foreach (var path in pending.Keys.OrderBy(path => path, StringComparer.Ordinal))
        {
            before.TryGetValue(path, out var oldSha);
            after.TryGetValue(path, out var newSha);
            if (!Sha.Same(oldSha, newSha))
            {
                var slash = path.LastIndexOf('/');
                names.Add(slash < 0 ? path : path[(slash + 1)..]);
            }
        }

        return names;
    }
}
