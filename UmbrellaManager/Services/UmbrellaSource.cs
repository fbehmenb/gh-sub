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
    private readonly SemaphoreSlim _remote = new(1, 1);
    private string? _origin;
    private bool _originLoaded;

    public UmbrellaSource(IExternalProcess process, ResponseCache cache, string path)
    {
        _process = process;
        _cache = cache;
        _path = path;
    }

    public bool Offline { get; private set; }

    public IReadOnlyDictionary<string, string> BranchTips { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public void ClearCache()
    {
        _cache.Clear();
        Offline = false;
    }

    public async Task<string?> ModuleVersionAsync(string? moduleUrl, string sha, CancellationToken cancellationToken)
    {
        var repo = GitHubRepo.FromUrl(moduleUrl);
        if (repo is null)
        {
            return null;
        }

        var route = $"repos/{Escape(repo.Owner)}/{Escape(repo.Name)}/contents/__manifest__.py?ref={Escape(sha)}";
        var result = await _process.RunAsync("gh", ["api", route], _path, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            _cache.Set(route, result.StandardOutput);
            return GitHubParse.ParseManifestVersion(result.StandardOutput);
        }

        var raw = result.StandardOutput + "\n" + result.StandardError;
        if (raw.Contains("404", StringComparison.Ordinal))
        {
            return null;
        }

        var message = GitHubText.Describe(result.StandardOutput, result.StandardError);
        if (GitHubText.IsOffline(message) && _cache.TryGet(route, out var cached))
        {
            Offline = true;
            return GitHubParse.ParseManifestVersion(cached);
        }

        throw new UserFacingException(message);
    }

    public async Task<IReadOnlyList<PinResult>> PublishPinsAsync(
        string tab,
        string loadedTip,
        IReadOnlyList<PinChange> changes,
        string titleTemplate,
        CancellationToken cancellationToken,
        Action<PushTick>? onProgress = null)
    {
        await _remote.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await new PinPublisher(_process, _path)
                .PublishAsync(tab, loadedTip, changes, titleTemplate, cancellationToken, onProgress)
                .ConfigureAwait(false);
        }
        finally
        {
            _remote.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>?> ListRemoteHeadsAsync(CancellationToken cancellationToken)
    {
        await _remote.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var remote = await _process.RunAsync("git", ["ls-remote", "--heads", "origin"], _path, cancellationToken)
                .ConfigureAwait(false);
            if (remote.ExitCode != 0)
            {
                return null;
            }

            return BranchOrder.ParseRemoteTips(remote.StandardOutput);
        }
        finally
        {
            _remote.Release();
        }
    }

    public async Task<string> FetchBranchTipAsync(string branch, CancellationToken cancellationToken)
    {
        await _remote.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fetch = await _process.RunAsync(
                "git",
                ["fetch", "origin", "+refs/heads/" + branch + ":refs/remotes/origin/" + branch],
                _path,
                cancellationToken).ConfigureAwait(false);
            if (fetch.ExitCode != 0)
            {
                throw new UserFacingException("Branch konnte nicht geholt werden. " + GitHubText.Redact(FirstLine(fetch.StandardError)));
            }

            var tip = await ReadTipAsync(branch, cancellationToken).ConfigureAwait(false);
            if (tip is null)
            {
                throw new UserFacingException("Branch konnte nicht gelesen werden.");
            }

            return tip;
        }
        finally
        {
            _remote.Release();
        }
    }

    public async Task<StagingPins> LoadStagingHistoryAsync(string stagingBranch, string path, CancellationToken cancellationToken)
    {
        var current = await ReadGitlinkAsync("origin/" + stagingBranch, path, cancellationToken).ConfigureAwait(false);
        var log = await _process.RunAsync(
            "git",
            ["--no-pager", "-c", "core.quotepath=false", "log", "origin/" + stagingBranch, "--raw", "--no-abbrev", "--pretty=format:", "--", path],
            _path,
            cancellationToken).ConfigureAwait(false);
        if (log.ExitCode != 0)
        {
            throw new UserFacingException("Staging-Pins konnten nicht gelesen werden. " + GitHubText.Redact(FirstLine(log.StandardError)));
        }

        var pins = StagingHistory.Parse(log.StandardOutput).ToList();
        if (current is not null && pins.All(sha => !Sha.Same(sha, current)))
        {
            pins.Insert(0, current);
        }

        return new StagingPins(current, pins);
    }

    public async Task<IReadOnlyDictionary<string, string>> ComparePinsAsync(
        SubmoduleEntry module,
        string productionSha,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        var compared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var repo = GitHubRepo.FromUrl(module.Url);
        if (repo is null || candidates.Count == 0)
        {
            return compared;
        }

        using var gate = new SemaphoreSlim(4);
        var tasks = candidates.Select(async sha =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var route = $"repos/{Escape(repo.Owner)}/{Escape(repo.Name)}/compare/{Escape(productionSha)}...{Escape(sha)}";
                var status = GitHubParse.ParseCompareStatus(await ApiAsync(route, cancellationToken).ConfigureAwait(false));
                if (string.IsNullOrEmpty(status))
                {
                    return;
                }

                lock (compared)
                {
                    compared[sha] = status;
                }
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return compared;
    }

    public async Task<IReadOnlyList<CommitInfo>> LoadCommitsAsync(
        SubmoduleEntry module,
        IReadOnlyList<string> shas,
        CancellationToken cancellationToken)
    {
        var repo = GitHubRepo.FromUrl(module.Url);
        if (repo is null)
        {
            return [];
        }

        var commits = new List<CommitInfo>();
        foreach (var sha in shas)
        {
            var commit = await CommitAsync(repo, sha, cancellationToken).ConfigureAwait(false);
            if (commit is not null)
            {
                commits.Add(commit.Value);
            }
        }

        return commits;
    }

    public async Task<string?> ReadGitlinkAsync(string commit, string path, CancellationToken cancellationToken)
    {
        var result = await _process.RunAsync(
            "git",
            ["rev-parse", "--verify", "--end-of-options", commit + ":" + path],
            _path,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var sha = result.StandardOutput.Trim();
        return sha.Length == 40 ? sha : null;
    }

    public async Task<string?> ReadTipAsync(string branch, CancellationToken cancellationToken)
    {
        var tip = await _process.RunAsync("git", ["rev-parse", "origin/" + branch], _path, cancellationToken).ConfigureAwait(false);
        if (tip.ExitCode != 0)
        {
            return null;
        }

        var sha = tip.StandardOutput.Trim();
        return sha.Length == 40 ? sha : null;
    }

    public async Task<IReadOnlyList<string>> LoadBranchesAsync(string hidePattern, CancellationToken cancellationToken)
    {
        await _remote.WaitAsync(cancellationToken).ConfigureAwait(false);
        ProcessResult fetch;
        ProcessResult remote;
        try
        {
            fetch = await _process.RunAsync(
                "git",
                ["fetch", "origin", "+refs/heads/*:refs/remotes/origin/*"],
                _path,
                cancellationToken).ConfigureAwait(false);
            remote = await _process.RunAsync("git", ["ls-remote", "--heads", "origin"], _path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _remote.Release();
        }

        if (remote.ExitCode == 0)
        {
            if (fetch.ExitCode != 0)
            {
                Offline = true;
            }

            var names = await WithGitmodulesAsync(BranchOrder.Apply(BranchOrder.ParseRemoteHeads(remote.StandardOutput), hidePattern), cancellationToken)
                .ConfigureAwait(false);
            BranchTips = await TipsForAsync(names, BranchOrder.ParseRemoteTips(remote.StandardOutput), cancellationToken).ConfigureAwait(false);
            return names;
        }

        var local = await _process.RunAsync("git", ["branch", "-r", "--format=%(refname:short)"], _path, cancellationToken).ConfigureAwait(false);
        if (local.ExitCode != 0)
        {
            throw new UserFacingException("Branches konnten nicht gelesen werden. " + GitHubText.Redact(FirstLine(remote.StandardError)));
        }

        Offline = true;
        var fallback = await WithGitmodulesAsync(BranchOrder.Apply(BranchOrder.ParseLocalRemoteBranches(local.StandardOutput), hidePattern), cancellationToken)
            .ConfigureAwait(false);
        BranchTips = await TipsForAsync(fallback, new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        return fallback;
    }

    private async Task<IReadOnlyDictionary<string, string>> TipsForAsync(
        IReadOnlyList<string> branches,
        IReadOnlyDictionary<string, string> advertised,
        CancellationToken cancellationToken)
    {
        var tips = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var branch in branches)
        {
            if (advertised.TryGetValue(branch, out var sha))
            {
                tips[branch] = sha;
                continue;
            }

            var parsed = await ReadTipAsync(branch, cancellationToken).ConfigureAwait(false);
            if (parsed is not null)
            {
                tips[branch] = parsed;
            }
        }

        return tips;
    }

    private async Task<IReadOnlyList<string>> WithGitmodulesAsync(IReadOnlyList<string> branches, CancellationToken cancellationToken)
    {
        if (branches.Count == 0)
        {
            return branches;
        }

        var kept = new List<string>();
        foreach (var branch in branches)
        {
            var probe = await _process.RunAsync(
                "git",
                ["cat-file", "-e", "origin/" + branch + ":.gitmodules"],
                _path,
                cancellationToken).ConfigureAwait(false);
            if (probe.ExitCode == 0)
            {
                kept.Add(branch);
            }
        }

        if (kept.Count == 0)
        {
            throw new UserFacingException("Kein Branch enthält eine .gitmodules.");
        }

        return kept;
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
        var origin = await OriginUrlAsync(cancellationToken).ConfigureAwait(false);
        var entries = SubmoduleTree.Merge(gitmodules, tree.StandardOutput)
            .Select(entry => entry with { Url = GitHubRepo.Resolve(origin, entry.Url) })
            .ToList();
        return SubmoduleTree.Build(entries);
    }

    private async Task<string?> OriginUrlAsync(CancellationToken cancellationToken)
    {
        if (_originLoaded)
        {
            return _origin;
        }

        _originLoaded = true;
        var remote = await _process.RunAsync("git", ["remote", "get-url", "origin"], _path, cancellationToken).ConfigureAwait(false);
        _origin = remote.ExitCode == 0 ? remote.StandardOutput.Trim() : null;
        return _origin;
    }

    public async Task<GraphSnapshot> LoadGraphAsync(
        SubmoduleEntry module,
        CancellationToken cancellationToken,
        string? pendingSha = null,
        string? includeSha = null)
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

        standardCommits = await WithListedCommitAsync(repo, standardCommits, extraCommits, module.Sha, cancellationToken)
            .ConfigureAwait(false);
        standardCommits = await WithListedCommitAsync(repo, standardCommits, extraCommits, includeSha, cancellationToken)
            .ConfigureAwait(false);

        var lanes = new List<string> { standardName };
        lanes.AddRange(extra.Select(lane => lane.Name));
        var rows = GraphBuilder.Build(standardName, standardCommits, extraCommits, module.Sha, pendingSha);
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

    private async Task<IReadOnlyList<CommitInfo>> WithListedCommitAsync(
        GitHubRepo repo,
        IReadOnlyList<CommitInfo> standard,
        IReadOnlyList<(string Name, IReadOnlyList<CommitInfo> Commits)> extra,
        string? sha,
        CancellationToken cancellationToken)
    {
        if (sha is null || Listed(standard, extra, sha))
        {
            return standard;
        }

        var commit = await CommitAsync(repo, sha, cancellationToken).ConfigureAwait(false);
        return commit is null ? standard : [..standard, commit.Value];
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
