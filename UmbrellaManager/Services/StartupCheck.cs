namespace UmbrellaManager.Services;

using UmbrellaManager.Process;

public static class StartupCheck
{
    public static async Task<string?> FindProblemAsync(
        IExternalProcess process,
        string path,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !directoryExists(path))
        {
            return $"Verzeichnis gibt es nicht: {path}";
        }

        if (!await RunsAsync(process, "git", ["--version"], null, cancellationToken).ConfigureAwait(false))
        {
            return "git wurde nicht im PATH gefunden.";
        }

        if (!await RunsAsync(process, "gh", ["--version"], null, cancellationToken).ConfigureAwait(false))
        {
            return "gh wurde nicht im PATH gefunden.";
        }

        var auth = await process.RunAsync("gh", ["auth", "status"], path, cancellationToken).ConfigureAwait(false);
        if (auth.ExitCode != 0)
        {
            return "gh ist nicht angemeldet. Bitte gh auth login ausführen.";
        }

        var inside = await process.RunAsync("git", ["rev-parse", "--is-inside-work-tree"], path, cancellationToken).ConfigureAwait(false);
        if (inside.ExitCode != 0 || !inside.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return $"Das Verzeichnis ist kein Git-Repo: {path}";
        }

        if (!fileExists(Path.Combine(path, ".gitmodules")))
        {
            return $"Das Verzeichnis hat keine .gitmodules: {path}";
        }

        return null;
    }

    private static async Task<bool> RunsAsync(
        IExternalProcess process,
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await process.RunAsync(fileName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch (UserFacingException)
        {
            return false;
        }
    }
}
