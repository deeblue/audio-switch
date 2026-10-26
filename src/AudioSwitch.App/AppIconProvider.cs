using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AudioSwitch.App;

internal static class AppIconProvider
{
    private const string ApplicationIcon = "pack://application:,,,/AudioSwitch.App;component/Assets/AudioSwitch.ico";
    private const string DarkTrayIcon = "pack://application:,,,/AudioSwitch.App;component/Assets/AudioSwitch.Tray.Dark.ico";
    private const string LightTrayIcon = "pack://application:,,,/AudioSwitch.App;component/Assets/AudioSwitch.Tray.Light.ico";

    public static BitmapSource LoadWindowIcon()
    {
        var stream = System.Windows.Application.GetResourceStream(new Uri(ApplicationIcon))?.Stream
            ?? throw new FileNotFoundException("Application icon resource is missing.");
        using (stream)
        {
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames.MaxBy(frame => frame.PixelWidth)!;
        }
    }

    public static Icon LoadTrayIcon(bool taskbarUsesLightTheme, int dpi)
    {
        var uri = new Uri(taskbarUsesLightTheme ? LightTrayIcon : DarkTrayIcon);
        var stream = System.Windows.Application.GetResourceStream(uri)?.Stream
            ?? throw new FileNotFoundException("System tray icon resource is missing.");
        using (stream)
            return new Icon(stream, GetTrayIconSize(dpi), GetTrayIconSize(dpi));
    }

    public static int GetTrayIconSize(int dpi) => dpi >= 168 ? 32 : dpi >= 120 ? 24 : 16;

    public static bool IsTaskbarLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 1);
            return value is int setting && setting != 0;
        }
        catch
        {
            return true;
        }
    }

    public static int GetCurrentDpi()
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return (int)Math.Round(graphics.DpiX);
    }
}
