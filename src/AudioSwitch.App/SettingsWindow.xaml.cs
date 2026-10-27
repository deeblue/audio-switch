using System.Windows;
using System.Windows.Controls;
using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App;

internal sealed partial class SettingsWindow : Window
{
    private readonly AudioSwitchConfiguration _configuration;
    private readonly string _dataDirectory;
    private readonly IntPtr _hotkeyTarget;
    private int _activeTabIndex;

    public SettingsWindow(IAudioTopologySnapshotSource snapshots, IAudioEndpointSource endpoints, IAudioProbe probe,
        IDefaultEndpointWriter writer, IEndpointNameWriter nameWriter, IHotkeyService hotkeyService, IntPtr hotkeyTarget, AudioSwitchConfiguration configuration,
        MachineIdentity machine, string dataDirectory, Action identifyScreens, IAutostartService autostartService, int selectedTab = 0)
    {
        InitializeComponent();
        _configuration = configuration;
        _dataDirectory = dataDirectory;
        _hotkeyTarget = hotkeyTarget;
        Icon = AppIconProvider.LoadWindowIcon();

        var deviceSettings = new DeviceSettingsViewModel(probe, nameWriter, snapshots, configuration.Ports, configuration.Aliases, Save);
        Devices = new DevicePageViewModel(snapshots, writer, probe, configuration.Ports, deviceSettings);
        Ports = new PortSettingsViewModel(snapshots, configuration.Ports, machine, deviceSettings, Save);
        Hotkeys = new HotkeySettingsViewModel(hotkeyService, endpoints, writer, configuration.Settings);
        General = new GeneralSettingsViewModel(autostartService, configuration.Settings);

        DevicesPage.DataContext = Devices;
        DevicesPage.IdentifyScreensRequested += (_, _) => identifyScreens();
        PortsPage.DataContext = Ports;
        Ports.DiagramChanged += (_, _) => RefreshAfterPortChange();
        HotkeysPage.DataContext = Hotkeys;
        HotkeysPage.ApplyRequested += (_, _) => ApplyHotkeys();
        GeneralPage.DataContext = General;
        GeneralPage.IdentifyScreensRequested += (_, _) => identifyScreens();
        General.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GeneralSettingsViewModel.AutostartEnabled)) TrySave();
        };

        Tabs.SelectedIndex = selectedTab;
        _activeTabIndex = selectedTab;
        Loaded += (_, _) => RefreshAfterPortChange();
        Closing += async (_, _) =>
        {
            Ports.CancelCalibration();
            await deviceSettings.CloseAsync();
            TrySave();
        };
    }

    public DevicePageViewModel Devices { get; }
    public PortSettingsViewModel Ports { get; }
    public HotkeySettingsViewModel Hotkeys { get; }
    public GeneralSettingsViewModel General { get; }

    public void SelectTab(int index)
    {
        if (index >= 0 && index < Tabs.Items.Count) Tabs.SelectedIndex = index;
        Activate();
    }

    // The device page is rebuilt whenever it is shown, so other pages need no refresh here.
    public void RefreshDevices()
    {
        if (_activeTabIndex == 0) Devices.Refresh();
    }

    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Tabs) || Tabs.SelectedIndex == _activeTabIndex) return;
        _activeTabIndex = Tabs.SelectedIndex;
        await Devices.Settings.SetDevicePageActiveAsync(_activeTabIndex == 0);
        if (_activeTabIndex == 0) Devices.Refresh();
    }

    private void RefreshAfterPortChange()
    {
        Devices.Refresh();
        DevicesPage.MiniDiagram.Items = Ports.Diagram.GetItems();
    }

    private void ApplyHotkeys()
    {
        if (Hotkeys.Register(_hotkeyTarget)) TrySave();
    }

    private void Save() => ConfigurationStore.Save(_dataDirectory, _configuration);

    private void TrySave()
    {
        try
        {
            Save();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine(exception);
            MessageBox.Show(this, $"設定檔儲存失敗：{exception.Message}", "AudioSwitch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
