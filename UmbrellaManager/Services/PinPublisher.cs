using System.Text.Json;

namespace UmbrellaManager.Services;

using UmbrellaManager.GitHub;
using UmbrellaManager.Model;
using UmbrellaManager.Parsing;
using UmbrellaManager.Process;

public sealed record PinChange(
    string Path,
    string Name,
    string? OldSha,
    string NewSha,
    string? ModuleBranch,
    string? ModuleUrl,
    string? Version = null);

public sealed record PinResult(string Path, bool Created, string Text, bool Conflict = false);

public readonly record struct PushTick(int Index, int Total, string Text, bool Finished);

public sealed class PinPublisher
{
    private readonly IExternalProcess _process;
    private readonly string _path;

    public PinPublisher(IExternalProcess process, string path)
    {
        _process = process;
        _path = path;
    }

    public static string ModuleName(string path)
    {
        var slash = path.LastIndexOf('/');
        var name = slash < 0 ? path : path[(slash + 1)..];
        var cleaned = new string(name.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-').ToArray()).Trim('-');
        return cleaned.Length == 0 ? "modul" : cleaned;
    }

    public static string Stage(string tab)
    {
        foreach (var stage in new[] { "production", "staging", "dev" })
        {
            if (tab.Equals(stage, StringComparison.Ordinal)
                || tab.StartsWith(stage + "/", StringComparison.Ordinal)
                || tab.StartsWith(stage + "-", StringComparison.Ordinal)
                || tab.EndsWith("-" + stage, StringComparison.Ordinal))
            {
                return stage;
            }
        }

        var slash = tab.IndexOf('/');
        return slash < 0 ? tab : tab[..slash];
    }

    public static string Title(string template, PinChange change, string tab)
    {
        var branch = string.IsNullOrWhiteSpace(change.ModuleBranch) ? "ohne Branch" : change.ModuleBranch;
        var version = string.IsNullOrWhiteSpace(change.Version) ? Sha.Short(change.NewSha) : change.Version;
        return template
            .Replace("<modul>", change.Name, StringComparison.Ordinal)
            .Replace("<sha7>", Sha.Short(change.NewSha), StringComparison.Ordinal)
            .Replace("<modul-branch>", branch, StringComparison.Ordinal)
            .Replace("<version>", version, StringComparison.Ordinal)
            .Replace("<stufe>", Stage(tab), StringComparison.Ordinal);
    }

    public static string HeadBranch(PinChange change, string tab)
    {
        return "chore/pin-" + change.Name + "-" + Sha.Short(change.NewSha) + "-" + tab;
    }

    public static string ChangeLine(PinChange change)
    {
        var oldSha = string.IsNullOrEmpty(change.OldSha) ? "keiner" : Sha.Short(change.OldSha);
        return change.Name + "  " + oldSha + " → " + Sha.Short(change.NewSha);
    }

    public static IReadOnlyList<string> PlanLines(
        string tab,
        IReadOnlyList<PinChange> clean,
        IReadOnlyList<string> conflicts,
        bool moved,
        string template)
    {
        var lines = new List<string> { "Pull Requests auf " + tab };
        if (clean.Count > 0)
        {
            lines.Add(clean.Count == 1 ? "1 Pull Request" : clean.Count + " Pull Requests");
        }

        if (conflicts.Count > 0)
        {
            lines.Add("Konflikt " + string.Join(", ", conflicts) + ": wird nicht gepusht");
        }

        if (moved)
        {
            lines.Add("setzt auf dem neuen Stand auf");
        }

        foreach (var change in clean.OrderBy(change => change.Path, StringComparer.Ordinal))
        {
            lines.Add("");
            lines.Add(ChangeLine(change));
            lines.Add("  " + Title(template, change, tab));
            lines.Add("  nach " + tab + " über " + HeadBranch(change, tab));
        }

        if (clean.Count > 0)
        {
            lines.Add("");
            lines.Add("j pushen    n abbrechen");
        }

        return lines;
    }

    public static string PushPreview(
        string tab,
        IReadOnlyList<PinChange> clean,
        IReadOnlyList<string> conflicts,
        bool moved,
        string template)
    {
        return string.Join("  ", PlanLines(tab, clean, conflicts, moved, template).Where(line => line.Length > 0));
    }

    public static string ProgressBar(int done, int total, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        var label = " " + Math.Max(0, done) + "/" + Math.Max(0, total);
        var barWidth = Math.Max(1, width - label.Length);
        var span = Math.Max(1, total);
        var filled = (int)Math.Round(barWidth * Math.Clamp(done / (double)span, 0, 1));
        filled = Math.Clamp(filled, 0, barWidth);
        return new string('█', filled) + new string('░', barWidth - filled) + label;
    }

    public static bool GitlinkConflicts(string? loadedGitlink, string? remoteGitlink)
    {
        return !Sha.Same(loadedGitlink, remoteGitlink);
    }

    public static bool IsConflicting(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("mergeable", out var mergeable)
                && mergeable.GetString()?.Equals("CONFLICTING", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            return root.TryGetProperty("mergeStateStatus", out var state)
                && state.GetString()?.Equals("DIRTY", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<PinResult>> PublishAsync(
        string tab,
        string loadedTip,
        IReadOnlyList<PinChange> changes,
        string titleTemplate,
        CancellationToken cancellationToken,
        Action<PushTick>? onProgress = null)
    {
        var fetch = await _process.RunAsync(
            "git",
            ["fetch", "origin", "+refs/heads/" + tab + ":refs/remotes/origin/" + tab],
            _path,
            cancellationToken).ConfigureAwait(false);
        if (fetch.ExitCode != 0)
        {
            throw new UserFacingException("Branch konnte nicht geholt werden. " + Failure(fetch));
        }

        var tip = await _process.RunAsync("git", ["rev-parse", "origin/" + tab], _path, cancellationToken).ConfigureAwait(false);
        var remoteTip = tip.StandardOutput.Trim();
        if (tip.ExitCode != 0 || remoteTip.Length != 40)
        {
            throw new UserFacingException("Branch konnte nicht gelesen werden. " + Failure(tip));
        }

        var moved = !remoteTip.Equals(loadedTip, StringComparison.OrdinalIgnoreCase);
        var results = new List<PinResult>();
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            onProgress?.Invoke(new PushTick(index, changes.Count, ChangeLine(change) + "   wird gepusht …", false));
            PinResult result;
            if (moved)
            {
                var loadedLink = await ReadGitlinkAsync(loadedTip, change.Path, cancellationToken).ConfigureAwait(false);
                var remoteLink = await ReadGitlinkAsync(remoteTip, change.Path, cancellationToken).ConfigureAwait(false);
                if (GitlinkConflicts(loadedLink, remoteLink))
                {
                    result = new PinResult(
                        change.Path,
                        false,
                        "Konflikt " + change.Name + ": der Pin auf " + tab + " wurde geändert. u holt den Stand.",
                        Conflict: true);
                }
                else
                {
                    result = await PublishOneAsync(tab, change, titleTemplate, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                result = await PublishOneAsync(tab, change, titleTemplate, cancellationToken).ConfigureAwait(false);
            }

            results.Add(result);
            onProgress?.Invoke(new PushTick(index, changes.Count, ChangeLine(change) + "   " + result.Text, true));
        }

        return results;
    }

    private async Task<string?> ReadGitlinkAsync(string commit, string path, CancellationToken cancellationToken)
    {
        var result = await GitAsync(["rev-parse", "--verify", "--end-of-options", commit + ":" + path], null, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var sha = result.StandardOutput.Trim();
        return sha.Length == 40 ? sha : null;
    }

    private async Task<PinResult> PublishOneAsync(string tab, PinChange change, string titleTemplate, CancellationToken cancellationToken)
    {
        if (change.NewSha.Length != 40 || change.NewSha.Any(character => !Uri.IsHexDigit(character)))
        {
            return new PinResult(change.Path, false, change.Name + ": der Pin ist keine Commit-SHA.");
        }

        var index = Path.Combine(Path.GetTempPath(), "umbrella-manager-" + Guid.NewGuid().ToString("N"));
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["GIT_INDEX_FILE"] = index };
        try
        {
            var read = await GitAsync(["read-tree", "origin/" + tab], environment, cancellationToken).ConfigureAwait(false);
            if (read.ExitCode != 0)
            {
                return new PinResult(change.Path, false, change.Name + ": " + Failure(read));
            }

            var update = await GitAsync(
                ["update-index", "--add", "--cacheinfo", "160000," + change.NewSha + "," + change.Path],
                environment,
                cancellationToken).ConfigureAwait(false);
            if (update.ExitCode != 0)
            {
                return new PinResult(change.Path, false, change.Name + ": " + Failure(update));
            }

            var tree = await GitAsync(["write-tree"], environment, cancellationToken).ConfigureAwait(false);
            if (tree.ExitCode != 0)
            {
                return new PinResult(change.Path, false, change.Name + ": " + Failure(tree));
            }

            var title = Title(titleTemplate, change, tab);
            var commit = await GitAsync(
                ["commit-tree", tree.StandardOutput.Trim(), "-p", "origin/" + tab, "-m", title],
                null,
                cancellationToken).ConfigureAwait(false);
            if (commit.ExitCode != 0)
            {
                return new PinResult(change.Path, false, change.Name + ": " + Failure(commit));
            }

            var head = HeadBranch(change, tab);
            var push = await GitAsync(
                ["push", "origin", commit.StandardOutput.Trim() + ":refs/heads/" + head],
                null,
                cancellationToken).ConfigureAwait(false);
            if (push.ExitCode != 0)
            {
                return new PinResult(change.Path, false, change.Name + ": " + Failure(push));
            }

            var body = Body(change, tab);
            var pull = await _process.RunAsync(
                "gh",
                ["pr", "create", "--base", tab, "--head", head, "--title", title, "--body", body],
                _path,
                cancellationToken).ConfigureAwait(false);
            if (pull.ExitCode != 0)
            {
                return new PinResult(change.Path, false, change.Name + ": " + GitHubText.Describe(pull.StandardOutput, pull.StandardError));
            }

            var url = FirstLine(pull.StandardOutput);
            var merge = await _process.RunAsync("gh", ["pr", "view", url, "--json", "mergeable,mergeStateStatus"], _path, cancellationToken)
                .ConfigureAwait(false);
            if (merge.ExitCode == 0 && IsConflicting(merge.StandardOutput))
            {
                return new PinResult(change.Path, true, "Konflikt " + change.Name + " · n", Conflict: true);
            }

            return new PinResult(change.Path, true, "Keine Konflikte · PR " + PullNumber(url));
        }
        finally
        {
            TryDelete(index);
            TryDelete(index + ".lock");
        }
    }

    private Task<ProcessResult> GitAsync(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken)
    {
        return _process.RunAsync("git", arguments, _path, cancellationToken, environment);
    }

    public static string Body(PinChange change, string tab)
    {
        var oldSha = string.IsNullOrEmpty(change.OldSha) ? "keiner" : change.OldSha;
        var branch = string.IsNullOrWhiteSpace(change.ModuleBranch) ? "ohne Branch" : change.ModuleBranch;
        var text = $"Alter Pin: {oldSha}\nNeuer Pin: {change.NewSha}\nBranch: {branch}";
        var partner = BranchOrder.StagingPartner(tab);
        if (partner is not null)
        {
            text += "\nQuelle: " + partner;
        }

        var repo = GitHubRepo.FromUrl(change.ModuleUrl);
        if (repo is null)
        {
            return text;
        }

        return text + $"\n\nhttps://github.com/{repo.Owner}/{repo.Name}/commit/{change.NewSha}";
    }

    private static string PullNumber(string url)
    {
        var slash = url.LastIndexOf('/');
        var number = slash < 0 ? url : url[(slash + 1)..].Trim();
        return number.Length == 0 ? url : number;
    }

    private static string Failure(ProcessResult result)
    {
        var line = FirstLine(result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput);
        var text = GitHubText.Redact(line);
        return text.Length == 0 ? "fehlgeschlagen" : text;
    }

    private static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text.Trim() : text[..end].Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
