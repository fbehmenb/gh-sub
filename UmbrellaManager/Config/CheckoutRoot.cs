namespace UmbrellaManager.Config;

using UmbrellaManager.Process;

public static class CheckoutRoot
{
    public static async Task<string?> ResolveAsync(
        IExternalProcess process,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var super = await process.RunAsync(
            "git",
            ["rev-parse", "--show-superproject-working-tree"],
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        var superPath = FirstPath(super);
        if (superPath is not null)
        {
            return superPath;
        }

        var top = await process.RunAsync(
            "git",
            ["rev-parse", "--show-toplevel"],
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        return FirstPath(top);
    }

    private static string? FirstPath(ProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            return null;
        }

        var line = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(line.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
