using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using AudioSwitch.Core;
using AudioSwitch.Presentation;
#pragma warning disable WPF0001

namespace AudioSwitch.App.Tests;

public sealed class WindowSmokeTests
{
    [Fact]
    public void SettingsWindowCanBeCreatedAndItsTabsCanBeShown()
    {
        var error = RunSta(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AppTheme.Apply(application, ThemeMode.Dark);
            Assert.Equal(ThemeMode.Dark, application.ThemeMode);
            Assert.True(IsReadableContrast(
                (SolidColorBrush)application.FindResource("SolidBackgroundFillColorBaseBrush"),
                (SolidColorBrush)application.FindResource("TextFillColorSecondaryBrush")));
            var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AudioSwitch.App.Tests"));
            var dataDirectory = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            SettingsWindow? window = null;
            var autostart = new FakeAutostartService();
            try
            {
                var snapshot = new FakeSnapshotSource();
                var writer = new FakeDefaultEndpointWriter();
                var probe = new FakeAudioProbe();
                window = new SettingsWindow(snapshot, new FakeEndpointSource(), probe, writer, new FakeEndpointNameWriter(), new FakeHotkeyService(),
                    IntPtr.Zero, new AudioSwitchConfiguration
                    {
                        Ports = PortTable.Empty(),
                        Aliases = new AliasStore(),
                        Settings = new AudioSwitchSettings(),
                        Errors = []
                    }, new MachineIdentity(), dataDirectory, () => { }, autostart, 0)
                {
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000,
                    Top = -20000
                };
                Assert.NotNull(window.Icon);
                window.Show();
                for (var index = 0; index < window.Tabs.Items.Count; index++)
                {
                    window.Tabs.SelectedIndex = index;
                    Pump();
                }

                window.Tabs.SelectedIndex = 0;
                Pump();
                Assert.True(SelectNode(window.DevicesPage.DeviceTree, window.Devices.FindEndpoint("render-1")!));
                Pump();
                Assert.True(window.Devices.CanTestTone);
                Assert.False(window.Devices.CanListen);
                Assert.False(window.Devices.CanIdentifyScreen);
                var names = FindVisualElements<TextBlock>(window.DevicesPage.DeviceTree).Where(block => block.Name == "NodeName").ToArray();
                Assert.NotEmpty(names);
                Assert.All(names, name =>
                {
                    Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
                    Assert.True(double.IsFinite(((FrameworkElement)name.Parent).MaxWidth));
                });

                Assert.True(SelectNode(window.DevicesPage.DeviceTree, window.Devices.FindEndpoint("capture-1")!));
                Pump();
                Assert.True(window.Devices.CanListen);
                window.DevicesPage.ListenButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump();
                Assert.True(window.Devices.IsListening);
                window.Devices.Settings.RefreshPeak();
                Pump();
                var peak = FindVisualElements<ProgressBar>(window.DevicesPage).Single();
                Assert.True(peak.IsVisible);
                Assert.Equal(0.42, peak.Value, 2);
                window.DevicesPage.ListenButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump();
                Assert.False(window.Devices.IsListening);
                Assert.Equal(1, probe.StartCount);
                Assert.Equal(1, probe.StopCount);

                window.Tabs.SelectedIndex = 1;
                Pump();
                Assert.True(window.Ports.ShowIdleHint);
                var port = window.Ports.Add(PortConnectorType.UsbA, PortEdge.Left, 0)!;
                Pump();
                Assert.Equal(port.Id, window.PortsPage.Diagram.SelectedId);
                Assert.Single(FindVisualElements<Border>(window.PortsPage.Diagram), border => Equals(border.Tag, port.Id));
                Assert.False(window.Ports.ShowIdleHint);
                Assert.True(File.Exists(Path.Combine(dataDirectory, "ports.json")));
                Assert.True(window.PortsPage.MoveEarlierButton.IsEnabled);
                window.Ports.BeginCalibration();
                Pump();
                Assert.False(window.PortsPage.MoveEarlierButton.IsEnabled);
                Assert.False(window.PortsPage.EdgeChoice.IsEnabled);
                window.Ports.CancelCalibration();
                Pump();
                window.Hide();

                var panelModel = new QuickSwitchPanelViewModel(snapshot, PortTable.Empty(), writer, probe);
                panelModel.Refresh();
                var panel = new QuickSwitchPanelWindow(panelModel, _ => { })
                {
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000,
                    Top = -20000
                };
                panel.FitToContent();
                Assert.InRange(panel.Height, 1, panel.MaxHeight);
                panel.Show();
                Pump();
                var marks = FindVisualElements<TextBlock>(panel).Where(block => block.Name == "DefaultMark").ToArray();
                Assert.Single(marks, mark => mark.IsVisible);
                panel.Close();

                var screen = new ScreenIdentificationWindow(1, -10000, -10000, 800, 600);
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

    private static void Pump()
    {
        for (var index = 0; index < 3; index++)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static bool SelectNode(ItemsControl parent, object node)
    {
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(node) is TreeViewItem direct)
        {
            direct.IsSelected = true;
            return true;
        }
        foreach (var item in parent.Items)
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem container && SelectNode(container, node))
                return true;
        return false;
    }

    private static IEnumerable<T> FindVisualElements<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in FindVisualElements<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }

    private static bool IsReadableContrast(SolidColorBrush background, SolidColorBrush foreground)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte channel)
            {
                var value = channel / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }

        var first = Luminance(background.Color);
        var second = Luminance(foreground.Color);
        var ratio = (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        return ratio >= 4.5;
    }

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
        public string? GetDefaultEndpointId(AudioEndpointFlow flow, AudioEndpointRole role) => flow == AudioEndpointFlow.Render ? "render-1" : "capture-1";
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
