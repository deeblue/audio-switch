using System.Windows;
using System.Windows.Controls;

namespace AudioSwitch.App.Views;

/// <summary>The General page.</summary>
public partial class GeneralSettingsPageView : System.Windows.Controls.UserControl
{
    public event EventHandler? IdentifyScreensRequested;

    public GeneralSettingsPageView() => InitializeComponent();

    private void IdentifyScreens_Click(object sender, RoutedEventArgs e) => IdentifyScreensRequested?.Invoke(this, EventArgs.Empty);
}
