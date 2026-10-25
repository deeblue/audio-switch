using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;
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
    private WindowsEndpointNameWriter? _nameWriter;
    private MachineIdentity? _machine;
    private SettingsWindow? _settingsWindow;
    private WindowsHotkeyService? _hotkeyService;
    private HwndSource? _hotkeyWindow;
    private HotkeySettingsViewModel? _hotkeyViewModel;
    private AudioSwitchConfiguration? _configuration;
    private QuickSwitchPanelViewModel? _viewModel;
    private QuickSwitchPanelWindow? _panel;
    private DispatcherTimer? _refreshDebounce;
    private DispatcherTimer? _signalPoll;
    private readonly PanelToggleBehavior _panelToggle = new(TimeSpan.FromMilliseconds(250));

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
            _machine = new WindowsMachineIdentitySource().ReadIdentity();
            _configuration = ConfigurationStore.Load(_dataDirectory, _machine);
            _endpointSource = new WindowsAudioEndpointSource();
            _snapshotSource = new WindowsAudioTopologySnapshotSource(endpoints: _endpointSource);
            _defaultWriter = new WindowsDefaultEndpointWriter();
            _probe = new WindowsAudioProbe();
            _nameWriter = new WindowsEndpointNameWriter();
            _hotkeyService = new WindowsHotkeyService();
            _hotkeyWindow = new HwndSource(new HwndSourceParameters("AudioSwitch.Hotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
            _hotkeyWindow.AddHook(OnHotkeyMessage);
            _hotkeyViewModel = new HotkeySettingsViewModel(_hotkeyService, _endpointSource, _defaultWriter, _configuration.Settings);
            var hotkeyResult = _hotkeyService.Register(_hotkeyWindow.Handle, _configuration.Settings.PlaybackHotkey, _configuration.Settings.RecordingHotkey);
            if (!hotkeyResult.Success) Log(hotkeyResult.Error ?? "快捷鍵註冊失敗");
            _viewModel = new QuickSwitchPanelViewModel(_snapshotSource, _configuration.Ports, _defaultWriter, _probe);
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(QuickSwitchPanelViewModel.ErrorMessage) && _viewModel.ErrorMessage is { Length: > 0 } message)
                    Log(message);
            };
            _trayIconHost = new TrayIconHost();
            _trayIconHost.ExitRequested += ExitApplication;
            _trayIconHost.TogglePanelRequested += (_, _) => TogglePanel();
            _trayIconHost.SettingsRequested += (_, _) => ShowSettings();
            _trayIconHost.IdentifyScreensRequested += (_, _) => IdentifyScreens();
            _endpointSource.EndpointChanged += OnEndpointNotification;
            _endpointSource.DefaultEndpointChanged += OnEndpointNotification;
            _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _refreshDebounce.Tick += (_, _) =>
            {
                _refreshDebounce.Stop();
                if (_panel?.IsVisible == true) _viewModel.Refresh();
            };
            _signalPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _signalPoll.Tick += (_, _) => { if (_instance?.ConsumeWakeSignal() == true) ShowPanel(); };
            _signalPoll.Start();
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
        _refreshDebounce?.Stop();
        _signalPoll?.Stop();
        _panel?.Close();
        _settingsWindow?.Close();
        _trayIconHost?.Dispose();
        _probe?.Dispose();
        _nameWriter?.Dispose();
        _hotkeyService?.Dispose();
        _hotkeyWindow?.Dispose();
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
        switch (_panelToggle.OnTrayClick(_panel?.IsVisible == true, DateTimeOffset.UtcNow))
        {
            case PanelToggleAction.Open: ShowPanel(); break;
            case PanelToggleAction.Close: _panel?.Close(); break;
        }
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
            _panel = new QuickSwitchPanelWindow(_viewModel!, ShowSettings);
            _panel.FocusLossClosed += (_, _) => _panelToggle.RecordFocusLoss(DateTimeOffset.UtcNow);
            _panel.Closed += (_, _) => _panel = null;
            _viewModel!.Refresh();
            var bounds = new WindowsTrayAnchor(new WindowsMonitorDpiSource()).PlacePopup(_panel.Width, _panel.Height);
            _panel.Left = bounds.Left;
            _panel.Top = bounds.Top;
            _panel.Show();
            _panel.Activate();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow?.IsVisible == true) { _settingsWindow.Activate(); return; }
        try
        {
            _settingsWindow = new SettingsWindow(_snapshotSource!, _endpointSource!, _probe!, _defaultWriter!, _nameWriter!, _hotkeyService!, _hotkeyWindow!.Handle, _configuration!, _machine!, _dataDirectory, IdentifyScreens);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        catch (Exception exception) { ReportError(exception); }
    }

    private void IdentifyScreens()
    {
        foreach (var (screen, index) in Forms.Screen.AllScreens.Select((screen, index) => (screen, index + 1)))
        {
            var centerX = screen.Bounds.Left + screen.Bounds.Width / 2;
            var centerY = screen.Bounds.Top + screen.Bounds.Height / 2;
            var dpi = new WindowsMonitorDpiSource().GetDpiForPoint(centerX, centerY);
            var bounds = PopupPlacement.ToDipBounds(
                new PhysicalRect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height),
                new AudioSwitch.Presentation.DpiScale(dpi.X / 96d, dpi.Y / 96d));
            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                ShowActivated = false,
                AllowsTransparency = true,
                IsHitTestVisible = false,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Background = System.Windows.Media.Brushes.Transparent,
                Content = new TextBlock { Text = index.ToString(), FontSize = 72, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, Background = System.Windows.Media.Brushes.DarkSlateBlue, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(30) }
            };
            overlay.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); overlay.Close(); };
            timer.Start();
        }
    }

    private IntPtr OnHotkeyMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0312 || _hotkeyViewModel is null) return IntPtr.Zero;
        var id = wParam.ToInt32();
        var flow = id == 0x4151 ? AudioEndpointFlow.Render : id == 0x4152 ? AudioEndpointFlow.Capture : (AudioEndpointFlow?)null;
        if (flow is null) return IntPtr.Zero;
        _hotkeyViewModel.Cycle(flow.Value);
        if (_hotkeyViewModel.ErrorMessage is { Length: > 0 } error) Log(error);
        if (_panel?.IsVisible == true) _viewModel?.Refresh();
        handled = true;
        return IntPtr.Zero;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception.ToString());
        _trayIconHost?.ShowNotice("AudioSwitch", "發生錯誤，詳細資訊已寫入記錄檔。", Forms.ToolTipIcon.Error);
        _viewModel?.ReportError(e.Exception);
        e.Handled = true;
    }

    private void ReportError(Exception exception)
    {
        Log(exception.ToString());
        _viewModel?.ReportError(exception);
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
