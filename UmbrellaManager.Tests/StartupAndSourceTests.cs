using UmbrellaManager.Process;
using UmbrellaManager.Services;

namespace UmbrellaManager.Tests;

public class StartupAndSourceTests
{
    [Fact]
    public async Task Missing_git_is_a_clear_message()
    {
        var process = new ScriptedProcess((_, _) => throw new UserFacingException("git wurde nicht im PATH gefunden."));
        var message = await StartupCheck.FindProblemAsync(process, "C:\\repo", _ => true, _ => true);

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

        var message = await StartupCheck.FindProblemAsync(process, "C:\\repo", _ => true, _ => true);

        Assert.Contains("gh auth login", message);
    }

    [Fact]
    public async Task Missing_gitmodules_is_a_clear_message()
    {
        var process = new ScriptedProcess((_, _) => new ProcessResult(0, "true", ""));
        var message = await StartupCheck.FindProblemAsync(process, "C:\\repo", _ => true, _ => false);

        Assert.Contains(".gitmodules", message);
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
    public async Task Production_tab_refuses_writes()
    {
        var source = new UmbrellaSource(new ScriptedProcess((_, _) => new ProcessResult(0, "", "")), new ResponseCache(), "C:\\repo");
        var controller = new SessionController(source, ["production/19.0", "dev/19.0"], "chore/pin-*");

        controller.Execute(AppCommand.AddModule);
        Assert.Contains("schreibgeschützt", controller.Message);

        controller.Execute(AppCommand.CreatePr);
        Assert.Contains("Promotion-PR", controller.Message);
    }

    [Fact]
    public void Dev_tab_defers_writes()
    {
        var source = new UmbrellaSource(new ScriptedProcess((_, _) => new ProcessResult(0, "", "")), new ResponseCache(), "C:\\repo");
        var controller = new SessionController(source, ["dev/19.0"], "chore/pin-*");

        controller.Execute(AppCommand.StagePin);
        Assert.Contains("nächsten Phase", controller.Message);
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
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_handle(fileName, arguments));
    }
}
