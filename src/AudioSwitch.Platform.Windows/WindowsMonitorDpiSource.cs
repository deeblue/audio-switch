using System.ComponentModel;
using System.Runtime.InteropServices;
using AudioSwitch.Core;

namespace AudioSwitch.Platform.Windows;

public sealed class WindowsMonitorDpiSource : IMonitorDpiSource
{
    private const uint MonitorDefaultToNearest = 2;

    public MonitorDpi GetDpiForPoint(int x, int y)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(x, y), MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "找不到游標所在螢幕。");
        var result = NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY);
        Marshal.ThrowExceptionForHR(result);
        return new MonitorDpi((int)dpiX, (int)dpiY);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr MonitorFromPoint(Point point, uint flags);

        [DllImport("shcore.dll")]
        internal static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        internal readonly record struct Point(int X, int Y);
    }
}
