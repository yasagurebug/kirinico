using Kirinico.App.Services;
using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;

namespace Kirinico.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DebugLog.InitializeSession();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
