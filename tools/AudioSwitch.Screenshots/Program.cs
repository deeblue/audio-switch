using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioSwitch.App;
using AudioSwitch.App.Views;
using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;
using AudioSwitch.Presentation;

static class ProgramEntry
{
    static readonly MachineIdentity Machine = new() { Manufacturer = "Lenovo", Model = "ThinkPad T14p Gen 3" };

    [STAThread]
    static int Main(string[] args)
    {
        var repositoryRoot = FindRepositoryRoot();
        var assets = Path.Combine(repositoryRoot, "src", "AudioSwitch.App", "Assets");
        if (args.Contains("--icons", StringComparer.OrdinalIgnoreCase))
        {
            IconGenerator.WriteIcons(assets);
            return 0;
        }

        var outputRoot = Path.GetFullPath(args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
            ?? Path.Combine(repositoryRoot, "artifacts", "screenshots", "current"));
        var live = args.Contains("--live", StringComparer.OrdinalIgnoreCase);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string? liveDirectory = null;
        try
        {
            IconGenerator.WriteOverview(assets, Path.Combine(outputRoot, "icon-overview.png"));
            if (live)
            {
                liveDirectory = CreateTemporaryConfigurationCopy();
                using var fixture = new LiveFixture(liveDirectory);
                foreach (var (theme, mode) in Themes)
                    foreach (var (scaleName, scale) in Scales)
                    {
                        AppTheme.Apply(app, mode);
                        var shot = new Shot(app, outputRoot, theme, scaleName, scale);
                        CaptureLive(shot, fixture, liveDirectory);
                    }
            }
            else
            {
                foreach (var (theme, mode) in Themes)
                    foreach (var (scaleName, scale) in Scales)
                    {
                        AppTheme.Apply(app, mode);
                        var shot = new Shot(app, outputRoot, theme, scaleName, scale);
                        CaptureFixtures(shot);
                    }
            }
        }
        finally
        {
            app.Shutdown();
            if (liveDirectory is not null) DeleteTemporaryConfigurationCopy(liveDirectory);
        }
        Console.WriteLine($"Screenshots written under {outputRoot}");
        return 0;
    }

    static readonly (string Name, ThemeMode Mode)[] Themes = [("light", ThemeMode.Light), ("dark", ThemeMode.Dark)];
    static readonly (string Name, double Scale)[] Scales = [("100", 1d), ("200", 2d)];

    static void CaptureFixtures(Shot shot)
    {
        var standard = Fixtures.Standard();
        var multiple = Fixtures.MultipleDevices();
        var longNames = Fixtures.LongNames();
        var builtInOnly = Fixtures.BuiltInOnly();
        var empty = Fixtures.Empty();
        var unconfigured = Fixtures.WithUnconfiguredUsb();

        shot.Panel("quick-switch/playback", standard, AudioEndpointFlow.Render);
        shot.Panel("quick-switch/recording", standard, AudioEndpointFlow.Capture);
        shot.Panel("quick-switch/built-in-only", builtInOnly, AudioEndpointFlow.Render);
        shot.Panel("quick-switch/multiple-devices", multiple, AudioEndpointFlow.Render);
        shot.Panel("quick-switch/long-names", longNames, AudioEndpointFlow.Render);
        shot.Panel("quick-switch/unconfigured-usb", unconfigured, AudioEndpointFlow.Render);
        shot.Panel("quick-switch/empty", empty, AudioEndpointFlow.Render);
        shot.Panel("quick-switch/switch-failure", standard, AudioEndpointFlow.Render,
            model => model.ReportError(new InvalidOperationException("Windows 未確認切換結果，請重試。")));

        shot.Settings("settings/devices/none-selected", standard, 0);
        shot.Settings("settings/devices/playback-selected", standard, 0, window => SelectEndpoint(window, "render-headset"));
        shot.Settings("settings/devices/recording-listening", standard, 0, window =>
        {
            SelectEndpoint(window, "capture-array");
            window.Devices.ToggleListeningAsync().GetAwaiter().GetResult();
            window.Devices.Settings.RefreshPeak();
        });
        shot.Settings("settings/devices/multiple-devices", multiple, 0, window => SelectEndpoint(window, "render-dock-speaker"));
        shot.Settings("settings/devices/screen-selected", multiple, 0, window => SelectNode(window, node => node.Kind == DeviceNodeKind.Display));
        shot.Settings("settings/devices/unconfigured-usb", unconfigured, 0, window => SelectEndpoint(window, "render-new-speaker"));
        shot.Settings("settings/devices/long-names", longNames, 0, window => SelectEndpoint(window, "render-headset"));
        shot.Settings("settings/devices/empty", empty, 0);

        shot.Settings("settings/ports/overview", standard, 1);
        shot.Settings("settings/ports/selected", standard, 1, window => window.Ports.Select("left-usb-a"));
        shot.Settings("settings/ports/calibrating", standard, 1, window =>
        {
            window.Ports.Select("right-usb-a");
            window.Ports.BeginCalibration();
        });
        shot.Settings("settings/ports/calibrating-usb-c", standard, 1, window =>
        {
            window.Ports.Select("rear-usb-c");
            window.Ports.BeginCalibration();
        });
        shot.Settings("settings/ports/calibrating-usb-c-plug", standard, 1, window =>
        {
            window.Ports.Select("rear-usb-c");
            window.Ports.BeginCalibration();
            window.Ports.ConfirmUnplugged();
        });
        shot.Settings("settings/ports/detect-new-device", standard, 1, window => window.Ports.BeginDetectNewDevice());
        var detecting = Fixtures.Standard();
        shot.Settings("settings/ports/device-detected", detecting, 1, window =>
        {
            window.Ports.BeginDetectNewDevice();
            window.Ports.ConfirmUnplugged();
            detecting.AttachUsbSpeaker();
            window.Ports.CompleteDetection();
            window.Ports.Select("rear-usb-c");
        });
        var calibrating = Fixtures.Standard();
        shot.Settings("settings/ports/calibrated-name-device", calibrating, 1, window =>
        {
            window.Ports.Select("rear-usb-c");
            window.Ports.BeginCalibration();
            window.Ports.ConfirmUnplugged();
            calibrating.AttachUsbSpeaker();
            window.Ports.CompleteDetection();
        });
        shot.Settings("settings/ports/many-ports", Fixtures.ManyPorts(), 1, window => window.Ports.Select("extra-4"));
        shot.Settings("settings/ports/other-machine", Fixtures.OtherMachine(), 1);

        shot.Settings("settings/hotkeys/empty", standard, 2);
        shot.Settings("settings/hotkeys/configured", standard, 2, window =>
        {
            window.Hotkeys.PlaybackHotkey = "Ctrl + Alt + F1";
            window.Hotkeys.RecordingHotkey = "Ctrl + Alt + F2";
        });
        shot.Settings("settings/hotkeys/registration-failed", standard, 2, window =>
        {
            window.Hotkeys.PlaybackHotkey = "Ctrl + Alt + F1";
            window.Hotkeys.Register(IntPtr.Zero);
        });
        shot.Settings("settings/general", standard, 3);
        shot.ScreenIdentification("screen-identification");
        shot.PanelTooltip("tooltips/test-tone", standard);
        shot.PortTooltip("tooltips/port", standard, "left-usb-a");
    }

    static void CaptureLive(Shot shot, LiveFixture fixture, string dataDirectory)
    {
        shot.Panel("quick-switch/playback", fixture, AudioEndpointFlow.Render);
        shot.Panel("quick-switch/recording", fixture, AudioEndpointFlow.Capture);
        shot.Settings("settings/devices/none-selected", fixture, 0, dataDirectory: dataDirectory);
        shot.Settings("settings/devices/first-playback", fixture, 0, window =>
            SelectNode(window, node => node.Kind == DeviceNodeKind.Playback), dataDirectory);
        shot.Settings("settings/devices/first-recording", fixture, 0, window =>
            SelectNode(window, node => node.Kind == DeviceNodeKind.Recording), dataDirectory);
        shot.Settings("settings/ports/overview", fixture, 1, dataDirectory: dataDirectory);
        shot.Settings("settings/hotkeys", fixture, 2, dataDirectory: dataDirectory);
        shot.Settings("settings/general", fixture, 3, dataDirectory: dataDirectory);
    }

    static void SelectEndpoint(SettingsWindow window, string endpointId) =>
        SelectNode(window, node => string.Equals(node.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase));

    static void SelectNode(SettingsWindow window, Func<DeviceNodeViewModel, bool> predicate)
    {
        static IEnumerable<DeviceNodeViewModel> Flatten(IEnumerable<DeviceNodeViewModel> nodes) =>
            nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
        var node = Flatten(window.Devices.Roots).FirstOrDefault(predicate)
            ?? throw new InvalidOperationException("Screenshot fixture node not found.");
        var container = FindTreeItem(window.DevicesPage.DeviceTree, node);
        if (container is not null) container.IsSelected = true;
        else window.Devices.SelectAsync(node).GetAwaiter().GetResult();
    }

    static TreeViewItem? FindTreeItem(ItemsControl parent, object item)
    {
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct) return direct;
        foreach (var child in parent.Items)
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem childContainer && FindTreeItem(childContainer, item) is { } found)
                return found;
        return null;
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "AudioSwitch.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Run the screenshot tool from inside the repository.");
    }

    static string CreateTemporaryConfigurationCopy()
    {
        var target = Path.Combine(Path.GetTempPath(), "AudioSwitch.Screenshots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioSwitch");
        if (Directory.Exists(source))
            foreach (var file in Directory.GetFiles(source, "*.json"))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        return target;
    }

    static void DeleteTemporaryConfigurationCopy(string directory)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AudioSwitch.Screenshots")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Temporary settings directory escaped the screenshot tool's temp root.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    internal static MachineIdentity FixtureMachine => Machine;
}

sealed class Shot(Application app, string root, string theme, string scaleName, double scale)
{
    public void Panel(string scenario, IScreenshotFixture fixture, AudioEndpointFlow flow, Action<QuickSwitchPanelViewModel>? configure = null)
    {
        var model = new QuickSwitchPanelViewModel(fixture, fixture.CreatePorts(), new SafeEndpointWriter(fixture), new SafeProbe());
        model.SelectFlow(flow);
        configure?.Invoke(model);
        var window = new QuickSwitchPanelWindow(model, _ => { }, () => { });
        window.FitToContent();
        Capture(window, scenario);
    }

    public void Settings(string scenario, IScreenshotFixture fixture, int tab, Action<SettingsWindow>? configure = null, string? dataDirectory = null)
    {
        var temporary = dataDirectory is null;
        dataDirectory ??= Path.Combine(Path.GetTempPath(), "AudioSwitch.Screenshots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var configuration = fixture.LoadConfiguration(dataDirectory);
            var window = new SettingsWindow(fixture, fixture, new SafeProbe(), new SafeEndpointWriter(fixture), new SafeNameWriter(fixture),
                new SafeHotkeyService(), IntPtr.Zero, configuration, fixture.Machine, dataDirectory, () => { }, new SafeAutostartService(), tab);
            Capture(window, scenario, configure is null ? null : w => configure((SettingsWindow)w));
        }
        finally
        {
            if (temporary && Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    public void ScreenIdentification(string scenario) => Capture(new ScreenIdentificationWindow(1, 0, 0, 480, 320), scenario);

    public void PanelTooltip(string scenario, IScreenshotFixture fixture)
    {
        var model = new QuickSwitchPanelViewModel(fixture, fixture.CreatePorts(), new SafeEndpointWriter(fixture), new SafeProbe());
        model.Refresh();
        var window = new QuickSwitchPanelWindow(model, _ => { }, () => { });
        window.FitToContent();
        CaptureTooltip(window, scenario, w => FindAll<Button>(w).First(button => Equals(button.ToolTip, "試音")));
    }

    public void PortTooltip(string scenario, IScreenshotFixture fixture, string portId)
    {
        var configuration = new AudioSwitchConfiguration { Ports = fixture.CreatePorts(), Aliases = new AliasStore(), Settings = new AudioSwitchSettings(), Errors = [] };
        var directory = Path.Combine(Path.GetTempPath(), "AudioSwitch.Screenshots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var window = new SettingsWindow(fixture, fixture, new SafeProbe(), new SafeEndpointWriter(fixture), new SafeNameWriter(fixture),
                new SafeHotkeyService(), IntPtr.Zero, configuration, ProgramEntry.FixtureMachine, directory, () => { }, new SafeAutostartService(), 1);
            CaptureTooltip(window, scenario, w => FindAll<Border>(((SettingsWindow)w).PortsPage.Diagram).First(border => Equals(border.Tag, portId)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    void CaptureTooltip(Window window, string scenario, Func<Window, FrameworkElement> target)
    {
        Show(window);
        var element = target(window);
        var tip = new ToolTip { Content = element.ToolTip, PlacementTarget = element, Placement = PlacementMode.Bottom };
        element.ToolTip = tip;
        tip.IsOpen = true;
        Settle();
        // Render the target with its tooltip below it, as the user sees it on hover.
        var origin = element.TranslatePoint(new Point(0, 0), window);
        var tipRoot = (FrameworkElement)PresentationSource.FromVisual(tip)!.RootVisual;
        var width = Math.Max(element.ActualWidth, tipRoot.ActualWidth) + 32;
        var height = element.ActualHeight + tipRoot.ActualHeight + 40;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Background(), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(window) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, Viewbox = new Rect(origin.X - 16, origin.Y - 16, width, element.ActualHeight + 24), ViewboxUnits = BrushMappingMode.Absolute },
                null, new Rect(0, 0, width, element.ActualHeight + 24));
            context.DrawRectangle(new VisualBrush(tipRoot) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(16, element.ActualHeight + 28, tipRoot.ActualWidth, tipRoot.ActualHeight));
        }
        Save(visual, width, height, scenario);
        tip.IsOpen = false;
        window.Close();
    }

    void Capture(Window window, string scenario, Action<Window>? configure = null)
    {
        Show(window);
        configure?.Invoke(window);
        Settle();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
            context.DrawRectangle(Background(), null, bounds);
            context.DrawRectangle(new VisualBrush(window), null, bounds);
        }
        Save(visual, window.ActualWidth, window.ActualHeight, scenario);
        window.Close();
    }

    void Show(Window window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Show();
        Settle();
    }

    // Lets bindings, layout and control animations (e.g. tree expander chevrons) finish before rendering.
    void Settle()
    {
        var until = DateTime.UtcNow.AddMilliseconds(600);
        while (DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(30);
        }
    }

    Brush Background()
    {
        var brush = new SolidColorBrush(theme == "dark" ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243));
        brush.Freeze();
        return brush;
    }

    void Save(Visual visual, double width, double height, string scenario)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(width * scale)), Math.Max(1, (int)Math.Ceiling(height * scale)),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var path = Path.Combine(root, scenario, $"{theme}-{scaleName}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        Console.WriteLine(path);
    }

    static IEnumerable<T> FindAll<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindAll<T>(child)) yield return descendant;
        }
    }
}
