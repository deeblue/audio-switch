using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App;

/// <summary>The quick-switch panel opened from the tray; it closes when it loses focus.</summary>
public partial class QuickSwitchPanelWindow : Window
{
    private const double MinimumPanelHeight = 220;
    private readonly QuickSwitchPanelViewModel _viewModel;
    private readonly Action<int> _settings;
    private readonly Action _exit;
    private readonly PropertyChangedEventHandler _viewModelChanged;
    private readonly NotifyCollectionChangedEventHandler _groupsChanged;
    private bool _loading;
    private bool _closing;

    public QuickSwitchPanelWindow(QuickSwitchPanelViewModel viewModel, Action<int> settings, Action exit)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        _exit = exit;
        GroupsList.ItemsSource = _viewModel.Groups;
        _loading = true;
        FlowTabs.SelectedIndex = _viewModel.Flow == AudioEndpointFlow.Capture ? 1 : 0;
        _loading = false;
        _viewModelChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(QuickSwitchPanelViewModel.ErrorMessage)) UpdateStatus();
            if (args.PropertyName == nameof(QuickSwitchPanelViewModel.HasUnconfiguredUsbEndpoints)) UpdateUnconfiguredNotice();
        };
        _groupsChanged = (_, _) => UpdateEmptyState();
        _viewModel.PropertyChanged += _viewModelChanged;
        _viewModel.Groups.CollectionChanged += _groupsChanged;
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= _viewModelChanged;
            _viewModel.Groups.CollectionChanged -= _groupsChanged;
        };
        UpdateStatus();
        UpdateUnconfiguredNotice();
        UpdateEmptyState();
        // Closing the panel takes its focus away too; that deactivation must not close it a second time.
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) =>
        {
            if (_closing) return;
            FocusLossClosed?.Invoke(this, EventArgs.Empty);
            Close();
        };
    }

    public AudioEndpointFlow CurrentFlow => FlowTabs.SelectedIndex == 1 ? AudioEndpointFlow.Capture : AudioEndpointFlow.Render;

    public event EventHandler? FocusLossClosed;

    /// <summary>Closes the panel unless it is already closing, which a fast second tray click can catch.</summary>
    public void CloseIfOpen()
    {
        if (!_closing) Close();
    }

    /// <summary>Sizes the panel to its content, up to <see cref="Window.MaxHeight"/>, before it is placed near the tray.</summary>
    public void FitToContent()
    {
        RootBorder.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Clamp(Math.Ceiling(RootBorder.DesiredSize.Height), MinimumPanelHeight, MaxHeight);
    }

    private void UpdateStatus()
    {
        StatusText.Text = _viewModel.ErrorMessage ?? string.Empty;
        StatusText.Visibility = string.IsNullOrEmpty(_viewModel.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateUnconfiguredNotice() =>
        UnconfiguredNotice.Visibility = _viewModel.HasUnconfiguredUsbEndpoints ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateEmptyState()
    {
        var empty = _viewModel.Groups.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        GroupsScrollViewer.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Flow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !ReferenceEquals(e.OriginalSource, FlowTabs) || FlowTabs.SelectedIndex < 0) return;
        _loading = true;
        try { _viewModel.SelectFlow(CurrentFlow); }
        finally { _loading = false; }
    }

    private void Endpoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string endpointId }) _viewModel.SelectEndpoint(endpointId);
    }

    private async void Tone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string endpointId }) await _viewModel.PlayToneAsync(endpointId);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => _settings(0);
    private void Configure_Click(object sender, RoutedEventArgs e) => _settings(1);
    private void Exit_Click(object sender, RoutedEventArgs e) => _exit();
}
