using System.Windows;
using System.Windows.Controls;
using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App;

public partial class QuickSwitchPanelWindow : Window
{
    private readonly QuickSwitchPanelViewModel _viewModel;
    private readonly Func<AudioEndpointFlow, Task> _flowChanged;
    private readonly Action<string> _selectEndpoint;
    private readonly Func<string, Task> _playTone;
    private readonly Action _settings;
    private bool _loading;

    public QuickSwitchPanelWindow(QuickSwitchPanelViewModel viewModel, Func<AudioEndpointFlow, Task> flowChanged,
        Action<string> selectEndpoint, Func<string, Task> playTone, Action settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _flowChanged = flowChanged;
        _selectEndpoint = selectEndpoint;
        _playTone = playTone;
        _settings = settings;
        GroupsList.ItemsSource = _viewModel.Groups;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(QuickSwitchPanelViewModel.ErrorMessage))
                StatusText.Text = _viewModel.ErrorMessage ?? string.Empty;
        };
        Loaded += (_, _) => Flow_SelectionChanged(this, null!);
        Deactivated += (_, _) => Close();
    }

    public AudioEndpointFlow CurrentFlow => FlowTabs.SelectedIndex == 1 ? AudioEndpointFlow.Capture : AudioEndpointFlow.Render;

    public void SetUnconfiguredUsbVisible(bool visible) => ConfigureButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetPeakText(string? text)
    {
        PeakText.Text = text ?? string.Empty;
        PeakText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetError(string message) => StatusText.Text = message;

    private void Flow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FlowTabs.SelectedIndex < 0) return;
        _loading = true;
        try { _ = _flowChanged(CurrentFlow); }
        finally { _loading = false; }
    }

    private void Endpoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string endpointId }) _selectEndpoint(endpointId);
    }

    private async void Tone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string endpointId }) await _playTone(endpointId);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => _settings();
    private void Configure_Click(object sender, RoutedEventArgs e) => _settings();
}
