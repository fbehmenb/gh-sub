using UmbrellaManager.Config;
using UmbrellaManager.Process;
using UmbrellaManager.Services;

namespace UmbrellaManager.Tests;

public class PinPublisherTests
{
    [Fact]
    public void Release_title_uses_manifest_version_and_stage()
    {
        var change = new PinChange(
            "gb_modules/gb_documents_archive",
            "gb_documents_archive",
            null,
            "8847dd1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "19.0",
            "https://github.com/Org/gb_documents_archive",
            "19.0.1.8.14");

        Assert.Equal("dev", PinPublisher.Stage("dev/19.0"));
        Assert.Equal("staging", PinPublisher.Stage("19.0-staging"));
        Assert.Equal(
            "[REL dev/19.0] gb_documents_archive: 19.0.1.8.14",
            PinPublisher.Title(LocalConfig.DefaultPinTitle, change, "dev/19.0"));
        Assert.Equal(
            "[REL staging/19.0] gb_documents_archive: 8847dd1",
            PinPublisher.Title(LocalConfig.DefaultPinTitle, change with { Version = null }, "staging/19.0"));
        Assert.Equal(
            "dev gb_documents_archive",
            PinPublisher.Title("<stufe> <modul>", change, "dev/19.0"));
        Assert.Contains("Quelle: staging/19.0", PinPublisher.Body(change, "production/19.0"));
        Assert.DoesNotContain("Quelle:", PinPublisher.Body(change, "dev/19.0"));
    }

    [Fact]
    public void Rejected_push_names_the_git_reason_and_an_open_pull_request()
    {
        const string stderr = """
            To https://github.com/Org/odoo_umbrella.git
             ! [rejected]        abcdef1 -> chore/pin-gb_html_drawio-a7e938b-production/20.0 (non-fast-forward)
            error: failed to push some refs to 'https://github.com/Org/odoo_umbrella.git'
            hint: Updates were rejected because a pushed branch tip is behind its remote
            hint: counterpart.
            """;

        Assert.Equal("Push abgelehnt (non-fast-forward)", PinPublisher.PushReason("", stderr));
        Assert.Equal(
            "gb_html_drawio: Push abgelehnt (non-fast-forward). PR 47 ist schon offen",
            PinPublisher.PushFailureText("gb_html_drawio", PinPublisher.PushReason("", stderr), 47));
        Assert.Equal(47, PinPublisher.ParseOpenPull("[{\"number\":47}]"));
        Assert.Null(PinPublisher.ParseOpenPull("[]"));
        Assert.Null(PinPublisher.ParseOpenPull("To https://github.com/Org/odoo_umbrella"));
    }

    [Fact]
    public void Remote_rule_beats_the_rejected_summary()
    {
        const string stderr = """
            remote: error: GH013: Repository rule violations found for refs/heads/chore/pin-gb_a-2222222-dev/19.0.
            remote:
            To https://github.com/Org/odoo_umbrella.git
             ! [remote rejected] abcdef1 -> chore/pin-gb_a-2222222-dev/19.0 (push declined due to repository rule violations)
            error: failed to push some refs to 'https://github.com/Org/odoo_umbrella.git'
            """;

        Assert.Equal(
            "remote: error: GH013: Repository rule violations found for refs/heads/chore/pin-gb_a-2222222-dev/19.0.",
            PinPublisher.PushReason("", stderr));
        Assert.Equal(
            "gb_a: Push abgelehnt",
            PinPublisher.PushFailureText("gb_a", PinPublisher.PushReason("", ""), null));
    }

    [Fact]
    public async Task Moved_gitlink_is_not_pushed()
    {
        var calls = new List<IReadOnlyList<string>>();
        var process = new TipProcess((file, args) =>
        {
            calls.Add(args);
            if (args.Contains("rev-parse"))
            {
                var spec = args[^1];
                if (spec.Contains(':'))
                {
                    return spec.StartsWith("aaaaaaaa", StringComparison.Ordinal)
                        ? new ProcessResult(0, "1111111111111111111111111111111111111111\n", "")
                        : new ProcessResult(0, "9999999999999999999999999999999999999999\n", "");
                }

                return new ProcessResult(0, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n", "");
            }

            return new ProcessResult(0, "", "");
        });

        var ticks = new List<PushTick>();
        var results = await new PinPublisher(process, "C:\\repo").PublishAsync(
            "dev/19.0",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            [new PinChange("gb_a", "gb_a", "1111111111111111111111111111111111111111", "2222222222222222222222222222222222222222", "19.0", null)],
            LocalConfig.DefaultPinTitle,
            CancellationToken.None,
            ticks.Add);

        Assert.Equal(2, ticks.Count);
        Assert.False(ticks[0].Finished);
        Assert.Contains("wird gepusht", ticks[0].Text);
        Assert.True(ticks[1].Finished);
        Assert.Contains("Konflikt", ticks[1].Text);
        Assert.Contains("Konflikt", results[0].Text);
        Assert.True(results[0].Conflict);
        Assert.False(results[0].Created);
        Assert.DoesNotContain(calls, args => args.Contains("push"));
    }

    [Fact]
    public async Task Moved_tip_with_the_same_gitlink_parents_on_the_new_tip()
    {
        var root = Path.Combine(Path.GetTempPath(), "umbrella-manager-pr-" + Guid.NewGuid().ToString("N"));
        var bare = Path.Combine(root, "origin.git");
        var work = Path.Combine(root, "umbrella");
        var git = new GitWithFakeGh();
        try
        {
            Directory.CreateDirectory(root);
            await Git(git, root, "init", "--bare", bare);
            await Git(git, root, "init", "-b", "dev/19.0", work);
            await File.WriteAllTextAsync(Path.Combine(work, "README"), "x");
            await Git(git, work, "add", "README");
            await Git(git, work, "commit", "-m", "init");
            const string oldSha = "1111111111111111111111111111111111111111";
            const string newA = "2222222222222222222222222222222222222222";
            await Git(git, work, "update-index", "--add", "--cacheinfo", "160000," + oldSha + ",gb_a");
            await Git(git, work, "commit", "-m", "links");
            await Git(git, work, "remote", "add", "origin", bare);
            await Git(git, work, "push", "origin", "dev/19.0");
            var loaded = (await Git(git, work, "rev-parse", "HEAD")).StandardOutput.Trim();
            await File.WriteAllTextAsync(Path.Combine(work, "README"), "y");
            await Git(git, work, "add", "README");
            await Git(git, work, "commit", "-m", "readme");
            await Git(git, work, "push", "origin", "dev/19.0");
            var tip = (await Git(git, work, "rev-parse", "HEAD")).StandardOutput.Trim();
            var before = (await Git(git, work, "status", "--porcelain")).StandardOutput;

            var results = await new PinPublisher(git, work).PublishAsync(
                "dev/19.0",
                loaded,
                [new PinChange("gb_a", "gb_a", oldSha, newA, "19.0", "https://github.com/Org/gb_a.git")],
                LocalConfig.DefaultPinTitle,
                CancellationToken.None);

            Assert.True(results[0].Created);
            Assert.Equal(before, (await Git(git, work, "status", "--porcelain")).StandardOutput);
            var head = RemoteHead(await Git(git, work, "ls-remote", "origin", "refs/heads/chore/pin-gb_a-" + newA[..7] + "-dev/19.0"));
            var parent = (await Git(git, work, "rev-parse", head + "^")).StandardOutput.Trim();
            Assert.Equal(tip, parent);
            Assert.DoesNotContain(git.Calls, args => args.Contains("--force") || args.Contains("-f"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Two_pins_become_two_commits_and_leave_the_worktree_clean()
    {
        var root = Path.Combine(Path.GetTempPath(), "umbrella-manager-pr-" + Guid.NewGuid().ToString("N"));
        var bare = Path.Combine(root, "origin.git");
        var work = Path.Combine(root, "umbrella");
        var git = new GitWithFakeGh();
        try
        {
            Directory.CreateDirectory(root);
            await Git(git, root, "init", "--bare", bare);
            await Git(git, root, "init", "-b", "dev/19.0", work);
            await File.WriteAllTextAsync(Path.Combine(work, "README"), "x");
            await Git(git, work, "add", "README");
            await Git(git, work, "commit", "-m", "init");
            const string oldSha = "1111111111111111111111111111111111111111";
            const string newA = "2222222222222222222222222222222222222222";
            const string newB = "3333333333333333333333333333333333333333";
            await Git(git, work, "update-index", "--add", "--cacheinfo", "160000," + oldSha + ",gb_a");
            await Git(git, work, "update-index", "--add", "--cacheinfo", "160000," + oldSha + ",gb_b");
            await Git(git, work, "commit", "-m", "links");
            await Git(git, work, "remote", "add", "origin", bare);
            await Git(git, work, "push", "origin", "dev/19.0");
            var tip = (await Git(git, work, "rev-parse", "HEAD")).StandardOutput.Trim();
            var before = (await Git(git, work, "status", "--porcelain")).StandardOutput;

            var results = await new PinPublisher(git, work).PublishAsync(
                "dev/19.0",
                tip,
                [
                    new PinChange("gb_a", "gb_a", oldSha, newA, "19.0", "https://github.com/Org/gb_a.git"),
                    new PinChange("gb_b", "gb_b", oldSha, newB, "19.0", "https://github.com/Org/gb_b.git"),
                ],
                LocalConfig.DefaultPinTitle,
                CancellationToken.None);

            Assert.Equal(2, results.Count(result => result.Created));
            Assert.Equal(before, (await Git(git, work, "status", "--porcelain")).StandardOutput);
            var pulls = git.Calls.Where(args => args.Contains("pr") && args.Contains("create")).ToArray();
            Assert.Equal(2, pulls.Length);
            Assert.All(pulls, args => Assert.Contains("dev/19.0", args));
            Assert.Contains(pulls, args => args.Any(argument => argument.Contains("chore/pin-gb_a-", StringComparison.Ordinal)));
            Assert.Contains(pulls, args => args.Any(argument => argument.Contains("chore/pin-gb_b-", StringComparison.Ordinal)));
            Assert.Contains(pulls, args => args.Contains("[REL dev/19.0] gb_a: 2222222"));
            Assert.Contains(pulls, args => args.Contains("[REL dev/19.0] gb_b: 3333333"));
            Assert.DoesNotContain(git.Calls, args => args.Contains("--force") || args.Contains("-f"));

            var first = RemoteHead(await Git(git, work, "ls-remote", "origin", "refs/heads/chore/pin-gb_a-" + newA[..7] + "-dev/19.0"));
            var second = RemoteHead(await Git(git, work, "ls-remote", "origin", "refs/heads/chore/pin-gb_b-" + newB[..7] + "-dev/19.0"));
            var diffA = (await Git(git, work, "diff-tree", "-r", "--name-only", tip, first)).StandardOutput;
            var diffB = (await Git(git, work, "diff-tree", "-r", "--name-only", tip, second)).StandardOutput;
            Assert.Contains("gb_a", diffA);
            Assert.DoesNotContain("gb_b", diffA);
            Assert.Contains("gb_b", diffB);
            Assert.DoesNotContain("gb_a", diffB);
        }
        finally
        {
            DeleteRepo(root);
        }
    }

    [Fact]
    public async Task Second_push_of_the_same_pin_reports_the_open_pull_request()
    {
        var root = Path.Combine(Path.GetTempPath(), "umbrella-manager-pr-" + Guid.NewGuid().ToString("N"));
        var bare = Path.Combine(root, "origin.git");
        var work = Path.Combine(root, "umbrella");
        var git = new GitWithFakeGh
        {
            Gh = args => args.Contains("list")
                ? new ProcessResult(0, "[{\"number\":47}]", "")
                : new ProcessResult(0, "https://example.invalid/pull/1", ""),
        };
        try
        {
            Directory.CreateDirectory(root);
            await Git(git, root, "init", "--bare", bare);
            await Git(git, root, "init", "-b", "dev/19.0", work);
            await File.WriteAllTextAsync(Path.Combine(work, "README"), "x");
            await Git(git, work, "add", "README");
            await Git(git, work, "commit", "-m", "init");
            const string oldSha = "1111111111111111111111111111111111111111";
            const string newA = "2222222222222222222222222222222222222222";
            await Git(git, work, "update-index", "--add", "--cacheinfo", "160000," + oldSha + ",gb_a");
            await Git(git, work, "commit", "-m", "links");
            await Git(git, work, "remote", "add", "origin", bare);
            await Git(git, work, "push", "origin", "dev/19.0");
            var loaded = (await Git(git, work, "rev-parse", "HEAD")).StandardOutput.Trim();
            var change = new PinChange("gb_a", "gb_a", oldSha, newA, "19.0", "https://github.com/Org/gb_a.git");

            var first = await new PinPublisher(git, work).PublishAsync(
                "dev/19.0",
                loaded,
                [change],
                LocalConfig.DefaultPinTitle,
                CancellationToken.None);
            Assert.True(first[0].Created);

            await File.WriteAllTextAsync(Path.Combine(work, "README"), "y");
            await Git(git, work, "add", "README");
            await Git(git, work, "commit", "-m", "readme");
            await Git(git, work, "push", "origin", "dev/19.0");
            var before = (await Git(git, work, "status", "--porcelain")).StandardOutput;

            var second = await new PinPublisher(git, work).PublishAsync(
                "dev/19.0",
                loaded,
                [change],
                LocalConfig.DefaultPinTitle,
                CancellationToken.None);

            Assert.False(second[0].Created);
            Assert.Contains("Push abgelehnt (non-fast-forward)", second[0].Text);
            Assert.Contains("PR 47 ist schon offen", second[0].Text);
            Assert.DoesNotContain("To ", second[0].Text);
            Assert.Equal(before, (await Git(git, work, "status", "--porcelain")).StandardOutput);
            Assert.Contains(git.Calls, args => args.Contains("pr") && args.Contains("list") && args.Contains("dev/19.0"));
            Assert.DoesNotContain(git.Calls, args => args.Contains("--force") || args.Contains("-f"));
        }
        finally
        {
            DeleteRepo(root);
        }
    }

    private static void DeleteRepo(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(root, recursive: true);
    }

    private static string RemoteHead(ProcessResult result)
    {
        var tab = result.StandardOutput.IndexOf('\t');
        return tab < 0 ? result.StandardOutput.Trim() : result.StandardOutput[..tab].Trim();
    }

    private static async Task<ProcessResult> Git(GitWithFakeGh git, string directory, params string[] args)
    {
        var result = await git.RunAsync("git", args, directory);
        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        return result;
    }
}

sealed class TipProcess : IExternalProcess
{
    private readonly Func<string, IReadOnlyList<string>, ProcessResult> _handle;

    public TipProcess(Func<string, IReadOnlyList<string>, ProcessResult> handle)
    {
        _handle = handle;
    }

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        return Task.FromResult(_handle(fileName, arguments));
    }
}

sealed class GitWithFakeGh : IExternalProcess
{
    private readonly ExternalProcess _git = new();

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public Func<IReadOnlyList<string>, ProcessResult>? Gh { get; init; }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        Calls.Add(arguments);
        if (fileName == "gh")
        {
            return Gh is null
                ? new ProcessResult(0, "https://example.invalid/pull/" + Calls.Count(args => args.Contains("pr")), "")
                : Gh(arguments);
        }

        var env = environment is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(environment, StringComparer.Ordinal);
        env["GIT_AUTHOR_NAME"] = "Test";
        env["GIT_AUTHOR_EMAIL"] = "test@example.com";
        env["GIT_COMMITTER_NAME"] = "Test";
        env["GIT_COMMITTER_EMAIL"] = "test@example.com";
        return await _git.RunAsync(fileName, arguments, workingDirectory, cancellationToken, env);
    }
}
