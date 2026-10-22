using UmbrellaManager.Config;
using UmbrellaManager.Process;

namespace UmbrellaManager.Tests;

public class FirstRunTests
{
    [Fact]
    public async Task Second_start_does_not_ask_or_clone()
    {
        var directory = Temp();
        var setupFile = Path.Combine(directory, "setup.json");
        var local = Path.Combine(directory, "odoo_umbrella");
        Directory.CreateDirectory(local);
        new UserSetup("https://github.com/Org/odoo_umbrella", local).Save(setupFile);
        var process = new RecordingProcess((_, _) => new ProcessResult(0, "", ""));

        var path = await FirstRun.EnsureAsync(
            setupFile,
            null,
            new StringReader(""),
            new StringWriter(),
            process,
            Directory.Exists,
            File.Exists,
            _ => false);

        Assert.Equal(Path.GetFullPath(local), path);
        Assert.Empty(process.Calls);
    }

    [Fact]
    public async Task First_start_clones_the_given_url_into_the_given_folder()
    {
        var directory = Temp();
        var setupFile = Path.Combine(directory, "setup.json");
        var local = Path.Combine(directory, "odoo_umbrella");
        var process = new RecordingProcess((file, args) =>
            file == "git" && args.Count > 0 && args[0] is "clone" or "remote"
                ? new ProcessResult(0, "https://github.com/Org/odoo_umbrella.git\n", "")
                : new ProcessResult(1, "", "unerwartet"));
        var output = new StringWriter();
        var input = new StringReader($"https://github.com/Org/odoo_umbrella.git\n{local}\n");

        var path = await FirstRun.EnsureAsync(
            setupFile,
            null,
            input,
            output,
            process,
            Directory.Exists,
            _ => true,
            _ => false);

        Assert.Equal(Path.GetFullPath(local), path);
        Assert.Equal(["clone", "https://github.com/Org/odoo_umbrella.git", Path.GetFullPath(local)], process.Calls[0].Arguments);
        var saved = UserSetup.Load(setupFile, out var warning);
        Assert.Null(warning);
        Assert.Equal("https://github.com/Org/odoo_umbrella.git", saved!.RepositoryUrl);
    }

    [Fact]
    public async Task Existing_clone_with_the_same_remote_is_kept()
    {
        var directory = Temp();
        var local = Path.Combine(directory, "odoo_umbrella");
        Directory.CreateDirectory(Path.Combine(local, ".git"));
        var process = new RecordingProcess((_, _) => new ProcessResult(0, "https://github.com/Org/odoo_umbrella.git\n", ""));

        var path = await FirstRun.EnsureAsync(
            Path.Combine(directory, "setup.json"),
            null,
            new StringReader($"https://github.com/Org/odoo_umbrella\n{local}\n"),
            new StringWriter(),
            process,
            Directory.Exists,
            File.Exists,
            _ => true);

        Assert.Equal(Path.GetFullPath(local), path);
        Assert.DoesNotContain(process.Calls, call => call.Arguments.Contains("clone"));
    }

    [Fact]
    public async Task Other_remote_is_refused()
    {
        var directory = Temp();
        var local = Path.Combine(directory, "odoo_umbrella");
        Directory.CreateDirectory(Path.Combine(local, ".git"));
        File.WriteAllText(Path.Combine(local, ".gitmodules"), "");
        var setupFile = Path.Combine(directory, "setup.json");
        var output = new StringWriter();
        var process = new RecordingProcess((_, _) => new ProcessResult(0, "https://github.com/Org/anderes-repo.git\n", ""));

        var path = await FirstRun.EnsureAsync(
            setupFile,
            null,
            new StringReader($"https://github.com/Org/odoo_umbrella\n{local}\n"),
            output,
            process,
            Directory.Exists,
            File.Exists,
            _ => true);

        Assert.Null(path);
        Assert.Contains("anderes Remote", output.ToString());
        Assert.False(File.Exists(setupFile));
    }

    [Fact]
    public async Task Invalid_url_is_asked_again()
    {
        var directory = Temp();
        var local = Path.Combine(directory, "odoo_umbrella");
        var output = new StringWriter();
        var process = new RecordingProcess((_, _) => new ProcessResult(0, "https://github.com/Org/odoo_umbrella.git\n", ""));

        var path = await FirstRun.EnsureAsync(
            Path.Combine(directory, "setup.json"),
            null,
            new StringReader($"kein-link\nhttps://github.com/Org/odoo_umbrella\n{local}\n"),
            output,
            process,
            _ => false,
            _ => true,
            _ => false);

        Assert.Equal(Path.GetFullPath(local), path);
        Assert.Contains("Bitte eine GitHub-URL", output.ToString());
    }

    [Fact]
    public async Task Non_empty_folder_is_refused()
    {
        var directory = Temp();
        var local = Path.Combine(directory, "voll");
        Directory.CreateDirectory(local);
        File.WriteAllText(Path.Combine(local, "datei.txt"), "x");
        var setupFile = Path.Combine(directory, "setup.json");
        var output = new StringWriter();

        var path = await FirstRun.EnsureAsync(
            setupFile,
            null,
            new StringReader($"https://github.com/Org/odoo_umbrella\n{local}\n"),
            output,
            new RecordingProcess((_, _) => new ProcessResult(0, "", "")),
            Directory.Exists,
            File.Exists,
            static folder => Directory.EnumerateFileSystemEntries(folder).Any());

        Assert.Null(path);
        Assert.Contains("nicht leer", output.ToString());
        Assert.False(File.Exists(setupFile));
    }

    [Fact]
    public async Task Empty_url_aborts()
    {
        var output = new StringWriter();
        var path = await FirstRun.EnsureAsync(
            Path.Combine(Temp(), "setup.json"),
            null,
            new StringReader("\n"),
            output,
            new RecordingProcess((_, _) => new ProcessResult(0, "", "")),
            _ => false,
            _ => false,
            _ => false);

        Assert.Null(path);
        Assert.Contains("Abbruch", output.ToString());
    }

    [Fact]
    public void Missing_poll_interval_stays_at_two_minutes()
    {
        var file = Path.Combine(Temp(), "setup.json");
        File.WriteAllText(file, """
            {
              "repositoryUrl": "https://github.com/Org/odoo_umbrella",
              "localPath": "C:\\repo"
            }
            """);

        var setup = UserSetup.Load(file, out var warning);

        Assert.Null(warning);
        Assert.Null(setup!.PollIntervalSeconds);
        Assert.Equal(120, setup.EffectivePollSeconds);
        Assert.Equal(120, UserSetup.Normalize(45));
        Assert.Equal(0, UserSetup.Normalize(0));
    }

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "umbrella-manager-local-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

file sealed class RecordingProcess : IExternalProcess
{
    private readonly Func<string, IReadOnlyList<string>, ProcessResult> _handle;

    public RecordingProcess(Func<string, IReadOnlyList<string>, ProcessResult> handle)
    {
        _handle = handle;
    }

    public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(_handle(fileName, arguments));
    }
}
