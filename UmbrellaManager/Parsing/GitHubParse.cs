using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public sealed record GitHubRepo(string Owner, string Name)
{
    public static GitHubRepo? FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var text = url.Trim();
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        const string https = "https://github.com/";
        const string ssh = "git@github.com:";
        const string sshUrl = "ssh://git@github.com/";
        string? rest = null;
        if (text.StartsWith(https, StringComparison.OrdinalIgnoreCase))
        {
            rest = text[https.Length..];
        }
        else if (text.StartsWith(ssh, StringComparison.OrdinalIgnoreCase))
        {
            rest = text[ssh.Length..];
        }
        else if (text.StartsWith(sshUrl, StringComparison.OrdinalIgnoreCase))
        {
            rest = text[sshUrl.Length..];
        }

        if (rest is null)
        {
            return null;
        }

        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1 || rest[(slash + 1)..].Contains('/'))
        {
            return null;
        }

        return new GitHubRepo(rest[..slash], rest[(slash + 1)..].TrimEnd('/'));
    }

    public static string? Resolve(string? baseUrl, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var text = url.Trim();
        if (IsAbsolute(text) || string.IsNullOrWhiteSpace(baseUrl))
        {
            return text;
        }

        var remote = baseUrl.Trim().TrimEnd('/');
        var useColon = false;
        while (text.StartsWith("../", StringComparison.Ordinal))
        {
            text = text[3..];
            var slash = remote.LastIndexOf('/');
            var colon = remote.LastIndexOf(':');
            if (slash >= 0 && slash > colon)
            {
                remote = remote[..slash];
            }
            else if (colon >= 0)
            {
                remote = remote[..colon];
                useColon = true;
            }
            else
            {
                break;
            }
        }

        if (text.StartsWith("./", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        if (remote.EndsWith(':'))
        {
            return remote + text;
        }

        return remote + (useColon ? ":" : "/") + text;
    }

    private static bool IsAbsolute(string url)
    {
        return url.Contains("://", StringComparison.Ordinal)
            || (url.Contains(':')
                && !url.StartsWith("./", StringComparison.Ordinal)
                && !url.StartsWith("../", StringComparison.Ordinal));
    }
}

public static partial class GitHubParse
{
    [GeneratedRegex("""['"]version['"]\s*:\s*['"](\d+(?:\.\d+)+)['"]""", RegexOptions.CultureInvariant)]
    private static partial Regex ManifestVersionPattern();

    public static string? ParseManifestVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("content", out var contentElement)
                || contentElement.GetString() is not { Length: > 0 } content)
            {
                return null;
            }

            var encoding = document.RootElement.TryGetProperty("encoding", out var encodingElement)
                ? encodingElement.GetString()
                : "base64";
            if (!string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var bytes = Convert.FromBase64String(string.Concat(content.Where(character => !char.IsWhiteSpace(character))));
            return ManifestVersion(Encoding.UTF8.GetString(bytes));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string? ManifestVersion(string manifest)
    {
        var match = ManifestVersionPattern().Match(manifest);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static IReadOnlyList<CommitInfo> ParseCommits(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            var one = ParseCommit(document.RootElement);
            return one is null ? [] : [one.Value];
        }

        var commits = new List<CommitInfo>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var commit = ParseCommit(element);
            if (commit is not null)
            {
                commits.Add(commit.Value);
            }
        }

        return commits;
    }

    public static CommitInfo? ParseCommit(JsonElement element)
    {
        if (!element.TryGetProperty("sha", out var shaElement))
        {
            return null;
        }

        var sha = shaElement.GetString();
        if (string.IsNullOrEmpty(sha) || !element.TryGetProperty("commit", out var commit))
        {
            return null;
        }

        var message = commit.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "";
        var subject = message.Split(['\r', '\n'], 2)[0].Trim();
        var date = DateTimeOffset.UnixEpoch;
        if (commit.TryGetProperty("committer", out var committer)
            && committer.TryGetProperty("date", out var dateElement)
            && dateElement.GetString() is string text
            && DateTimeOffset.TryParse(text, out var parsed))
        {
            date = parsed;
        }

        var parents = new List<string>();
        if (element.TryGetProperty("parents", out var parentArray) && parentArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var parent in parentArray.EnumerateArray())
            {
                var parentSha = parent.TryGetProperty("sha", out var parentElement) ? parentElement.GetString() : null;
                if (!string.IsNullOrEmpty(parentSha))
                {
                    parents.Add(parentSha);
                }
            }
        }

        return new CommitInfo(sha, subject, date, parents);
    }

    public static IReadOnlyList<BranchTip> ParseBranches(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tips = new List<BranchTip>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var name = element.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrEmpty(name) || !element.TryGetProperty("commit", out var commit))
            {
                continue;
            }

            var sha = commit.TryGetProperty("sha", out var shaElement) ? shaElement.GetString() ?? "" : "";
            DateTimeOffset? date = null;
            if (commit.TryGetProperty("commit", out var inner)
                && inner.TryGetProperty("committer", out var committer)
                && committer.TryGetProperty("date", out var dateElement)
                && DateTimeOffset.TryParse(dateElement.GetString(), out var parsed))
            {
                date = parsed;
            }

            tips.Add(new BranchTip(name, sha, date, PinIsOnBranch: false));
        }

        return tips;
    }

    public static string? ParseDefaultBranch(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("default_branch", out var value) ? value.GetString() : null;
    }

    public static string? ParseCompareStatus(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("status", out var value) ? value.GetString() : null;
    }
}

public static class CompareStatus
{
    public static bool PinIsOnBranch(string? status)
    {
        // compare/<pin>...<branch>: ahead bedeutet, der Branch enthält den Pin.
        return status is "ahead" or "identical";
    }
}
