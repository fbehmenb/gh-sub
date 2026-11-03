using System.Text;
using Terminal.Gui.App;

namespace UmbrellaManager;

using UmbrellaManager.Config;
using UmbrellaManager.Process;
using UmbrellaManager.Services;
using UmbrellaManager.Views;

public static class AppHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var process = new ExternalProcess();
            var tools = await StartupCheck.FindToolProblemAsync(process).ConfigureAwait(false);
            if (tools is not null)
            {
                Console.Error.WriteLine(tools);
                return 1;
            }

            var argument = SessionLaunch.ArgumentPath(args);
            string? checkout = null;
            if (argument is null && SessionLaunch.IsExtension(Environment.GetEnvironmentVariable("GH_EXTENSION")))
            {
                checkout = await CheckoutRoot.ResolveAsync(process, Directory.GetCurrentDirectory(), CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var suggested = argument ?? checkout;
            string? path;
            if (suggested is not null && Directory.Exists(suggested))
            {
                path = suggested;
            }
            else
            {
                path = await FirstRun.EnsureAsync(
                    UserSetup.DefaultFilePath,
                    suggested,
                    Console.In,
                    Console.Out,
                    process,
                    Directory.Exists,
                    File.Exists,
                    static directory => Directory.EnumerateFileSystemEntries(directory).Any()).ConfigureAwait(false);
                if (path is null)
                {
                    return 1;
                }
            }

            var problem = await StartupCheck.FindProblemAsync(process, path, Directory.Exists).ConfigureAwait(false);
            if (problem is not null)
            {
                Console.Error.WriteLine(problem);
                return 1;
            }

            var (config, warning) = LocalConfig.Load(path, AppContext.BaseDirectory, Directory.GetCurrentDirectory());
            if (warning is not null)
            {
                Console.Error.WriteLine(warning);
            }

            var source = new UmbrellaSource(process, new ResponseCache(), path);
            var controller = new SessionController(source, [], config.HiddenBranchPattern, config.PinTitle);
            var setup = UserSetup.Load(UserSetup.DefaultFilePath, out _);
            controller.UseSetup(UserSetup.DefaultFilePath, setup?.PollIntervalSeconds);
            IApplication app = Application.Create();
            app.Init();
            controller.Post = action => app.Invoke(action);
            using var shell = new ShellView(controller, app);
            controller.BeginLoading("Hole Remote-Branches …");
            var started = false;
            shell.Initialized += (_, _) =>
            {
                if (started)
                {
                    return;
                }

                started = true;
                _ = controller.StartupAsync();
            };
            app.Run(shell);
            controller.Cancel();
            app.Dispose();
            return controller.StartupFailed ? 1 : 0;
        }
        catch (UserFacingException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(GitHub.GitHubText.Redact(ex.Message));
            return 1;
        }
    }
}
