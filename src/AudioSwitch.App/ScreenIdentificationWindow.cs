using System.Windows;
using System.Windows.Controls;

namespace AudioSwitch.App;

internal sealed class ScreenIdentificationWindow : Window
{
    public ScreenIdentificationWindow(int number, double left, double top, double width, double height)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        AllowsTransparency = true;
        IsHitTestVisible = false;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Background = System.Windows.Media.Brushes.Transparent;
        Content = new TextBlock
        {
            Text = number.ToString(),
            FontSize = 72,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.White,
            Background = System.Windows.Media.Brushes.DarkSlateBlue,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(30)
        };
    }
}
