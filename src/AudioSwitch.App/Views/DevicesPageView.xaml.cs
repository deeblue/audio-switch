using System.Windows;
using System.Windows.Threading;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Views;

public partial class DevicesPageView : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _peakTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public DevicesPageView()
    {
        InitializeComponent();
        _peakTimer.Tick += (_, _) => Model?.Settings.RefreshPeak();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is DevicePageViewModel oldModel) oldModel.PropertyChanged -= OnModelChanged;
            if (args.NewValue is DevicePageViewModel newModel) newModel.PropertyChanged += OnModelChanged;
        };
        Unloaded += (_, _) => _peakTimer.Stop();
    }

    public event EventHandler? IdentifyScreensRequested;

    private DevicePageViewModel? Model => DataContext as DevicePageViewModel;

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DevicePageViewModel.IsListening) || Model is null) return;
        if (Model.IsListening) _peakTimer.Start();
        else _peakTimer.Stop();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Model?.Refresh();

    private async void DeviceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Model is { } model) await model.SelectAsync(e.NewValue as DeviceNodeViewModel);
    }

    private async void TestTone_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.PlayTestToneAsync();
    }

    private async void Listen_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.ToggleListeningAsync();
        ListenButton.IsChecked = Model?.IsListening;
    }

    private void IdentifyScreen_Click(object sender, RoutedEventArgs e) => IdentifyScreensRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyName_Click(object sender, RoutedEventArgs e) => Model?.ApplyName();

    private void RestoreName_Click(object sender, RoutedEventArgs e) => Model?.RestoreName();

    private void ApplyDockName_Click(object sender, RoutedEventArgs e) => Model?.ApplyDockName();

    private void RestoreDockName_Click(object sender, RoutedEventArgs e) => Model?.RestoreDockName();

    private void AssignPort_Click(object sender, RoutedEventArgs e)
    {
        if (PortChoice.SelectedValue is string interfaceId) Model?.AssignToPort(interfaceId);
    }
}
