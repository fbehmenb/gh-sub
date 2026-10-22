using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Proc = System.Diagnostics.Process;

namespace UmbrellaManager.Process;

public interface IExternalProcess
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default);
}

public readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class UserFacingException : Exception
{
    public UserFacingException(string message)
        : base(message)
    {
    }
}

public sealed class ExternalProcess : IExternalProcess
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (fileName is not ("git" or "gh"))
        {
            throw new UserFacingException("Erlaubt sind nur die Programme git und gh.");
        }

        foreach (var argument in arguments)
        {
            if (ContainsToken(argument))
            {
                throw new UserFacingException("Token dürfen nicht an git oder gh übergeben werden.");
            }
        }

        var start = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "Never";
        start.Environment["GH_PROMPT_DISABLED"] = "1";

        using var process = new Proc { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new UserFacingException($"{fileName} wurde nicht im PATH gefunden.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var kill = timeout.Token.Register(() => TryKill(process));

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            return new ProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new UserFacingException("offline/zwischengespeichert");
        }
    }

    private static bool ContainsToken(string argument)
    {
        return argument.Contains("ghp_", StringComparison.Ordinal)
            || argument.Contains("github_pat_", StringComparison.Ordinal)
            || argument.Contains("Bearer ", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryKill(Proc process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
