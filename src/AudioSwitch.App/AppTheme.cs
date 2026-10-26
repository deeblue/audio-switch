namespace AudioSwitch.App;

/// <summary>Theme setup shared by the app and the screenshot tool; views use only Fluent theme brushes.</summary>
public static class AppTheme
{
    public static void Apply(System.Windows.Application application, System.Windows.ThemeMode mode)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.ThemeMode = mode;
    }
}
