using UmbrellaManager.Config;
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
    public void Unrelated_commits_are_not_reported_as_a_hidden_repo()
    {
        const string body = """
            {"message":"No common ancestor between a and b.","status":"404"}
            """;
        var message = GitHub.GitHubText.Describe(body, "gh: No common ancestor between a and b. (HTTP 404)");
        Assert.Equal(GitHub.GitHubText.Unrelated, message);
        Assert.DoesNotContain("SSO", message);
    }

    [Fact]
    public async Task Missing_manifest_has_no_version_and_rate_limit_stays_visible()
    {
        var missing = new UmbrellaSource(
            new ScriptedProcess((_, _) => new ProcessResult(1, "", "HTTP 404: Not Found")),
            new ResponseCache(),
            "C:\\repo");
        var version = await missing.ModuleVersionAsync(
            "https://github.com/Org/ratocon_modules",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CancellationToken.None);
        Assert.Null(version);

        var limited = new UmbrellaSource(
            new ScriptedProcess((_, _) => new ProcessResult(1, "", "API rate limit exceeded")),
            new ResponseCache(),
            "C:\\repo");
        var error = await Assert.ThrowsAsync<UserFacingException>(() => limited.ModuleVersionAsync(
            "https://github.com/Org/gb_a",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CancellationToken.None));
        Assert.Equal(GitHub.GitHubText.Offline, error.Message);
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
        Assert.Contains("Nichts vorgemerkt", controller.Message);

        controller.Execute(AppCommand.StagePin);
        Assert.Contains("Modul wählen", controller.Message);
    }

    [Fact]
    public async Task Production_enter_stages_only_a_newer_staging_pin()
    {
        var production = new string('a', 40);
        var staging = new string('b', 40);
        var older = new string('c', 40);
        var foreign = new string('d', 40);
        var tip = new string('e', 40);
        var zeros = new string('0', 40);
        var calls = new List<IReadOnlyList<string>>();
        var process = new ScriptedProcess((file, args) =>
        {
            calls.Add(args);
            var joined = string.Join(' ', args);
            if (args.Contains("log"))
            {
                return new ProcessResult(0, $":160000 160000 {zeros} {older} A\tgb_a\n:160000 160000 {older} {staging} M\tgb_a\n", "");
            }

            if (args.Contains("rev-parse") && joined.Contains(':'))
            {
                return new ProcessResult(0, staging + "\n", "");
            }

            if (args.Contains("rev-parse"))
            {
                return new ProcessResult(0, tip + "\n", "");
            }

            if (args.Contains("ls-tree"))
            {
                return new ProcessResult(0, $"160000 commit {production}\tgb_a\n", "");
            }

            if (args.Contains("show"))
            {
                return new ProcessResult(0, """
                    [submodule "gb_a"]
                    	path = gb_a
                    	url = https://github.com/Org/gb_a.git
                    	branch = 19.0
                    """, "");
            }

            if (joined.Contains("compare/"))
            {
                if (joined.Contains(staging))
                {
                    return new ProcessResult(0, "{\"status\":\"ahead\"}", "");
                }

                if (joined.Contains(older))
                {
                    return new ProcessResult(0, "{\"status\":\"behind\"}", "");
                }

                if (joined.Contains(foreign))
                {
                    return new ProcessResult(0, "{\"status\":\"ahead\"}", "");
                }

                return new ProcessResult(0, "{\"status\":\"ahead\"}", "");
            }

            if (joined.Contains("commits"))
            {
                var commits = "["
                    + Commit(production, "production", "2026-10-01T00:00:00Z") + ","
                    + Commit(older, "früher", "2026-10-02T00:00:00Z") + ","
                    + Commit(staging, "staging", "2026-10-03T00:00:00Z") + ","
                    + Commit(foreign, "fremd", "2026-10-04T00:00:00Z")
                    + "]";
                return new ProcessResult(0, commits, "");
            }

            if (joined.Contains("graphql"))
            {
                return new ProcessResult(0, Heads("19.0", foreign, "2026-10-04T00:00:00Z"), "");
            }

            if (joined.Contains("contents"))
            {
                return new ProcessResult(1, "", "HTTP 404: Not Found");
            }

            return new ProcessResult(0, "", "");
        });
        var controller = new SessionController(
            new UmbrellaSource(process, new ResponseCache(), "C:\\repo"),
            ["production/19.0", "staging/19.0"],
            "chore/pin-*");
        await controller.LoadCurrentAsync();

        Assert.Equal(PinMark.Production, controller.Graph.Single(row => row.Sha == production).Mark);
        Assert.Equal(PinMark.Staging, controller.Graph.Single(row => row.Sha == staging).Mark);
        Assert.True(controller.Graph.Single(row => row.Sha == staging).Promotable);
        Assert.False(controller.Graph.Single(row => row.Sha == older).Promotable);
        Assert.False(controller.Graph.Single(row => row.Sha == foreign).Promotable);
        Assert.Contains(calls, args => args.Contains("log") && args.Contains("origin/staging/19.0") && args.Contains("--raw"));
        Assert.Contains(calls, args => string.Join(' ', args).Contains("compare/") && string.Join(' ', args).Contains(staging));
        Assert.DoesNotContain(calls, args => string.Join(' ', args).Contains("compare/") && string.Join(' ', args).Contains(foreign));

        controller.Execute(AppCommand.StagePin);
        Assert.Equal(staging, controller.StagedPin("gb_a"));
        Assert.Equal(staging, controller.Graph[controller.GraphIndex].Sha);
        Assert.Contains(controller.Graph, row => row.Sha == staging && row.Tone == CommitTone.LightGreen);

        controller.Execute(AppCommand.MoveUp);
        controller.Execute(AppCommand.StagePin);
        Assert.Equal(staging, controller.StagedPin("gb_a"));
        Assert.Equal(staging, controller.Graph[controller.GraphIndex].Sha);

        controller.Execute(AppCommand.StagePin);
        Assert.Null(controller.StagedPin("gb_a"));

        controller.Execute(AppCommand.MoveDown);
        controller.Execute(AppCommand.StagePin);
        Assert.Equal(staging, controller.StagedPin("gb_a"));
        Assert.NotEqual(older, controller.StagedPin("gb_a"));

        controller.Execute(AppCommand.ChooseNewer);
        Assert.Contains("schon auf Staging", controller.Message);
        Assert.Equal(staging, controller.StagedPin("gb_a"));

        controller.Execute(AppCommand.AddModule);
        Assert.Contains("schreibgeschützt", controller.Message);

        controller.Execute(AppCommand.CreatePr);
        Assert.NotNull(controller.PrProposal);
        Assert.Contains(controller.PrProposal, line => line.Contains("gb_a", StringComparison.Ordinal) && line.Contains('→'));
        Assert.Contains(controller.PrProposal, line => line.Contains("production/19.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Production_graph_stays_when_pins_have_no_common_ancestor()
    {
        var production = new string('a', 40);
        var staging = new string('b', 40);
        var process = new ScriptedProcess((_, args) =>
        {
            var joined = string.Join(' ', args);
            if (args.Contains("log"))
            {
                return new ProcessResult(0, $":160000 160000 {new string('0', 40)} {staging} A\tgb_a\n", "");
            }

            if (args.Contains("rev-parse") && joined.Contains(':'))
            {
                return new ProcessResult(0, staging + "\n", "");
            }

            if (args.Contains("rev-parse"))
            {
                return new ProcessResult(0, production + "\n", "");
            }

            if (args.Contains("ls-tree"))
            {
                return new ProcessResult(0, $"160000 commit {production}\tgb_a\n", "");
            }

            if (args.Contains("show"))
            {
                return new ProcessResult(0, """
                    [submodule "gb_a"]
                    	path = gb_a
                    	url = https://github.com/Org/gb_a.git
                    	branch = 19.0
                    """, "");
            }

            if (joined.Contains("compare/"))
            {
                return new ProcessResult(
                    1,
                    "{\"message\":\"No common ancestor between a and b.\",\"status\":\"404\"}",
                    "gh: No common ancestor between a and b. (HTTP 404)");
            }

            if (joined.Contains("commits"))
            {
                return new ProcessResult(0, "["
                    + Commit(production, "production", "2026-10-01T00:00:00Z") + ","
                    + Commit(staging, "staging", "2026-10-03T00:00:00Z")
                    + "]", "");
            }

            if (joined.Contains("graphql"))
            {
                return new ProcessResult(0, Heads("19.0", production, "2026-10-01T00:00:00Z"), "");
            }

            return new ProcessResult(0, "", "");
        });
        var controller = new SessionController(
            new UmbrellaSource(process, new ResponseCache(), "C:\\repo"),
            ["production/19.0", "staging/19.0"],
            "chore/pin-*");
        await controller.LoadCurrentAsync();

        Assert.DoesNotContain("SSO", controller.Message);
        Assert.DoesNotContain("nicht sichtbar", controller.GraphMessage);
        Assert.Contains(controller.Graph, row => row.Sha == production && row.Mark == PinMark.Production);
        var staged = controller.Graph.Single(row => row.Sha == staging);
        Assert.Equal(PinMark.Staging, staged.Mark);
        Assert.False(staged.Promotable);

        controller.Execute(AppCommand.FocusGraph);
        controller.Execute(AppCommand.StagePin);
        Assert.Contains("verzweigt", controller.Message);
        Assert.Null(controller.StagedPin("gb_a"));
    }

    private static string Commit(string sha, string subject, string date)
    {
        return "{\"sha\":\"" + sha + "\",\"commit\":{\"message\":\"" + subject + "\",\"committer\":{\"date\":\"" + date + "\"}}}";
    }

    [Fact]
    public async Task Help_shows_login_and_primary_email_and_logout_waits_for_confirmation()
    {
        var calls = new List<string>();
        var process = new ScriptedProcess((_, args) =>
        {
            var joined = string.Join(' ', args);
            calls.Add(joined);
            if (joined.Contains("user/emails"))
            {
                return new ProcessResult(0, """[{"email":"andere@example.com","primary":false},{"email":"ich@example.com","primary":true}]""", "");
            }

            if (args.Contains("user"))
            {
                return new ProcessResult(0, """{"login":"fbehmenb","email":null}""", "");
            }

            if (args.Contains("logout"))
            {
                return new ProcessResult(0, "", "");
            }

            return new ProcessResult(0, "", "");
        });
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), ["dev/19.0"], "chore/pin-*");

        controller.Execute(AppCommand.Logout);
        Assert.Contains("Hilfe", controller.Message);
        Assert.DoesNotContain(calls, call => call.Contains("logout"));

        controller.Execute(AppCommand.ShowHelp);
        await controller.AccountTask;
        Assert.Equal(["GitHub", "fbehmenb", "ich@example.com", ""], controller.AccountLines);

        controller.Execute(AppCommand.Logout);
        Assert.True(controller.LogoutArmed);
        Assert.True(controller.HelpVisible);
        Assert.Contains("j meldet ab", string.Join('\n', controller.AccountLines));
        Assert.DoesNotContain(calls, call => call.Contains("logout"));

        controller.Execute(AppCommand.CancelPush);
        Assert.False(controller.LogoutArmed);
        Assert.True(controller.HelpVisible);

        controller.Execute(AppCommand.Logout);
        controller.Execute(AppCommand.ConfirmPush);
        await controller.AccountTask;
        Assert.Contains(calls, call => call.Contains("auth logout") && call.Contains("--hostname github.com") && call.Contains("--user fbehmenb"));
        Assert.Contains("abgemeldet", controller.Message);
        Assert.Equal(["GitHub", "Abgemeldet.", ""], controller.AccountLines);
        Assert.All(calls, call => Assert.DoesNotContain("ghp_", call));
    }

    [Fact]
    public async Task Help_names_the_missing_user_scope_when_the_email_stays_hidden()
    {
        var process = new ScriptedProcess((_, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("user/emails"))
            {
                return new ProcessResult(1, "{\"message\":\"Not Found\",\"status\":\"404\"}", "This API operation needs the \"user\" scope.");
            }

            if (args.Contains("user"))
            {
                return new ProcessResult(0, """{"login":"fbehmenb","email":null}""", "");
            }

            return new ProcessResult(0, "", "");
        });
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), ["dev/19.0"], "chore/pin-*");
        controller.Execute(AppCommand.ShowHelp);
        await controller.AccountTask;

        Assert.Contains("fbehmenb", controller.AccountLines);
        Assert.Contains(controller.AccountLines, line => line.Contains("Bereich user", StringComparison.Ordinal));
        Assert.DoesNotContain(controller.AccountLines, line => line.Contains("SSO", StringComparison.Ordinal));
    }

    private static string Heads(string name, string sha, string date)
    {
        return "{\"data\":{\"repository\":{\"refs\":{\"nodes\":[{\"name\":\""
            + name
            + "\",\"target\":{\"oid\":\""
            + sha
            + "\",\"committedDate\":\""
            + date
            + "\"}}]}}}}";
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

            if (joined.Contains("graphql"))
            {
                return new ProcessResult(0, Heads("19.0", newer, "2026-10-03T00:00:00Z"), "");
            }

            return new ProcessResult(0, "", "");
        });
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), ["dev/19.0"], "chore/pin-*");
        await controller.LoadCurrentAsync();
        controller.Execute(AppCommand.FocusNext);
        controller.Execute(AppCommand.MoveUp);
        controller.Execute(AppCommand.StagePin);

        Assert.Contains("vorgemerkt: gb_a bbbbbbb", controller.Message);
        controller.Execute(AppCommand.RemoveModule);
        Assert.Equal("keine vorgemerkten Änderungen", controller.Message);
        controller.Execute(AppCommand.ChooseNewer);
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
        SessionController? live = null;
        string? checkMessage = null;
        var process = new ScriptedProcess((file, args) =>
        {
            calls.Add(args);
            var joined = string.Join(' ', args);
            if (joined.Contains("fetch") && live?.CheckingRemote == true)
            {
                checkMessage = live.PrProposal?.FirstOrDefault();
            }
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

            if (joined.Contains("graphql"))
            {
                return new ProcessResult(0, Heads("19.0", newer, "2026-10-03T00:00:00Z"), "");
            }

            if (args.Contains("pr"))
            {
                return new ProcessResult(0, "https://example.invalid/pull/1\n", "");
            }

            return new ProcessResult(0, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n", "");
        });
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), ["dev/19.0"], "chore/pin-*");
        live = controller;
        await controller.LoadCurrentAsync();
        controller.Execute(AppCommand.CreatePr);
        Assert.Equal("Nichts vorgemerkt.", controller.Message);

        controller.Execute(AppCommand.FocusNext);
        controller.Execute(AppCommand.MoveUp);
        controller.Execute(AppCommand.StagePin);
        controller.Execute(AppCommand.CreatePr);

        Assert.Contains("Repo-Stand", checkMessage ?? "");
        Assert.NotNull(controller.PrProposal);
        Assert.Contains(controller.PrProposal, line => line.Contains("gb_a", StringComparison.Ordinal) && line.Contains('→'));
        Assert.Contains(controller.PrProposal, line => line.Contains("j pushen", StringComparison.Ordinal) && line.Contains("n abbrechen", StringComparison.Ordinal));
        Assert.False(controller.PushRunning);
        Assert.Equal(0, controller.PushDone);
        Assert.DoesNotContain(calls, args => args.Contains("push"));
        controller.Execute(AppCommand.ShowHelp);
        Assert.True(controller.HelpVisible);
        Assert.NotNull(controller.PrProposal);
        controller.Execute(AppCommand.ShowHelp);
        Assert.False(controller.HelpVisible);
        Assert.NotNull(controller.PrProposal);

        controller.Execute(AppCommand.CancelPush);
        Assert.Null(controller.PrProposal);
        Assert.Contains("abgebrochen", controller.Message);
        Assert.Contains("vorgemerkt: gb_a", controller.Message);
        Assert.DoesNotContain(calls, args => args.Contains("push"));

        controller.SuccessHold = TimeSpan.FromMilliseconds(40);
        controller.Execute(AppCommand.CreatePr);
        controller.Execute(AppCommand.ConfirmPush);

        Assert.Contains(calls, args => args.Contains("push") && args.Any(argument => argument.Contains("refs/heads/chore/pin-gb_a-", StringComparison.Ordinal)));
        Assert.Contains(calls, args => args.Contains("--base") && args.Contains("dev/19.0") && args.Contains("--head"));
        Assert.DoesNotContain(calls, args => args.Contains("--force") || args.Contains("-f"));
        Assert.NotNull(controller.PrProposal);
        Assert.Contains(controller.PrProposal, line => line.Contains("Erfolgreich", StringComparison.Ordinal));
        Assert.Contains(controller.PrProposal, line => line.Contains("PR 1", StringComparison.Ordinal));
        Assert.Contains(controller.PrProposal, line => line.Contains("Taste schließt", StringComparison.Ordinal));
        Assert.InRange(controller.SuccessFraction, 0, 0.95);
        Assert.Contains("Erfolgreich", controller.Message);
        Assert.False(controller.PushRunning);
        await Task.Delay(200);
        Assert.Null(controller.PrProposal);
        Assert.DoesNotContain("Erfolgreich", controller.Message);

        controller.SuccessHold = TimeSpan.FromSeconds(30);
        controller.Execute(AppCommand.StagePin);
        controller.Execute(AppCommand.CreatePr);
        controller.Execute(AppCommand.ConfirmPush);
        Assert.Contains(controller.PrProposal!, line => line.Contains("Erfolgreich", StringComparison.Ordinal));
        Assert.Contains(controller.PrProposal!, line => line.Contains("Taste schließt", StringComparison.Ordinal));
        controller.Execute(AppCommand.MoveDown);
        Assert.Null(controller.PrProposal);
        Assert.DoesNotContain("Erfolgreich", controller.Message);
    }

    [Fact]
    public void Setup_saves_the_interval_and_a_failed_check_keeps_the_mark()
    {
        var directory = Path.Combine(Path.GetTempPath(), "umbrella-manager-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "setup.json");
        var local = Path.Combine(directory, "odoo");
        new UserSetup("https://github.com/Org/odoo_umbrella", local).Save(file);
        var controller = new SessionController(
            new UmbrellaSource(new ScriptedProcess((_, _) => new ProcessResult(0, "", "")), new ResponseCache(), directory),
            ["dev/19.0"],
            "chore/pin-*");
        try
        {
            controller.UseSetup(file, null);
            controller.Execute(AppCommand.OpenSetup);
            Assert.True(controller.SetupOpen);
            Assert.Contains(controller.SetupLines, line => line.StartsWith(">  2 Minuten", StringComparison.Ordinal));
            controller.Execute(AppCommand.MoveDown);
            controller.Execute(AppCommand.StagePin);
            var saved = UserSetup.Load(file, out var warning);
            Assert.Null(warning);
            Assert.Equal(300, saved!.PollIntervalSeconds);
            Assert.Equal("https://github.com/Org/odoo_umbrella", saved.RepositoryUrl);
            Assert.Equal(local, saved.LocalPath);
            Assert.False(controller.SetupOpen);

            const string oldTip = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            const string newTip = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            controller.RememberTips(new Dictionary<string, string> { ["dev/19.0"] = oldTip });
            controller.NoteRemote(new Dictionary<string, string>
            {
                ["dev/19.0"] = newTip,
                ["topic/x"] = oldTip,
            });
            Assert.True(controller.IsStale("dev/19.0"));
            Assert.Contains("topic/x", controller.Message);
            Assert.Contains("u holt den Stand", controller.Message);
            controller.NoteRemote(null);
            Assert.True(controller.IsStale("dev/19.0"));
            Assert.Contains("fehlgeschlagen", controller.Message);
        }
        finally
        {
            controller.Cancel();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Reload_keeps_the_selected_submodule()
    {
        var controller = TreeController(dropSelected: false);
        await controller.LoadCurrentAsync();
        controller.Execute(AppCommand.MoveDown);

        Assert.Equal("gb_b", controller.Selected?.Path);
        controller.Execute(AppCommand.ReloadAll);

        Assert.Equal("gb_b", controller.Selected?.Path);
        Assert.Equal(NextPin, controller.Selected?.Sha);
        Assert.False(controller.Loading);
    }

    [Fact]
    public async Task Reload_selects_the_top_submodule_when_the_previous_one_is_gone()
    {
        var controller = TreeController(dropSelected: true);
        await controller.LoadCurrentAsync();
        controller.Execute(AppCommand.MoveDown);
        controller.Execute(AppCommand.ReloadAll);

        Assert.Equal("aaa/gb_c", controller.Selected?.Path);
        Assert.False(controller.Selected?.IsFolder);
        Assert.False(controller.Loading);
    }

    [Fact]
    public async Task Reload_refreshes_the_open_module_without_the_umbrella()
    {
        var shows = 0;
        var commits = 0;
        var controller = TreeController(dropSelected: false, () => shows++, () => commits++);
        await controller.LoadCurrentAsync();
        controller.Execute(AppCommand.MoveDown);
        var showsAfterOpen = shows;
        var commitsAfterOpen = commits;

        controller.Execute(AppCommand.Reload);

        Assert.Equal("gb_b", controller.Selected?.Path);
        Assert.Equal(Pin, controller.Selected?.Sha);
        Assert.Equal(showsAfterOpen, shows);
        Assert.True(commits > commitsAfterOpen);
        Assert.False(controller.Loading);
    }

    [Fact]
    public async Task Page_keys_step_through_modules_and_leave_commit_navigation_in_place()
    {
        var process = new ScriptedProcess((_, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("show"))
            {
                return new ProcessResult(0, Modules("aaa/gb_c") + Modules("gb_a"), "");
            }

            if (joined.Contains("ls-tree"))
            {
                return new ProcessResult(0, Link("aaa/gb_c", Pin) + Link("gb_a", Pin), "");
            }

            if (joined.Contains("commits"))
            {
                return new ProcessResult(
                    0,
                    "[" + Commit(Pin, "aktuell", "2026-10-01T00:00:00Z") + "," + Commit(NextPin, "danach", "2026-10-02T00:00:00Z") + "]",
                    "");
            }

            if (joined.Contains("graphql"))
            {
                return new ProcessResult(0, Heads("19.0", Pin, "2026-10-01T00:00:00Z"), "");
            }

            if (joined.Contains("rev-parse") && joined.Contains("origin/dev/19.0") && !joined.Contains(':'))
            {
                return new ProcessResult(0, Pin + "\n", "");
            }

            if (joined.Contains("get-url"))
            {
                return new ProcessResult(0, "https://github.com/Org/odoo_umbrella\n", "");
            }

            return new ProcessResult(0, "", "");
        });
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), ["dev/19.0"], "chore/pin-*");
        await controller.LoadCurrentAsync();

        Assert.Equal("aaa/gb_c", controller.Selected?.Path);
        controller.Execute(AppCommand.FocusTree);
        controller.Execute(AppCommand.MoveUp);
        Assert.True(controller.Selected?.IsFolder);
        controller.Execute(AppCommand.FocusGraph);
        controller.Execute(AppCommand.NextModule);

        Assert.Equal("aaa/gb_c", controller.Selected?.Path);
        Assert.Equal(FocusPane.Graph, controller.Focus);
        controller.Execute(AppCommand.NextModule);
        Assert.Equal("gb_a", controller.Selected?.Path);
        Assert.Equal(FocusPane.Graph, controller.Focus);

        var commit = controller.GraphIndex;
        controller.Execute(AppCommand.MoveUp);
        Assert.Equal("gb_a", controller.Selected?.Path);
        Assert.NotEqual(commit, controller.GraphIndex);

        controller.Execute(AppCommand.PreviousModule);
        Assert.Equal("aaa/gb_c", controller.Selected?.Path);
        Assert.Equal(FocusPane.Graph, controller.Focus);
    }

    [Fact]
    public async Task Default_branch_is_shown_when_it_matches_the_tracked_tip()
    {
        const string sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var process = new ScriptedProcess((_, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("graphql"))
            {
                return new ProcessResult(0, """
                    {"data":{"repository":{"refs":{"nodes":[
                      {"name":"19.0","target":{"oid":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","committedDate":"2026-10-21T17:53:49Z"}},
                      {"name":"20.0","target":{"oid":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","committedDate":"2026-10-21T17:53:49Z"}}
                    ]}}}}
                    """, "");
            }

            if (joined.Contains("commits"))
            {
                return new ProcessResult(0, "[" + Commit(sha, "spitze", "2026-10-21T17:53:49Z") + "]", "");
            }

            if (args.Contains("repos/Org/gb_activity_actions"))
            {
                return new ProcessResult(0, "{\"default_branch\":\"20.0\"}", "");
            }

            return new ProcessResult(0, "", "");
        });
        var source = new UmbrellaSource(process, new ResponseCache(), "C:\\repo");
        var module = new SubmoduleEntry(
            "gb_modules/gb_activity_actions",
            sha,
            "https://github.com/Org/gb_activity_actions.git",
            "19.0");

        var snapshot = await source.LoadGraphAsync(module, CancellationToken.None);

        Assert.Equal(["19.0", "20.0"], snapshot.Lanes);
        Assert.Contains(snapshot.Rows, row => row.Names is not null && row.Names.Contains("20.0"));
    }

    [Fact]
    public async Task Branch_column_shows_the_tip_that_holds_the_pin()
    {
        const string pin = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string older = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var process = new ScriptedProcess((_, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("show"))
            {
                return new ProcessResult(0, """
                    [submodule "thirdparty_modules"]
                    	path = thirdparty_modules
                    	url = https://github.com/Org/thirdparty_modules.git
                    	branch = 19.0
                    """, "");
            }

            if (joined.Contains("ls-tree"))
            {
                return new ProcessResult(0, "160000 commit " + pin + "\tthirdparty_modules\n", "");
            }

            if (joined.Contains("graphql"))
            {
                return new ProcessResult(0, "{\"data\":{\"repository\":{\"refs\":{\"nodes\":["
                    + "{\"name\":\"19.0\",\"target\":{\"oid\":\"" + older + "\",\"committedDate\":\"2026-10-01T00:00:00Z\"}},"
                    + "{\"name\":\"20.0\",\"target\":{\"oid\":\"" + pin + "\",\"committedDate\":\"2026-10-02T00:00:00Z\"}}"
                    + "]}}}}", "");
            }

            if (joined.Contains("compare"))
            {
                var status = joined.Contains("...20.0") ? "identical" : "diverged";
                return new ProcessResult(0, "{\"status\":\"" + status + "\"}", "");
            }

            if (joined.Contains("commits") && joined.Contains("sha=20.0"))
            {
                return new ProcessResult(0, "[" + Commit(pin, "vendor", "2026-10-02T00:00:00Z") + "]", "");
            }

            if (joined.Contains("commits"))
            {
                return new ProcessResult(0, "[" + Commit(older, "alt", "2026-10-01T00:00:00Z") + "]", "");
            }

            if (joined.Contains("rev-parse"))
            {
                return new ProcessResult(0, older + "\n", "");
            }

            if (joined.Contains("get-url"))
            {
                return new ProcessResult(0, "https://github.com/Org/odoo_umbrella\n", "");
            }

            if (joined.Contains("repos/Org/thirdparty_modules"))
            {
                return new ProcessResult(0, "{\"default_branch\":\"19.0\"}", "");
            }

            return new ProcessResult(0, "", "");
        });
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), ["dev/20.0"], "chore/pin-*");
        await controller.LoadCurrentAsync();

        var row = Assert.Single(controller.Rows, item => item.Name == "thirdparty_modules");
        Assert.Equal("19.0", row.Branch);
        Assert.Equal("20.0", controller.DisplayedBranch(row));
        Assert.Contains(controller.Graph, item => item.Tone == CommitTone.DarkGreen && item.Names is not null && item.Names.Contains("20.0"));
    }

    [Fact]
    public async Task Startup_shows_changing_loading_steps_and_ignores_navigation()
    {
        SessionController? current = null;
        var moved = false;
        var process = new ScriptedProcess((_, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("commits") && current is { Loading: true } && !moved)
            {
                moved = true;
                current.Execute(AppCommand.MoveDown);
                current.Execute(AppCommand.ShowHelp);
            }

            if (joined.Contains("ls-remote"))
            {
                return new ProcessResult(0, Pin + "\trefs/heads/dev/19.0\n", "");
            }

            if (joined.Contains("show"))
            {
                return new ProcessResult(0, Modules("gb_a") + Modules("gb_b"), "");
            }

            if (joined.Contains("ls-tree"))
            {
                return new ProcessResult(0, Link("gb_a", Pin) + Link("gb_b", Pin), "");
            }

            if (joined.Contains("commits"))
            {
                return new ProcessResult(0, "[" + Commit(Pin, "aktuell", "2026-10-01T00:00:00Z") + "]", "");
            }

            if (joined.Contains("graphql"))
            {
                return new ProcessResult(0, Heads("19.0", Pin, "2026-10-01T00:00:00Z"), "");
            }

            if (joined.Contains("rev-parse") && joined.Contains("origin/dev/19.0") && !joined.Contains(':'))
            {
                return new ProcessResult(0, Pin + "\n", "");
            }

            if (joined.Contains("get-url"))
            {
                return new ProcessResult(0, "https://github.com/Org/odoo_umbrella\n", "");
            }

            return new ProcessResult(0, "", "");
        });
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), [], "chore/pin-*");
        current = controller;
        var messages = new List<string>();
        controller.Changed += () =>
        {
            if (controller.Loading && controller.LoadingMessage.Length > 0)
            {
                messages.Add(controller.LoadingMessage);
            }
        };

        await controller.StartupAsync();

        Assert.False(controller.Loading);
        Assert.False(controller.LoadingFailed);
        Assert.False(controller.HelpVisible);
        Assert.Equal("gb_a", controller.Selected?.Path);
        Assert.True(controller.LoadingDone >= 2);
        Assert.Contains(messages, message => message.Contains("Remote-Branches", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains(".gitmodules", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("Commits", StringComparison.Ordinal));
        Assert.True(messages.Distinct().Count() >= 3);
    }

    [Fact]
    public async Task Startup_keeps_the_loading_screen_when_no_branch_exists()
    {
        var process = new ScriptedProcess((_, _) => new ProcessResult(0, "", ""));
        var controller = new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), [], "chore/pin-*");

        await controller.StartupAsync();

        Assert.True(controller.Loading);
        Assert.True(controller.LoadingFailed);
        Assert.True(controller.StartupFailed);
        Assert.Contains("Keine Remote-Branches", controller.LoadingMessage);
        controller.Execute(AppCommand.ReloadAll);
        controller.Execute(AppCommand.MoveDown);
        Assert.True(controller.LoadingFailed);
    }

    private const string Pin = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string NextPin = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static SessionController TreeController(bool dropSelected, Action? onShow = null, Action? onCommits = null)
    {
        var generation = 0;
        var process = new ScriptedProcess((_, args) =>
        {
            var joined = string.Join(' ', args);
            if (joined.Contains("ls-remote"))
            {
                return new ProcessResult(0, Pin + "\trefs/heads/dev/19.0\n", "");
            }

            if (joined.Contains("show"))
            {
                onShow?.Invoke();
                generation++;
                var reload = generation > 1;
                if (reload && dropSelected)
                {
                    return new ProcessResult(0, Modules("aaa/gb_c") + Modules("gb_a"), "");
                }

                return new ProcessResult(0, Modules("gb_a") + Modules("gb_b"), "");
            }

            if (joined.Contains("ls-tree"))
            {
                var reload = generation > 1;
                if (reload && dropSelected)
                {
                    return new ProcessResult(0, Link("aaa/gb_c", Pin) + Link("gb_a", Pin), "");
                }

                var sha = reload ? NextPin : Pin;
                return new ProcessResult(0, Link("gb_a", sha) + Link("gb_b", sha), "");
            }

            if (joined.Contains("commits"))
            {
                onCommits?.Invoke();
                return new ProcessResult(
                    0,
                    "[" + Commit(Pin, "aktuell", "2026-10-01T00:00:00Z") + "," + Commit(NextPin, "danach", "2026-10-02T00:00:00Z") + "]",
                    "");
            }

            if (joined.Contains("graphql"))
            {
                return new ProcessResult(0, Heads("19.0", Pin, "2026-10-01T00:00:00Z"), "");
            }

            if (joined.Contains("rev-parse") && joined.Contains("origin/dev/19.0") && !joined.Contains(':'))
            {
                return new ProcessResult(0, Pin + "\n", "");
            }

            if (joined.Contains("get-url"))
            {
                return new ProcessResult(0, "https://github.com/Org/odoo_umbrella\n", "");
            }

            return new ProcessResult(0, "", "");
        });
        return new SessionController(new UmbrellaSource(process, new ResponseCache(), "C:\\repo"), ["dev/19.0"], "chore/pin-*");
    }

    private static string Modules(string path)
    {
        return "[submodule \"" + path + "\"]\n"
            + "\tpath = " + path + "\n"
            + "\turl = https://github.com/Org/" + path.Replace('/', '_') + ".git\n"
            + "\tbranch = 19.0\n";
    }

    private static string Link(string path, string sha)
    {
        return "160000 commit " + sha + "\t" + path + "\n";
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
