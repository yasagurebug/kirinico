using Kirinico.App.Cli;
using Kirinico.App.Services;
using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;

namespace Kirinico.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DebugLog.InitializeSession();

        if (e.Args.Length > 0)
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            var exitCode = await CliRunner.RunAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
