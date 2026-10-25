using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;
using AudioSwitch.Presentation;
using Forms = System.Windows.Forms;

namespace AudioSwitch.App;

public partial class App : System.Windows.Application
{
    private readonly string _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioSwitch");
    private readonly string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioSwitch", "logs");
    private SingleInstanceGate? _instance;
    private TrayIconHost? _trayIconHost;
    private WindowsAudioEndpointSource? _endpointSource;
    private WindowsAudioTopologySnapshotSource? _snapshotSource;
    private WindowsDefaultEndpointWriter? _defaultWriter;
    private WindowsAudioProbe? _probe;
    private AudioSwitchConfiguration? _configuration;
    private QuickSwitchPanelViewModel? _viewModel;
    private QuickSwitchPanelWindow? _panel;
    private DispatcherTimer? _refreshDebounce;
    private DispatcherTimer? _signalPoll;
    private DispatcherTimer? _peakRefresh;
    private string? _samplingEndpointId;
    private Task? _peakStopTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeMode = System.Windows.ThemeMode.System;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _instance = new SingleInstanceGate();
        if (!_instance.IsPrimary)
        {
            Shutdown();
            return;
        }

        try
        {
            Directory.CreateDirectory(_dataDirectory);
            var machine = new WindowsMachineIdentitySource().ReadIdentity();
            _configuration = ConfigurationStore.Load(_dataDirectory, machine);
            _endpointSource = new WindowsAudioEndpointSource();
            _snapshotSource = new WindowsAudioTopologySnapshotSource(endpoints: _endpointSource);
            _defaultWriter = new WindowsDefaultEndpointWriter();
            _probe = new WindowsAudioProbe();
            _viewModel = new QuickSwitchPanelViewModel(_defaultWriter);
            _trayIconHost = new TrayIconHost();
            _trayIconHost.ExitRequested += ExitApplication;
            _trayIconHost.TogglePanelRequested += (_, _) => TogglePanel();
            _trayIconHost.SettingsRequested += (_, _) => ShowSettings();
            _trayIconHost.IdentifyScreensRequested += (_, _) => IdentifyScreens();
            _endpointSource.EndpointChanged += OnEndpointNotification;
            _endpointSource.DefaultEndpointChanged += OnEndpointNotification;
            _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); RefreshOpenPanel(); _ = UpdatePeakSamplingAsync(); };
            _signalPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _signalPoll.Tick += (_, _) => { if (_instance?.ConsumeWakeSignal() == true) ShowPanel(); };
            _signalPoll.Start();
            _peakRefresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _peakRefresh.Tick += (_, _) => UpdatePeak();
            if (_viewModel.TryMarkFirstRunNotice(_configuration.Settings))
            {
                ConfigurationStore.Save(_dataDirectory, _configuration);
                _trayIconHost.ShowNotice("AudioSwitch", "首次使用可在設定中定義電腦接口。", Forms.ToolTipIcon.Info);
            }
            if (_configuration.Errors.Count > 0)
                Log(string.Join(Environment.NewLine, _configuration.Errors));
        }
        catch (Exception exception)
        {
            Log(exception.ToString());
            System.Windows.MessageBox.Show("AudioSwitch 啟動失敗，詳細資訊已寫入記錄檔。", "AudioSwitch", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_endpointSource is not null)
        {
            _endpointSource.EndpointChanged -= OnEndpointNotification;
            _endpointSource.DefaultEndpointChanged -= OnEndpointNotification;
        }
        _samplingEndpointId = null;
        _refreshDebounce?.Stop();
        _signalPoll?.Stop();
        _peakRefresh?.Stop();
        _panel?.Close();
        _trayIconHost?.Dispose();
        _probe?.Dispose();
        _defaultWriter?.Dispose();
        _snapshotSource = null;
        _endpointSource?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnEndpointNotification(object? sender, EndpointNotificationEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_refreshDebounce is null) return;
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    });

    private void TogglePanel()
    {
        if (_panel?.IsVisible == true) _panel.Close();
        else ShowPanel();
    }

    private void ShowPanel()
    {
        if (_panel?.IsVisible == true)
        {
            _panel.Activate();
            return;
        }
        try
        {
            _panel = new QuickSwitchPanelWindow(_viewModel!, ChangeFlow, SelectEndpoint, PlayTone, ShowSettings);
            _panel.Closed += (_, _) => { _ = StopPeakSamplingAsync(); _panel = null; };
            RefreshOpenPanel();
            var bounds = new WindowsTrayAnchor().PlacePopup(_panel.Width, _panel.Height);
            _panel.Left = bounds.Left;
            _panel.Top = bounds.Top;
            _panel.Show();
            _panel.Activate();
            _ = UpdatePeakSamplingAsync();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void RefreshOpenPanel()
    {
        if (_panel is null || _snapshotSource is null || _configuration is null || _viewModel is null) return;
        try
        {
            var snapshot = _snapshotSource.Capture();
            var tree = TopologyBuilder.Build(snapshot, _configuration.Ports);
            _viewModel.Refresh(tree, _panel.CurrentFlow);
            _panel.SetUnconfiguredUsbVisible(tree.Roots.Any(root => root.Id == "USB（未設定接口）" && ContainsEndpoint(root.Children)));
            if (snapshot.Errors.Count > 0) Log(string.Join(Environment.NewLine, snapshot.Errors.SelectMany(error => error.Values)));
        }
        catch (Exception exception) { ReportError(exception); }
    }

    private async Task ChangeFlow(AudioEndpointFlow flow)
    {
        await StopPeakSamplingAsync();
        RefreshOpenPanel();
        await UpdatePeakSamplingAsync();
    }

    private void SelectEndpoint(string endpointId)
    {
        _viewModel?.SelectEndpoint(endpointId);
        _ = UpdatePeakSamplingAsync();
    }

    private static bool ContainsEndpoint(IEnumerable<TopologyNode> nodes) => nodes.Any(node => node.EndpointId is not null || ContainsEndpoint(node.Children));

    private async Task PlayTone(string endpointId)
    {
        try { await _probe!.PlayTestToneAsync(endpointId, TimeSpan.FromSeconds(2)); }
        catch (Exception exception) { ReportError(exception); }
    }

    private async Task UpdatePeakSamplingAsync()
    {
        if (_panel is null || _panel.CurrentFlow != AudioEndpointFlow.Capture || _probe is null || _viewModel is null) { await StopPeakSamplingAsync(); return; }
        var endpoint = _viewModel.Groups.SelectMany(group => group.Endpoints).FirstOrDefault(item => item.IsDefault);
        if (endpoint is null) { await StopPeakSamplingAsync(); return; }
        if (string.Equals(endpoint.EndpointId, _samplingEndpointId, StringComparison.OrdinalIgnoreCase)) return;
        await StopPeakSamplingAsync();
        if (_panel is null || _panel.CurrentFlow != AudioEndpointFlow.Capture ||
            string.Equals(endpoint.EndpointId, _samplingEndpointId, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            _probe.StartPeakSampling(endpoint.EndpointId);
            _samplingEndpointId = endpoint.EndpointId;
            _peakRefresh?.Start();
        }
        catch (Exception exception) { _panel.SetPeakText("無法讀取麥克風峰值：" + exception.Message); Log(exception.ToString()); }
    }

    private void UpdatePeak()
    {
        if (_samplingEndpointId is null || _probe is null || _panel is null) return;
        try { _panel.SetPeakText($"麥克風峰值  {_probe.ReadPeakLevel(_samplingEndpointId):P0}"); }
        catch (Exception exception) { _panel.SetPeakText("麥克風峰值暫時無法讀取"); Log(exception.ToString()); _ = StopPeakSamplingAsync(); }
    }

    private async Task StopPeakSamplingAsync()
    {
        _peakRefresh?.Stop();
        if (_peakStopTask is not null)
        {
            await _peakStopTask;
            return;
        }
        if (_samplingEndpointId is not null)
        {
            _samplingEndpointId = null;
            if (_probe is not null)
            {
                _peakStopTask = ObserveStopAsync(_probe.StopPeakSamplingAsync());
                try { await _peakStopTask; }
                finally { _peakStopTask = null; }
            }
        }
        _panel?.SetPeakText(null);
    }

    private async Task ObserveStopAsync(Task stopTask)
    {
        try { await stopTask; }
        catch (Exception exception) { Log(exception.ToString()); }
    }

    private void ShowSettings()
    {
        var settings = new Window
        {
            Title = "AudioSwitch 設定",
            Width = 480,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new System.Windows.Controls.TabControl
            {
                Margin = new Thickness(12),
                SelectedIndex = 0,
                Items =
                {
                    new TabItem { Header = "接口定義", Content = new TextBlock { Text = "接口校準與示意圖將於設定階段提供。", Margin = new Thickness(16), FontSize = 15 } }
                }
            }
        };
        settings.Show();
    }

    private void IdentifyScreens()
    {
        foreach (var (screen, index) in Forms.Screen.AllScreens.Select((screen, index) => (screen, index + 1)))
        {
            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height,
                Background = System.Windows.Media.Brushes.Transparent,
                Content = new TextBlock { Text = index.ToString(), FontSize = 72, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, Background = System.Windows.Media.Brushes.DarkSlateBlue, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(30) }
            };
            overlay.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); overlay.Close(); };
            timer.Start();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception.ToString());
        _trayIconHost?.ShowNotice("AudioSwitch", "發生錯誤，詳細資訊已寫入記錄檔。", Forms.ToolTipIcon.Error);
        if (_panel is not null) _panel.SetError("發生錯誤；請查看記錄檔後重試。");
        e.Handled = true;
    }

    private void ReportError(Exception exception)
    {
        Log(exception.ToString());
        if (_panel is not null) _panel.SetError("操作失敗：" + exception.Message);
        _trayIconHost?.ShowNotice("AudioSwitch", "操作失敗，詳細資訊已寫入記錄檔。", Forms.ToolTipIcon.Error);
    }

    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            File.AppendAllText(Path.Combine(_logDirectory, "audioswitch.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception loggingException) { System.Diagnostics.Trace.WriteLine(loggingException); }
    }

    private void ExitApplication(object? sender, EventArgs e) => Shutdown();
}
