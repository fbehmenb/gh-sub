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

            var saved = UserSetup.Load(UserSetup.DefaultFilePath, out _);
            var savedReady = saved is not null && Directory.Exists(saved.LocalPath);
            var suggested = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
            var path = await FirstRun.EnsureAsync(
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

            if (savedReady && suggested is not null)
            {
                path = suggested;
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
            Console.WriteLine("Hole Branches …");
            var branches = await source.LoadBranchesAsync(config.HiddenBranchPattern, CancellationToken.None).ConfigureAwait(false);
            if (branches.Count == 0)
            {
                Console.Error.WriteLine("Keine Remote-Branches gefunden.");
                return 1;
            }

            var controller = new SessionController(source, branches, config.HiddenBranchPattern, config.PinTitle);
            controller.RememberTips(source.BranchTips);
            await controller.LoadCurrentAsync().ConfigureAwait(false);
            var setup = UserSetup.Load(UserSetup.DefaultFilePath, out _);
            controller.UseSetup(UserSetup.DefaultFilePath, setup?.PollIntervalSeconds);
            IApplication app = Application.Create();
            app.Init();
            controller.Post = action => app.Invoke(action);
            using var shell = new ShellView(controller, app);
            app.Run(shell);
            controller.Cancel();
            app.Dispose();
            return 0;
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
