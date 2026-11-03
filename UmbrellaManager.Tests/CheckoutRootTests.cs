using UmbrellaManager.Config;
using UmbrellaManager.Process;

namespace UmbrellaManager.Tests;

public class CheckoutRootTests
{
    [Fact]
    public async Task Superproject_is_the_umbrella_root()
    {
        var process = new RootProcess((args, _) =>
            args[1] == "--show-superproject-working-tree"
                ? new ProcessResult(0, "C:/dev/odoo_umbrella\n", "")
                : new ProcessResult(0, "C:/dev/odoo_umbrella/gb_a\n", ""));

        var path = await CheckoutRoot.ResolveAsync(process, "C:/dev/odoo_umbrella/gb_a");

        Assert.Equal(Path.GetFullPath("C:/dev/odoo_umbrella"), path);
        Assert.Equal(["rev-parse", "--show-superproject-working-tree"], process.Calls[0].Arguments);
    }

    [Fact]
    public async Task Toplevel_is_used_when_there_is_no_superproject()
    {
        var process = new RootProcess((args, directory) =>
            args[1] == "--show-superproject-working-tree"
                ? new ProcessResult(0, "\n", "")
                : new ProcessResult(0, directory + "\n", ""));

        var path = await CheckoutRoot.ResolveAsync(process, "C:/dev/odoo_umbrella/addons");

        Assert.Equal(Path.GetFullPath("C:/dev/odoo_umbrella/addons"), path);
        Assert.Equal("C:/dev/odoo_umbrella/addons", process.Calls[1].Directory);
    }

    [Fact]
    public async Task Missing_checkout_returns_null()
    {
        var process = new RootProcess((_, _) => new ProcessResult(128, "", "not a git repository"));

        var path = await CheckoutRoot.ResolveAsync(process, "C:/temp");

        Assert.Null(path);
    }

    [Fact]
    public void Extension_launch_ignores_flags_and_reads_a_path()
    {
        Assert.True(SessionLaunch.IsExtension("1"));
        Assert.False(SessionLaunch.IsExtension(null));
        Assert.Null(SessionLaunch.ArgumentPath(["--help"]));
        Assert.Equal(Path.GetFullPath("C:/dev/odoo_umbrella"), SessionLaunch.ArgumentPath(["--help", "C:/dev/odoo_umbrella"]));
    }

    private sealed class RootProcess : IExternalProcess
    {
        private readonly Func<IReadOnlyList<string>, string?, ProcessResult> _handle;

        public RootProcess(Func<IReadOnlyList<string>, string?, ProcessResult> handle)
        {
            _handle = handle;
        }

        public List<(IReadOnlyList<string> Arguments, string? Directory)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            Calls.Add((arguments, workingDirectory));
            return Task.FromResult(_handle(arguments, workingDirectory));
        }
    }
}
