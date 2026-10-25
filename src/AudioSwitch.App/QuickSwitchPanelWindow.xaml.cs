using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App;

public partial class QuickSwitchPanelWindow : Window
{
    private readonly QuickSwitchPanelViewModel _viewModel;
    private readonly Action _settings;
    private readonly PropertyChangedEventHandler _viewModelChanged;
    private bool _loading;

    public QuickSwitchPanelWindow(QuickSwitchPanelViewModel viewModel, Action settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        GroupsList.ItemsSource = _viewModel.Groups;
        _loading = true;
        FlowTabs.SelectedIndex = _viewModel.Flow == AudioEndpointFlow.Capture ? 1 : 0;
        _loading = false;
        _viewModelChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(QuickSwitchPanelViewModel.ErrorMessage))
                StatusText.Text = _viewModel.ErrorMessage ?? string.Empty;
            if (args.PropertyName == nameof(QuickSwitchPanelViewModel.HasUnconfiguredUsbEndpoints))
                SetUnconfiguredUsbVisible(_viewModel.HasUnconfiguredUsbEndpoints);
        };
        _viewModel.PropertyChanged += _viewModelChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= _viewModelChanged;
        SetUnconfiguredUsbVisible(_viewModel.HasUnconfiguredUsbEndpoints);
        Deactivated += (_, _) =>
        {
            FocusLossClosed?.Invoke(this, EventArgs.Empty);
            Close();
        };
    }

    public AudioEndpointFlow CurrentFlow => FlowTabs.SelectedIndex == 1 ? AudioEndpointFlow.Capture : AudioEndpointFlow.Render;

    public event EventHandler? FocusLossClosed;

    public void SetUnconfiguredUsbVisible(bool visible) => ConfigureButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void Flow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FlowTabs.SelectedIndex < 0) return;
        _loading = true;
        try { _viewModel.SelectFlow(CurrentFlow); }
        finally { _loading = false; }
    }

    private void Endpoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string endpointId }) _viewModel.SelectEndpoint(endpointId);
    }

    private async void Tone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string endpointId }) await _viewModel.PlayToneAsync(endpointId);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => _settings();
    private void Configure_Click(object sender, RoutedEventArgs e) => _settings();
}
