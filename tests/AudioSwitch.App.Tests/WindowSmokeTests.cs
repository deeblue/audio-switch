using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Tests;

public sealed class WindowSmokeTests
{
    [Fact]
    public void SettingsWindowCanBeCreatedAndItsTabsCanBeShown()
    {
        var error = RunSta(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AudioSwitch.App.Tests"));
            var dataDirectory = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            Window? window = null;
            var autostart = new FakeAutostartService();
            try
            {
                var snapshot = new FakeSnapshotSource();
                var endpointSource = new FakeEndpointSource();
                var writer = new FakeDefaultEndpointWriter();
                var probe = new FakeAudioProbe();
                var assembly = typeof(TrayIconHost).Assembly;
                var settingsWindowType = assembly.GetType("AudioSwitch.App.SettingsWindow", throwOnError: true)!;
                var constructor = settingsWindowType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
                window = (Window)constructor.Invoke([
                    snapshot,
                    endpointSource,
                    probe,
                    writer,
                    new FakeEndpointNameWriter(),
                    new FakeHotkeyService(),
                    IntPtr.Zero,
                    new AudioSwitchConfiguration
                    {
                        Ports = PortTable.Empty(),
                        Aliases = new AliasStore(),
                        Settings = new AudioSwitchSettings(),
                        Errors = []
                    },
                    new MachineIdentity(),
                    dataDirectory,
                    (Action)(() => { }),
                    autostart,
                    0
                ]);
                window.ShowActivated = false;
                window.Show();
                window.UpdateLayout();
                var tabs = (TabControl)window.Content;
                for (var index = 0; index < tabs.Items.Count; index++)
                {
                    tabs.SelectedIndex = index;
                    tabs.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                }
                tabs.SelectedIndex = 0;
                tabs.UpdateLayout();
                var deviceTree = FindVisualTree(window);
                Assert.True(SelectEndpoint(deviceTree, "render-1"));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Empty(ReadDeviceStatus(settingsWindowType, window));
                Assert.True(SelectEndpoint(deviceTree, "capture-1"));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Empty(ReadDeviceStatus(settingsWindowType, window));
                var listenButton = (Button)settingsWindowType.GetField("_listenButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                listenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                settingsWindowType.GetMethod("UpdatePeak", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                var peakProgress = (ProgressBar)settingsWindowType.GetField("_peakProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var peakPercent = (TextBlock)settingsWindowType.GetField("_peakPercent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                Assert.Equal(Visibility.Visible, peakProgress.Visibility);
                Assert.Equal(0.42, peakProgress.Value, 2);
                Assert.Contains("42", peakPercent.Text);
                Assert.EndsWith("%", peakPercent.Text);
                listenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, peakProgress.Visibility);
                Assert.Equal(1, probe.StartCount);
                Assert.Equal(1, probe.StopCount);

                window.Hide();
                var snapshotTree = TopologyBuilder.Build(snapshot.Capture(), PortTable.Empty());
                var panelModel = new QuickSwitchPanelViewModel(snapshot, PortTable.Empty(), writer, probe);
                panelModel.Refresh(snapshotTree, AudioEndpointFlow.Render);
                var panel = new AudioSwitch.App.QuickSwitchPanelWindow(panelModel, _ => { }) { ShowActivated = false };
                panel.Show();
                panel.UpdateLayout();
                panel.Close();

                var screenType = assembly.GetType("AudioSwitch.App.ScreenIdentificationWindow", throwOnError: true)!;
                var screen = (Window)Activator.CreateInstance(screenType, 1, -10000d, -10000d, 800d, 600d)!;
                screen.Show();
                screen.UpdateLayout();
                screen.Close();
                Assert.Equal(0, autostart.CallCount);
            }
            finally
            {
                window?.Close();
                application.Shutdown();
                var fullDataDirectory = Path.GetFullPath(dataDirectory);
                if (!fullDataDirectory.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Temporary configuration directory escaped its test root.");
                if (Directory.Exists(fullDataDirectory)) Directory.Delete(fullDataDirectory, recursive: true);
            }
        });

        Assert.Null(error);
    }

    private static TreeView FindVisualTree(DependencyObject root)
    {
        if (root is TreeView tree) return tree;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindVisualTree(System.Windows.Media.VisualTreeHelper.GetChild(root, index));
            if (found is not null) return found;
        }
        throw new InvalidOperationException("Settings window did not create its endpoint tree.");
    }

    private static bool SelectEndpoint(ItemsControl parent, string endpointId)
    {
        foreach (var item in parent.Items.OfType<TreeViewItem>())
        {
            if (Equals(item.Tag, endpointId))
            {
                item.IsSelected = true;
                return true;
            }
            if (SelectEndpoint(item, endpointId)) return true;
        }
        return false;
    }

    private static string ReadDeviceStatus(Type settingsWindowType, Window window) =>
        ((TextBlock)settingsWindowType.GetField("_deviceStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Text;

    private static Exception? RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error;
    }

    private sealed class FakeSnapshotSource : IAudioTopologySnapshotSource
    {
        public Snapshot Capture() => new()
        {
            Endpoints =
            [
                new AudioEndpoint { EndpointId = "render-1", Flow = "render", State = "active", FriendlyName = "Playback" },
                new AudioEndpoint { EndpointId = "capture-1", Flow = "capture", State = "active", FriendlyName = "Microphone" }
            ]
        };
    }

    private sealed class FakeEndpointSource : IAudioEndpointSource
    {
        private static readonly AudioEndpoint[] Endpoints =
        [
            new AudioEndpoint { EndpointId = "render-1", Flow = "render", State = "active", FriendlyName = "Playback" },
            new AudioEndpoint { EndpointId = "capture-1", Flow = "capture", State = "active", FriendlyName = "Microphone" }
        ];

        public IReadOnlyList<PlatformError> LastErrors => [];
        public event EventHandler<EndpointNotificationEventArgs>? EndpointChanged { add { } remove { } }
        public event EventHandler<EndpointNotificationEventArgs>? DefaultEndpointChanged { add { } remove { } }
        public IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints() => Endpoints;
        public void Dispose() { }
    }

    private sealed class FakeDefaultEndpointWriter : IDefaultEndpointWriter
    {
        public string? GetDefaultEndpointId(AudioEndpointFlow flow, AudioEndpointRole role) => null;
        public EndpointSwitchResult SetDefaultEndpoint(string endpointId, AudioEndpointFlow flow) => throw new InvalidOperationException("Smoke tests must not change default endpoints.");
        public EndpointRoleChangeResult SetDefaultEndpointRole(string endpointId, AudioEndpointFlow flow, AudioEndpointRole role) => throw new InvalidOperationException("Smoke tests must not change default endpoints.");
        public void Dispose() { }
    }

    private sealed class FakeEndpointNameWriter : IEndpointNameWriter
    {
        public string ReadDeviceDescription(string endpointId) => endpointId;
        public string ReadFriendlyName(string endpointId) => endpointId;
        public void WriteDeviceDescription(string endpointId, string value) => throw new InvalidOperationException("Smoke tests must not rename endpoints.");
        public void Dispose() { }
    }

    private sealed class FakeAudioProbe : IAudioProbe
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task PlayTestToneAsync(string endpointId, TimeSpan duration, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Smoke tests must not play a tone.");
        public void StartPeakSampling(string endpointId) => StartCount++;
        public float ReadPeakLevel(string endpointId) => 0.42f;
        public Task StopPeakSamplingAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }

    private sealed class FakeHotkeyService : IHotkeyService
    {
        public HotkeyRegistrationResult Register(IntPtr targetWindow, string? playbackHotkey, string? recordingHotkey) => new(true, null);
        public void Unregister() { }
        public void Dispose() { }
    }

    private sealed class FakeAutostartService : IAutostartService
    {
        public int CallCount { get; private set; }

        public void SetEnabled(bool enabled)
        {
            CallCount++;
            throw new InvalidOperationException("Smoke tests must not modify autostart settings.");
        }
    }
}
