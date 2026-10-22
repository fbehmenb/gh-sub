namespace UmbrellaManager.Config;

using UmbrellaManager.GitHub;
using UmbrellaManager.Parsing;
using UmbrellaManager.Process;

public static class FirstRun
{
    public static async Task<string?> EnsureAsync(
        string filePath,
        string? suggestedPath,
        TextReader input,
        TextWriter output,
        IExternalProcess process,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists,
        Func<string, bool> directoryHasEntries,
        CancellationToken cancellationToken = default)
    {
        var saved = UserSetup.Load(filePath, out var warning);
        if (warning is not null)
        {
            output.WriteLine(warning);
        }

        if (saved is not null && directoryExists(saved.LocalPath))
        {
            return Path.GetFullPath(saved.LocalPath);
        }

        if (saved is not null)
        {
            output.WriteLine($"Der gespeicherte Ordner fehlt: {saved.LocalPath}");
        }

        output.WriteLine("Einrichtung, nur beim ersten Start.");
        output.WriteLine();
        var url = ReadUrl(input, output);
        if (url is null)
        {
            return null;
        }

        var localPath = ReadPath(input, output, suggestedPath);
        if (localPath is null)
        {
            return null;
        }

        var problem = await PrepareAsync(url, localPath, process, directoryExists, fileExists, directoryHasEntries, cancellationToken)
            .ConfigureAwait(false);
        if (problem is not null)
        {
            output.WriteLine(problem);
            return null;
        }

        new UserSetup(url, localPath).Save(filePath);
        return localPath;
    }

    public static bool SameRepository(string left, string right)
    {
        var first = GitHubRepo.FromUrl(left);
        var second = GitHubRepo.FromUrl(right);
        return first is not null
            && second is not null
            && first.Owner.Equals(second.Owner, StringComparison.OrdinalIgnoreCase)
            && first.Name.Equals(second.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> PrepareAsync(
        string url,
        string localPath,
        IExternalProcess process,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists,
        Func<string, bool> directoryHasEntries,
        CancellationToken cancellationToken)
    {
        var gitPath = Path.Combine(localPath, ".git");
        if (directoryExists(localPath) && (directoryExists(gitPath) || fileExists(gitPath)))
        {
            return await MatchingOriginAsync(url, localPath, process, cancellationToken).ConfigureAwait(false);
        }

        if (directoryExists(localPath) && directoryHasEntries(localPath))
        {
            return "Der Ordner ist nicht leer und kein Umbrella-Clone.";
        }

        var clone = await process.RunAsync("git", ["clone", url, localPath], null, cancellationToken).ConfigureAwait(false);
        if (clone.ExitCode != 0)
        {
            return "Klonen fehlgeschlagen. " + GitHubText.Redact(FirstLine(clone.StandardError));
        }

        return await MatchingOriginAsync(url, localPath, process, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> MatchingOriginAsync(
        string url,
        string localPath,
        IExternalProcess process,
        CancellationToken cancellationToken)
    {
        var remote = await process.RunAsync("git", ["remote", "get-url", "origin"], localPath, cancellationToken)
            .ConfigureAwait(false);
        if (remote.ExitCode != 0)
        {
            return "In diesem Ordner fehlt das Remote origin.";
        }

        if (!SameRepository(url, remote.StandardOutput))
        {
            return "In diesem Ordner liegt ein anderes Remote.";
        }

        return null;
    }

    private static string? ReadUrl(TextReader input, TextWriter output)
    {
        while (true)
        {
            output.Write("GitHub-URL des Umbrella-Repos: ");
            var line = input.ReadLine();
            if (line is null || line.Trim().Length == 0)
            {
                output.WriteLine("Abbruch.");
                return null;
            }

            var url = line.Trim();
            if (GitHubRepo.FromUrl(url) is not null)
            {
                return url;
            }

            output.WriteLine("Bitte eine GitHub-URL eintragen, zum Beispiel https://github.com/Org/odoo_umbrella");
        }
    }

    private static string? ReadPath(TextReader input, TextWriter output, string? suggestedPath)
    {
        while (true)
        {
            output.Write(suggestedPath is null
                ? "Lokaler Ordner für den Clone: "
                : $"Lokaler Ordner für den Clone [{suggestedPath}]: ");
            var line = input.ReadLine();
            if (line is null)
            {
                output.WriteLine("Abbruch.");
                return null;
            }

            var text = line.Trim();
            if (text.Length == 0)
            {
                text = suggestedPath ?? "";
            }

            if (text.Length == 0)
            {
                output.WriteLine("Abbruch.");
                return null;
            }

            try
            {
                return Path.GetFullPath(text);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                output.WriteLine("Der Ordner ist kein gültiger Pfad.");
            }
        }
    }

    private static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text.Trim() : text[..end].Trim();
    }
}
