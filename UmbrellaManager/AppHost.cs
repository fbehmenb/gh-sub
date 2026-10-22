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
            var path = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
            var process = new ExternalProcess();
            var problem = await StartupCheck.FindProblemAsync(process, path, Directory.Exists, File.Exists).ConfigureAwait(false);
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
            var branches = await source.LoadBranchesAsync(config.HiddenBranchPattern, CancellationToken.None).ConfigureAwait(false);
            if (branches.Count == 0)
            {
                Console.Error.WriteLine("Keine Remote-Branches gefunden.");
                return 1;
            }

            var controller = new SessionController(source, branches, config.HiddenBranchPattern);
            await controller.LoadCurrentAsync().ConfigureAwait(false);
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
