using System.Text.RegularExpressions;

namespace UmbrellaManager.GitHub;

public static partial class GitHubText
{
    public const string Offline = "offline/zwischengespeichert";

    public const string SsoDenied =
        "GitHub meldet 404. Das Repo ist nicht sichtbar. Bei privaten Repos fehlt oft die SSO-Freigabe der Organisation.";

    public static bool IsOffline(string message) => message == Offline;

    public static string Describe(string stdout, string stderr)
    {
        var text = stdout + "\n" + stderr;
        if (text.Contains("404", StringComparison.Ordinal))
        {
            return SsoDenied;
        }

        if (text.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || IsNetwork(text))
        {
            return Offline;
        }

        var line = Redact(FirstLine(stderr.Length > 0 ? stderr : stdout));
        return line.Length == 0 ? "GitHub-Abfrage fehlgeschlagen." : "GitHub-Abfrage fehlgeschlagen. " + line;
    }

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var redacted = TokenPattern().Replace(text, "[token]");
        return redacted.Length > 180 ? redacted[..180] : redacted;
    }

    private static bool IsNetwork(string text)
    {
        return text.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Could not resolve", StringComparison.OrdinalIgnoreCase)
            || text.Contains("network", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TLS", StringComparison.Ordinal);
    }

    private static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text.Trim() : text[..end].Trim();
    }

    [GeneratedRegex(@"ghp_[A-Za-z0-9]+|github_pat_[A-Za-z0-9_]+|Bearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();
}
