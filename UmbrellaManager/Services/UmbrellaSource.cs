namespace UmbrellaManager.Services;

using UmbrellaManager.GitHub;
using UmbrellaManager.Model;
using UmbrellaManager.Parsing;
using UmbrellaManager.Process;

public sealed class UmbrellaSource
{
    private readonly IExternalProcess _process;
    private readonly ResponseCache _cache;
    private readonly string _path;

    public UmbrellaSource(IExternalProcess process, ResponseCache cache, string path)
    {
        _process = process;
        _cache = cache;
        _path = path;
    }

    public bool Offline { get; private set; }

    public void ClearCache()
    {
        _cache.Clear();
        Offline = false;
    }

    public async Task<IReadOnlyList<string>> LoadBranchesAsync(string hidePattern, CancellationToken cancellationToken)
    {
        var fetch = await _process.RunAsync("git", ["fetch", "origin"], _path, cancellationToken).ConfigureAwait(false);
        var remote = await _process.RunAsync("git", ["ls-remote", "--heads", "origin"], _path, cancellationToken).ConfigureAwait(false);
        if (remote.ExitCode == 0)
        {
            if (fetch.ExitCode != 0)
            {
                Offline = true;
            }

            return BranchOrder.Apply(BranchOrder.ParseRemoteHeads(remote.StandardOutput), hidePattern);
        }

        var local = await _process.RunAsync("git", ["branch", "-r", "--format=%(refname:short)"], _path, cancellationToken).ConfigureAwait(false);
        if (local.ExitCode != 0)
        {
            throw new UserFacingException("Branches konnten nicht gelesen werden. " + GitHubText.Redact(FirstLine(remote.StandardError)));
        }

        Offline = true;
        return BranchOrder.Apply(BranchOrder.ParseLocalRemoteBranches(local.StandardOutput), hidePattern);
    }

    public async Task<IReadOnlyList<TreeNode>> LoadTreeAsync(string branch, CancellationToken cancellationToken)
    {
        var reference = "origin/" + branch;
        var modules = await _process.RunAsync(
            "git",
            ["--no-pager", "-c", "core.quotepath=false", "show", reference + ":.gitmodules"],
            _path,
            cancellationToken).ConfigureAwait(false);
        var tree = await _process.RunAsync(
            "git",
            ["--no-pager", "-c", "core.quotepath=false", "ls-tree", "-r", reference],
            _path,
            cancellationToken).ConfigureAwait(false);
        if (tree.ExitCode != 0)
        {
            throw new UserFacingException($"Branch {branch} konnte nicht gelesen werden. " + GitHubText.Redact(FirstLine(tree.StandardError)));
        }

        var gitmodules = modules.ExitCode == 0 ? modules.StandardOutput : "";
        return SubmoduleTree.Build(SubmoduleTree.Merge(gitmodules, tree.StandardOutput));
    }

    public async Task<GraphSnapshot> LoadGraphAsync(SubmoduleEntry module, CancellationToken cancellationToken)
    {
        var repo = GitHubRepo.FromUrl(module.Url);
        if (repo is null)
        {
            return new GraphSnapshot([], [], module.Url is null
                ? "Keine GitHub-URL in .gitmodules."
                : "Nur GitHub-URLs werden gelesen.");
        }

        var standardName = module.Branch;
        if (string.IsNullOrEmpty(standardName))
        {
            standardName = await DefaultBranchAsync(repo, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(standardName))
        {
            return new GraphSnapshot([], [], "Kein Standard-Branch für das Modul.");
        }

        var standardCommits = await CommitsAsync(repo, standardName, cancellationToken).ConfigureAwait(false);
        var pinOnStandard = module.Sha is not null && standardCommits.Any(commit => Sha.Same(commit.Sha, module.Sha));
        if (module.Sha is not null && !pinOnStandard)
        {
            pinOnStandard = await IsOnBranchAsync(repo, module.Sha, standardName, cancellationToken).ConfigureAwait(false);
        }

        var tips = await BranchesAsync(repo, cancellationToken).ConfigureAwait(false);
        var matched = tips.Where(tip => tip.Name == standardName).Take(1).ToArray();
        var fallbackCommit = standardCommits.Count == 0 ? default : standardCommits[0];
        var standardTip = matched.Length == 1
            ? matched[0]
            : new BranchTip(standardName, fallbackCommit.Sha ?? "", fallbackCommit.Date, false);
        var marked = new List<BranchTip>();
        if (module.Sha is not null && !pinOnStandard)
        {
            foreach (var tip in tips.Where(tip => tip.Name != standardName).OrderByDescending(tip => tip.TipDate).Take(10))
            {
                var onBranch = await IsOnBranchAsync(repo, module.Sha, tip.Name, cancellationToken).ConfigureAwait(false);
                marked.Add(tip with { PinIsOnBranch = onBranch });
            }
        }

        var known = marked.Count == 0 ? tips : MergeTips(tips, marked);
        var standardShas = standardCommits.Select(commit => commit.Sha).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extra = GraphBuilder.SelectExtraLanes(standardTip, pinOnStandard, known, standardShas);
        var extraCommits = new List<(string Name, IReadOnlyList<CommitInfo> Commits)>();
        foreach (var lane in extra)
        {
            extraCommits.Add((lane.Name, await CommitsAsync(repo, lane.Name, cancellationToken).ConfigureAwait(false)));
        }

        if (module.Sha is not null && !Listed(standardCommits, extraCommits, module.Sha))
        {
            var pin = await CommitAsync(repo, module.Sha, cancellationToken).ConfigureAwait(false);
            if (pin is not null)
            {
                standardCommits = [..standardCommits, pin.Value];
            }
        }

        var lanes = new List<string> { standardName };
        lanes.AddRange(extra.Select(lane => lane.Name));
        var rows = GraphBuilder.Build(standardName, standardCommits, extraCommits, module.Sha, pendingSha: null);
        var message = extra.Count == GraphBuilder.MaxExtraLanes ? "Weitere Branches sind nicht als Spuren gezeigt." : "";
        return new GraphSnapshot(lanes, rows, message);
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadPinMarksAsync(
        IReadOnlyList<SubmoduleEntry> modules,
        CancellationToken cancellationToken)
    {
        var marks = new Dictionary<string, string>(StringComparer.Ordinal);
        using var gate = new SemaphoreSlim(4);
        var tasks = modules.Select(async module =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var mark = await MarkAsync(module, cancellationToken).ConfigureAwait(false);
                lock (marks)
                {
                    marks[module.Path] = mark;
                }
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return marks;
    }

    private async Task<string> MarkAsync(SubmoduleEntry module, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(module.Sha))
        {
            return "kein Pin";
        }

        var repo = GitHubRepo.FromUrl(module.Url);
        if (repo is null)
        {
            return "keine URL";
        }

        try
        {
            var branch = module.Branch;
            if (string.IsNullOrEmpty(branch))
            {
                branch = await DefaultBranchAsync(repo, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(branch))
            {
                return "kein Branch";
            }

            var onBranch = await IsOnBranchAsync(repo, module.Sha, branch, cancellationToken).ConfigureAwait(false);
            return onBranch ? "✓" : "nicht " + branch;
        }
        catch (UserFacingException ex) when (GitHubText.IsOffline(ex.Message))
        {
            Offline = true;
            return GitHubText.Offline;
        }
        catch (UserFacingException)
        {
            return "nicht geprüft";
        }
    }

    private async Task<string?> DefaultBranchAsync(GitHubRepo repo, CancellationToken cancellationToken)
    {
        var json = await ApiAsync($"repos/{Escape(repo.Owner)}/{Escape(repo.Name)}", cancellationToken).ConfigureAwait(false);
        return GitHubParse.ParseDefaultBranch(json);
    }

    private async Task<IReadOnlyList<CommitInfo>> CommitsAsync(GitHubRepo repo, string branch, CancellationToken cancellationToken)
    {
        var route = $"repos/{Escape(repo.Owner)}/{Escape(repo.Name)}/commits?sha={Escape(branch)}&per_page=30";
        return GitHubParse.ParseCommits(await ApiAsync(route, cancellationToken).ConfigureAwait(false));
    }

    private async Task<CommitInfo?> CommitAsync(GitHubRepo repo, string sha, CancellationToken cancellationToken)
    {
        var route = $"repos/{Escape(repo.Owner)}/{Escape(repo.Name)}/commits/{Escape(sha)}";
        var commits = GitHubParse.ParseCommits(await ApiAsync(route, cancellationToken).ConfigureAwait(false));
        return commits.Count == 0 ? null : commits[0];
    }

    private async Task<IReadOnlyList<BranchTip>> BranchesAsync(GitHubRepo repo, CancellationToken cancellationToken)
    {
        var route = $"repos/{Escape(repo.Owner)}/{Escape(repo.Name)}/branches?per_page=100";
        return GitHubParse.ParseBranches(await ApiAsync(route, cancellationToken).ConfigureAwait(false));
    }

    private async Task<bool> IsOnBranchAsync(GitHubRepo repo, string sha, string branch, CancellationToken cancellationToken)
    {
        var route = $"repos/{Escape(repo.Owner)}/{Escape(repo.Name)}/compare/{Escape(sha)}...{Escape(branch)}";
        var status = GitHubParse.ParseCompareStatus(await ApiAsync(route, cancellationToken).ConfigureAwait(false));
        return CompareStatus.PinIsOnBranch(status);
    }

    private async Task<string> ApiAsync(string route, CancellationToken cancellationToken)
    {
        var result = await _process.RunAsync("gh", ["api", route], _path, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            _cache.Set(route, result.StandardOutput);
            return result.StandardOutput;
        }

        var message = GitHubText.Describe(result.StandardOutput, result.StandardError);
        if (GitHubText.IsOffline(message) && _cache.TryGet(route, out var cached))
        {
            Offline = true;
            return cached;
        }

        throw new UserFacingException(message);
    }

    private static List<BranchTip> MergeTips(IReadOnlyList<BranchTip> tips, IReadOnlyList<BranchTip> marked)
    {
        var flags = marked.ToDictionary(tip => tip.Name, tip => tip.PinIsOnBranch, StringComparer.Ordinal);
        return tips.Select(tip => flags.TryGetValue(tip.Name, out var onBranch) ? tip with { PinIsOnBranch = onBranch } : tip).ToList();
    }

    private static bool Listed(
        IReadOnlyList<CommitInfo> standard,
        IReadOnlyList<(string Name, IReadOnlyList<CommitInfo> Commits)> extra,
        string sha)
    {
        return standard.Any(commit => Sha.Same(commit.Sha, sha))
            || extra.Any(lane => lane.Commits.Any(commit => Sha.Same(commit.Sha, sha)));
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text.Trim() : text[..end].Trim();
    }
}
