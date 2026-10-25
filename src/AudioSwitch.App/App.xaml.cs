namespace AudioSwitch.App;

public partial class App : System.Windows.Application
{
    private TrayIconHost? _trayIconHost;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _trayIconHost = new TrayIconHost();
        _trayIconHost.ExitRequested += ExitApplication;
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_trayIconHost is not null)
        {
            _trayIconHost.ExitRequested -= ExitApplication;
            _trayIconHost.Dispose();
        }

        base.OnExit(e);
    }

    private void ExitApplication(object? sender, EventArgs e) => Shutdown();
}
