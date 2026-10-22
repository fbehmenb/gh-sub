namespace UmbrellaManager.Services;

using UmbrellaManager.GitHub;
using UmbrellaManager.Model;
using UmbrellaManager.Parsing;
using UmbrellaManager.Process;

public sealed record PinChange(string Path, string Name, string? OldSha, string NewSha, string? ModuleBranch, string? ModuleUrl);

public sealed record PinResult(string Path, bool Created, string Text);

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

    public static string Title(string template, PinChange change)
    {
        var branch = string.IsNullOrWhiteSpace(change.ModuleBranch) ? "ohne Branch" : change.ModuleBranch;
        return template
            .Replace("<modul>", change.Name, StringComparison.Ordinal)
            .Replace("<sha7>", Sha.Short(change.NewSha), StringComparison.Ordinal)
            .Replace("<modul-branch>", branch, StringComparison.Ordinal);
    }

    public static string HeadBranch(PinChange change, string tab)
    {
        return "chore/pin-" + change.Name + "-" + Sha.Short(change.NewSha) + "-" + tab;
    }

    public static IReadOnlyList<string> Question(string tab, IReadOnlyList<PinChange> changes, string template)
    {
        var lines = new List<string> { "Wirklich folgende Änderungen als Pull Request pushen?" };
        foreach (var change in changes.OrderBy(change => change.Path, StringComparer.Ordinal))
        {
            var oldSha = string.IsNullOrEmpty(change.OldSha) ? "keiner" : Sha.Short(change.OldSha);
            lines.Add($"{tab}  {change.Path}  {oldSha} → {Sha.Short(change.NewSha)}");
            lines.Add(Title(template, change));
        }

        lines.Add("j ja    n nein");
        return lines;
    }

    public async Task<IReadOnlyList<PinResult>> PublishAsync(
        string tab,
        string loadedTip,
        IReadOnlyList<PinChange> changes,
        string titleTemplate,
        CancellationToken cancellationToken)
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
        if (tip.ExitCode != 0 || !tip.StandardOutput.Trim().Equals(loadedTip, StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFacingException($"Der Branch {tab} ist weitergelaufen. Bitte mit r neu laden.");
        }

        var results = new List<PinResult>();
        foreach (var change in changes)
        {
            results.Add(await PublishOneAsync(tab, change, titleTemplate, cancellationToken).ConfigureAwait(false));
        }

        return results;
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

            var title = Title(titleTemplate, change);
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

            var body = Body(change);
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
            return new PinResult(change.Path, true, "angelegt: " + url);
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

    private static string Body(PinChange change)
    {
        var oldSha = string.IsNullOrEmpty(change.OldSha) ? "keiner" : change.OldSha;
        var branch = string.IsNullOrWhiteSpace(change.ModuleBranch) ? "ohne Branch" : change.ModuleBranch;
        var text = $"Alter Pin: {oldSha}\nNeuer Pin: {change.NewSha}\nBranch: {branch}";
        var repo = GitHubRepo.FromUrl(change.ModuleUrl);
        if (repo is null)
        {
            return text;
        }

        return text + $"\n\nhttps://github.com/{repo.Owner}/{repo.Name}/commit/{change.NewSha}";
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
