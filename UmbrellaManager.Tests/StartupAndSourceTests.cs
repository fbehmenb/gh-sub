using UmbrellaManager.Model;
using UmbrellaManager.Process;
using UmbrellaManager.Services;

namespace UmbrellaManager.Tests;

public class StartupAndSourceTests
{
    [Fact]
    public async Task Missing_git_is_a_clear_message()
    {
        var process = new ScriptedProcess((_, _) => throw new UserFacingException("git wurde nicht im PATH gefunden."));
        var message = await StartupCheck.FindProblemAsync(process, "C:\\repo", _ => true);

        Assert.Equal("git wurde nicht im PATH gefunden.", message);
    }

    [Fact]
    public async Task Missing_login_is_a_clear_message()
    {
        var process = new ScriptedProcess((file, args) =>
        {
            if (args.Contains("auth"))
            {
                return new ProcessResult(1, "", "not logged in");
            }

            return new ProcessResult(0, "true", "");
        });

        var message = await StartupCheck.FindProblemAsync(process, "C:\\repo", _ => true);

        Assert.Contains("gh auth login", message);
    }

    [Fact]
    public async Task Worktree_without_gitmodules_is_accepted()
    {
        var process = new ScriptedProcess((_, _) => new ProcessResult(0, "true", ""));
        var message = await StartupCheck.FindProblemAsync(process, "C:\\repo", _ => true);

        Assert.Null(message);
    }

    [Fact]
    public async Task Tokens_are_not_passed_to_a_process()
    {
        var exception = await Assert.ThrowsAsync<UserFacingException>(() =>
            new ExternalProcess().RunAsync("gh", ["api", "repos/org/repo", "ghp_secretvalue"], null));

        Assert.DoesNotContain("ghp_secretvalue", exception.Message);
    }

    [Fact]
    public void Sso_404_is_not_the_raw_error()
    {
        var message = GitHub.GitHubText.Describe("", "HTTP 404: ghp_secretvalue");
        Assert.Contains("SSO", message);
        Assert.DoesNotContain("ghp_secretvalue", message);
    }

    [Fact]
    public async Task Two_modules_load_from_git_output()
    {
        const string pin = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var process = new ScriptedProcess((file, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("ls-remote"))
            {
                return new ProcessResult(0, "abc\trefs/heads/dev/19.0\nabc\trefs/heads/chore/pin-x-aaaaaaa-dev/19.0\n", "");
            }

            if (joined.Contains("show"))
            {
                return new ProcessResult(0, """
                    [submodule "gb_modules/gb_a"]
                    	path = gb_modules/gb_a
                    	url = https://github.com/Org/gb_a.git
                    	branch = 19.0
                    [submodule "ratocon_x"]
                    	path = ratocon_x
                    	url = https://github.com/Org/ratocon_x.git
                    	branch = 19.0
                    """, "");
            }

            if (joined.Contains("ls-tree"))
            {
                return new ProcessResult(0, $"160000 commit {pin}\tgb_modules/gb_a\n160000 commit {pin}\tratocon_x\n", "");
            }

            return new ProcessResult(0, "", "");
        });

        var source = new UmbrellaSource(process, new ResponseCache(), "C:\\repo");
        var branches = await source.LoadBranchesAsync("chore/pin-*", CancellationToken.None);
        var roots = await source.LoadTreeAsync(branches[0], CancellationToken.None);

        Assert.Equal(["dev/19.0"], branches);
        Assert.Contains(roots, node => node.Name == "gb_modules");
        Assert.Contains(roots, node => node.Name == "ratocon_x" && node.IsModule);
    }

    [Fact]
    public async Task Branches_without_gitmodules_stay_hidden()
    {
        var process = new ScriptedProcess((file, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("ls-remote"))
            {
                return new ProcessResult(0, "abc\trefs/heads/main\nabc\trefs/heads/dev/19.0\n", "");
            }

            if (joined.Contains("cat-file") && joined.Contains("origin/main:.gitmodules"))
            {
                return new ProcessResult(1, "", "missing");
            }

            return new ProcessResult(0, "", "");
        });

        var branches = await new UmbrellaSource(process, new ResponseCache(), "C:\\repo")
            .LoadBranchesAsync("chore/pin-*", CancellationToken.None);

        Assert.Equal(["dev/19.0"], branches);
    }

    [Fact]
    public async Task No_branch_with_gitmodules_is_a_clear_message()
    {
        var process = new ScriptedProcess((file, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("ls-remote"))
            {
                return new ProcessResult(0, "abc\trefs/heads/main\n", "");
            }

            if (joined.Contains("cat-file"))
            {
                return new ProcessResult(1, "", "missing");
            }

            return new ProcessResult(0, "", "");
        });

        var exception = await Assert.ThrowsAsync<UserFacingException>(() =>
            new UmbrellaSource(process, new ResponseCache(), "C:\\repo").LoadBranchesAsync("chore/pin-*", CancellationToken.None));

        Assert.Contains(".gitmodules", exception.Message);
    }

    [Fact]
    public async Task Production_tab_refuses_writes()
    {
        var source = new UmbrellaSource(new ScriptedProcess((_, _) => new ProcessResult(0, "", "")), new ResponseCache(), "C:\\repo");
        var controller = new SessionController(source, ["production/19.0", "dev/19.0"], "chore/pin-*");

        controller.Execute(AppCommand.AddModule);
        Assert.Contains("schreibgeschützt", controller.Message);

        controller.Execute(AppCommand.CreatePr);
        Assert.Contains("Promotion-PR", controller.Message);

        controller.Execute(AppCommand.StagePin);
        Assert.Contains("schreibgeschützt", controller.Message);
    }

    [Fact]
    public void Dev_tab_defers_writes()
    {
        var source = new UmbrellaSource(new ScriptedProcess((_, _) => new ProcessResult(0, "", "")), new ResponseCache(), "C:\\repo");
        var controller = new SessionController(source, ["dev/19.0"], "chore/pin-*");

        controller.Execute(AppCommand.StagePin);
        Assert.Contains("Commit im Graph wählen", controller.Message);

        controller.Execute(AppCommand.AddModule);
        Assert.Contains("nächsten Phase", controller.Message);
    }

    [Fact]
    public async Task Enter_stages_a_pin_and_the_current_pin_clears_it()
    {
        const string pin = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string newer = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var process = new ScriptedProcess((file, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("show"))
            {
                return new ProcessResult(0, """
                    [submodule "gb_a"]
                    	path = gb_a
                    	url = https://github.com/Org/gb_a.git
                    	branch = 19.0
                    [submodule "gb_b"]
                    	path = gb_b
                    	url = https://github.com/Org/gb_b.git
                    	branch = 19.0
                    """, "");
            }

            if (joined.Contains("ls-tree"))
            {
                return new ProcessResult(0, $"160000 commit {pin}\tgb_a\n160000 commit {pin}\tgb_b\n", "");
            }

            if (joined.Contains("commits"))
            {
                var commits = "["
                    + "{\"sha\":\"" + pin + "\",\"commit\":{\"message\":\"aktuell\",\"committer\":{\"date\":\"2026-10-01T00:00:00Z\"}}},"
                    + "{\"sha\":\"" + newer + "\",\"commit\":{\"message\":\"danach\",\"committer\":{\"date\":\"2026-10-03T00:00:00Z\"}}}"
                    + "]";
                return new ProcessResult(0, commits, "");
            }

            if (joined.Contains("branches"))
            {
                var branches = "[{\"name\":\"19.0\",\"commit\":{\"sha\":\"" + newer + "\",\"commit\":{\"committer\":{\"date\":\"2026-10-03T00:00:00Z\"}}}}]";
                return new ProcessResult(0, branches, "");
            }

            return new ProcessResult(0, "", "");
        });
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), ["dev/19.0"], "chore/pin-*");
        await controller.LoadCurrentAsync();
        controller.Execute(AppCommand.FocusNext);
        controller.Execute(AppCommand.MoveUp);
        controller.Execute(AppCommand.StagePin);

        Assert.Contains("vorgemerkt: gb_a bbbbbbb", controller.Message);
        Assert.Contains(controller.Graph, row => row.Sha == newer && row.Tone == CommitTone.LightGreen);
        Assert.Contains(controller.Graph, row => row.Sha == pin && row.Tone == CommitTone.DarkGreen);

        controller.Execute(AppCommand.MoveDown);
        controller.Execute(AppCommand.StagePin);
        Assert.Equal("keine vorgemerkten Änderungen", controller.Message);

        controller.Execute(AppCommand.MoveUp);
        controller.Execute(AppCommand.StagePin);
        controller.Execute(AppCommand.FocusNext);
        controller.Execute(AppCommand.MoveDown);
        controller.Execute(AppCommand.FocusNext);
        controller.Execute(AppCommand.MoveUp);
        controller.Execute(AppCommand.StagePin);

        Assert.Contains("gb_a bbbbbbb", controller.Message);
        Assert.Contains("gb_b bbbbbbb", controller.Message);
    }

    [Fact]
    public async Task Push_asks_before_it_creates_a_pull_request()
    {
        const string pin = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string newer = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string tip = "cccccccccccccccccccccccccccccccccccccccc";
        var calls = new List<IReadOnlyList<string>>();
        var process = new ScriptedProcess((file, args) =>
        {
            calls.Add(args);
            var joined = string.Join(' ', args);
            if (joined.Contains("rev-parse"))
            {
                return new ProcessResult(0, tip, "");
            }

            if (joined.Contains("show"))
            {
                return new ProcessResult(0, """
                    [submodule "gb_a"]
                    	path = gb_a
                    	url = https://github.com/Org/gb_a.git
                    	branch = 19.0
                    """, "");
            }

            if (joined.Contains("ls-tree"))
            {
                return new ProcessResult(0, $"160000 commit {pin}\tgb_a\n", "");
            }

            if (joined.Contains("commit-tree"))
            {
                return new ProcessResult(0, "dddddddddddddddddddddddddddddddddddddddd\n", "");
            }

            if (joined.Contains("commits"))
            {
                var commits = "["
                    + "{\"sha\":\"" + pin + "\",\"commit\":{\"message\":\"aktuell\",\"committer\":{\"date\":\"2026-10-01T00:00:00Z\"}}},"
                    + "{\"sha\":\"" + newer + "\",\"commit\":{\"message\":\"danach\",\"committer\":{\"date\":\"2026-10-03T00:00:00Z\"}}}"
                    + "]";
                return new ProcessResult(0, commits, "");
            }

            if (joined.Contains("branches"))
            {
                return new ProcessResult(0, "[{\"name\":\"19.0\",\"commit\":{\"sha\":\"" + newer + "\",\"commit\":{\"committer\":{\"date\":\"2026-10-03T00:00:00Z\"}}}}]", "");
            }

            if (args.Contains("pr"))
            {
                return new ProcessResult(0, "https://example.invalid/pull/1\n", "");
            }

            return new ProcessResult(0, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n", "");
        });
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), ["dev/19.0"], "chore/pin-*");
        await controller.LoadCurrentAsync();
        controller.Execute(AppCommand.CreatePr);
        Assert.Equal("Nichts vorgemerkt.", controller.Message);

        controller.Execute(AppCommand.FocusNext);
        controller.Execute(AppCommand.MoveUp);
        controller.Execute(AppCommand.StagePin);
        controller.Execute(AppCommand.CreatePr);

        Assert.NotNull(controller.PrProposal);
        Assert.Contains(controller.PrProposal, line => line.Contains("Wirklich folgende Änderungen", StringComparison.Ordinal));
        Assert.Contains(controller.PrProposal, line => line.Contains("gb_a", StringComparison.Ordinal) && line.Contains('→'));
        Assert.DoesNotContain(calls, args => args.Contains("push"));

        controller.Execute(AppCommand.CancelPush);
        Assert.Null(controller.PrProposal);
        Assert.Contains("abgebrochen", controller.Message);
        Assert.Contains("vorgemerkt: gb_a", controller.Message);
        Assert.DoesNotContain(calls, args => args.Contains("push"));

        controller.Execute(AppCommand.CreatePr);
        controller.Execute(AppCommand.ConfirmPush);

        Assert.Contains(calls, args => args.Contains("push") && args.Any(argument => argument.Contains("refs/heads/chore/pin-gb_a-", StringComparison.Ordinal)));
        Assert.Contains(calls, args => args.Contains("--base") && args.Contains("dev/19.0") && args.Contains("--head"));
        Assert.DoesNotContain(calls, args => args.Contains("--force") || args.Contains("-f"));
        Assert.Contains(controller.PrProposal!, line => line.Contains("example.invalid/pull/1", StringComparison.Ordinal));
        Assert.Equal("keine vorgemerkten Änderungen", controller.Message);
    }
}

file sealed class ScriptedProcess : IExternalProcess
{
    private readonly Func<string, IReadOnlyList<string>, ProcessResult> _handle;

    public ScriptedProcess(Func<string, IReadOnlyList<string>, ProcessResult> handle)
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
