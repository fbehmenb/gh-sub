using UmbrellaManager.Config;
using UmbrellaManager.Process;
using UmbrellaManager.Services;

namespace UmbrellaManager.Tests;

public class PinPublisherTests
{
    [Fact]
    public async Task Stale_branch_is_not_pushed()
    {
        var calls = new List<IReadOnlyList<string>>();
        var process = new TipProcess((file, args) =>
        {
            calls.Add(args);
            if (args.Contains("rev-parse"))
            {
                return new ProcessResult(0, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n", "");
            }

            return new ProcessResult(0, "", "");
        });

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => new PinPublisher(process, "C:\\repo").PublishAsync(
            "dev/19.0",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            [new PinChange("gb_a", "gb_a", "1111111111111111111111111111111111111111", "2222222222222222222222222222222222222222", "19.0", null)],
            LocalConfig.DefaultPinTitle,
            CancellationToken.None));

        Assert.Contains("weitergelaufen", exception.Message);
        Assert.DoesNotContain(calls, args => args.Contains("push"));
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
            return new ProcessResult(0, "https://example.invalid/pull/" + Calls.Count(args => args.Contains("pr")), "");
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
