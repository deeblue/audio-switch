using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;
using AudioSwitch.Presentation;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTreeView = System.Windows.Controls.TreeView;

namespace AudioSwitch.App;

internal sealed class SettingsWindow : Window
{
    private readonly IAudioTopologySnapshotSource _snapshots;
    private readonly IAudioEndpointSource _endpoints;
    private readonly IAudioProbe _probe;
    private readonly IDefaultEndpointWriter _writer;
    private readonly IEndpointNameWriter _nameWriter;
    private readonly AudioSwitchConfiguration _configuration;
    private readonly string _dataDirectory;
    private readonly Action _identifyScreens;
    private readonly PortDiagramViewModel _diagram;
    private readonly CalibrationWorkflowViewModel _calibration;
    private readonly IHotkeyService _hotkeyService;
    private readonly IntPtr _hotkeyTarget;
    private readonly GeneralSettingsViewModel _general;
    private readonly HotkeySettingsViewModel _hotkeys;
    private readonly WpfTreeView _deviceTree = new() { MinWidth = 260 };
    private readonly StackPanel _deviceDetails = new() { Margin = new Thickness(12) };
    private readonly WpfListBox _portList = new() { MinWidth = 280 };
    private readonly WpfComboBox _portPicker = new() { MinWidth = 220 };
    private readonly WpfTextBox _portNameEditor = new() { Margin = new Thickness(3) };
    private readonly TextBlock _calibrationStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly WpfTextBox _calibrationAlias = new() { Margin = new Thickness(3), ToolTip = "校準偵測到音訊端點時可選填別名" };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(6) };
    private readonly Dictionary<string, Border> _diagramZones = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _peakTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private WpfButton? _listenButton;
    private Snapshot? _snapshot;
    private TopologyTree? _tree;
    private AudioEndpoint? _selectedEndpoint;
    private string? _highlightedInterfaceId;
    private bool _listening;

    public SettingsWindow(IAudioTopologySnapshotSource snapshots, IAudioEndpointSource endpoints, IAudioProbe probe,
        IDefaultEndpointWriter writer, IEndpointNameWriter nameWriter, IHotkeyService hotkeyService, IntPtr hotkeyTarget, AudioSwitchConfiguration configuration,
        MachineIdentity machine, string dataDirectory, Action identifyScreens)
    {
        _snapshots = snapshots; _endpoints = endpoints; _probe = probe; _writer = writer; _nameWriter = nameWriter;
        _configuration = configuration; _dataDirectory = dataDirectory; _identifyScreens = identifyScreens; _hotkeyService = hotkeyService; _hotkeyTarget = hotkeyTarget;
        _diagram = new PortDiagramViewModel(configuration.Ports, machine);
        _calibration = new CalibrationWorkflowViewModel(snapshots, configuration.Ports);
        _hotkeys = new HotkeySettingsViewModel(_hotkeyService, endpoints, writer, configuration.Settings);
        _general = new GeneralSettingsViewModel(new WindowsAutostartService(), configuration.Settings);
        Title = "AudioSwitch 設定"; Width = 920; Height = 660; MinWidth = 760; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var tabs = new System.Windows.Controls.TabControl { Margin = new Thickness(12) };
        tabs.Items.Add(new TabItem { Header = "裝置", Content = BuildDevicePage() });
        tabs.Items.Add(new TabItem { Header = "接口定義", Content = BuildPortPage() });
        tabs.Items.Add(new TabItem { Header = "快捷鍵", Content = BuildHotkeyPage() });
        tabs.Items.Add(new TabItem { Header = "一般", Content = BuildGeneralPage() });
        tabs.SelectionChanged += async (_, _) => await StopListeningAsync();
        Content = tabs;
        _peakTimer.Tick += (_, _) => UpdatePeak();
        Loaded += (_, _) => RefreshDevices();
        Closing += async (_, _) => { await StopListeningAsync(); Save(); };
    }

    private UIElement BuildDevicePage()
    {
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        Grid.SetColumn(_deviceTree, 0); Grid.SetColumn(_deviceDetails, 1); layout.Children.Add(_deviceTree); layout.Children.Add(new ScrollViewer { Content = _deviceDetails, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _deviceDetails.Children.Add(Button("識別螢幕", (_, _) => _identifyScreens()));
        _deviceTree.SelectedItemChanged += async (_, _) => { await StopListeningAsync(); if (_deviceTree.SelectedItem is TreeViewItem { Tag: string id }) try { SelectDevice(id); } catch (Exception exception) { _error.Text = exception.Message; } };
        return layout;
    }

    private UIElement BuildPortPage()
    {
        var root = new DockPanel(); var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(4) };
        var type = new WpfComboBox { Width = 120, ItemsSource = new[] { "USB-A", "USB-C", "HDMI", "3.5mm" }, SelectedIndex = 0, Margin = new Thickness(3) };
        var edge = new WpfComboBox { Width = 100, ItemsSource = new[] { "左側", "右側", "後側", "前側" }, SelectedIndex = 0, Margin = new Thickness(3) };
        row.Children.Add(type); row.Children.Add(edge); row.Children.Add(Button("新增接口", (_, _) => { var p = _diagram.AddInterface((PortConnectorType)type.SelectedIndex, (PortEdge)edge.SelectedIndex, _diagram.Interfaces.Count(item => item.Edge == new[] { "left", "right", "rear", "front" }[edge.SelectedIndex])); RefreshPorts(); SelectPort(p.Id); }));
        row.Children.Add(Button("插入裝置後建立接口", (_, _) => { _calibration.BeginBeforePortPlacement(); _calibrationStatus.Text = "現在插入一個裝置，再按「完成偵測」。"; }));
        DockPanel.SetDock(row, Dock.Top); root.Children.Add(row);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel(); left.Children.Add(new TextBlock { Text = "筆電接口示意（俯視）", FontWeight = FontWeights.Bold, Margin = new Thickness(5) });
        var palette = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        foreach (var connector in new[] { "USB-A", "USB-C", "HDMI", "3.5mm" }) { var item = Button(connector, (_, _) => { }); item.Tag = connector; item.PreviewMouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) System.Windows.DragDrop.DoDragDrop(item, connector, System.Windows.DragDropEffects.Copy); }; palette.Children.Add(item); }
        left.Children.Add(palette);
        var diagram = new Grid { MinHeight = 190, Margin = new Thickness(10) };
        diagram.RowDefinitions.Add(new RowDefinition()); diagram.RowDefinitions.Add(new RowDefinition()); diagram.RowDefinitions.Add(new RowDefinition());
        diagram.ColumnDefinitions.Add(new ColumnDefinition()); diagram.ColumnDefinitions.Add(new ColumnDefinition()); diagram.ColumnDefinitions.Add(new ColumnDefinition());
        Border DropZone(string edgeName, int rowIndex, int columnIndex)
        {
            var zoneContent = new StackPanel(); zoneContent.Children.Add(new TextBlock { Text = edgeName, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, FontWeight = FontWeights.Bold });
            var zone = new Border { BorderBrush = System.Windows.Media.Brushes.SlateGray, BorderThickness = new Thickness(1), Padding = new Thickness(5), Background = System.Windows.Media.Brushes.AliceBlue, AllowDrop = true, Child = zoneContent };
            _diagramZones[edgeName.ToLowerInvariant()] = zone;
            zone.Drop += (_, e) => { var edgeValue = edgeName switch { "左側" => PortEdge.Left, "右側" => PortEdge.Right, "後側" => PortEdge.Rear, _ => PortEdge.Front }; var edgeKey = edgeValue.ToString().ToLowerInvariant(); var orderIndex = DropOrder(edgeValue, zone, e, _diagram.Interfaces.Count(port => port.Edge == edgeKey)); if (e.Data.GetData(typeof(string)) is string connector && TryConnector(connector, out var parsed)) { var created = _diagram.AddInterface(parsed, edgeValue, orderIndex); RefreshPorts(); SelectPort(created.Id); } else if (e.Data.GetData(typeof(string)) is string existingId && _diagram.Interfaces.Any(port => port.Id == existingId)) { _diagram.MoveInterface(existingId, edgeValue, orderIndex); RefreshPorts(); SelectPort(existingId); } };
            Grid.SetRow(zone, rowIndex); Grid.SetColumn(zone, columnIndex); diagram.Children.Add(zone); return zone;
        }
        DropZone("後側", 0, 1); DropZone("左側", 1, 0); DropZone("右側", 1, 2); DropZone("前側", 2, 1);
        var chassis = new Border { BorderBrush = System.Windows.Media.Brushes.SlateGray, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(18), Padding = new Thickness(12), Margin = new Thickness(5), Child = new TextBlock { Text = "螢幕／筆電機身", HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; Grid.SetRow(chassis, 1); Grid.SetColumn(chassis, 1); diagram.Children.Add(chassis); left.Children.Add(diagram);
        _portList.DisplayMemberPath = nameof(PortDefinition.Name); left.Children.Add(_portList); _portList.PreviewMouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && _portList.SelectedItem is PortDefinition dragged) System.Windows.DragDrop.DoDragDrop(_portList, dragged.Id, System.Windows.DragDropEffects.Move); }; _portList.SelectionChanged += (_, _) => { if (_portList.SelectedItem is PortDefinition p) SelectPort(p.Id); };
        Grid.SetColumn(left, 0); body.Children.Add(left);
        var editor = new StackPanel { Margin = new Thickness(8) }; editor.Children.Add(new TextBlock { Text = "接口設定與校準", FontWeight = FontWeights.Bold }); editor.Children.Add(_portPicker);
        editor.Children.Add(Button("開始校準（先選接口，再插入）", (_, _) => BeginCalibrationAtSelectedPort()));
        editor.Children.Add(Button("完成裝置偵測", (_, _) => CompleteCalibrationDetection()));
        editor.Children.Add(Button("套用到選定接口", (_, _) => ApplyCalibrationToSelectedPort()));
        editor.Children.Add(Button("取消校準", (_, _) => { _calibration.Cancel(); _calibrationStatus.Text = "已取消。"; }));
        editor.Children.Add(new TextBlock { Text = "音訊端點別名（選填）" }); editor.Children.Add(_calibrationAlias);
        editor.Children.Add(new TextBlock { Text = "接口名稱（預填文字已全選，可直接輸入）" }); editor.Children.Add(_portNameEditor);
        editor.Children.Add(Button("儲存名稱", (_, _) => { if (_portPicker.SelectedValue is string id) { _diagram.RenameInterface(id, _portNameEditor.Text); RefreshPorts(); } }));
        var moveEdge = new WpfComboBox { ItemsSource = new[] { "左側", "右側", "後側", "前側" }, SelectedIndex = 0, Margin = new Thickness(3) }; var moveOrder = new WpfComboBox { ItemsSource = Enumerable.Range(0, 16).Select(index => index + 1), SelectedIndex = 0, Margin = new Thickness(3) };
        editor.Children.Add(new TextBlock { Text = "移動接口（邊與順序）" }); editor.Children.Add(moveEdge); editor.Children.Add(moveOrder);
        editor.Children.Add(Button("移動", (_, _) => { if (_portPicker.SelectedValue is string id) { _diagram.MoveInterface(id, (PortEdge)moveEdge.SelectedIndex, moveOrder.SelectedIndex); RefreshPorts(); SelectPort(id); } }));
        editor.Children.Add(Button("刪除接口", (_, _) => { if (_portPicker.SelectedValue is string id) { _diagram.DeleteInterface(id); RefreshPorts(); } }));
        editor.Children.Add(_calibrationStatus); Grid.SetColumn(editor, 1); body.Children.Add(editor);
        DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error); root.Children.Add(body); RefreshPorts(); return root;
    }

    private UIElement BuildHotkeyPage()
    {
        var panel = new StackPanel { Margin = new Thickness(18) }; panel.Children.Add(new TextBlock { Text = "點欄位後按下組合鍵（例如 Ctrl + Alt + F1）；留白代表不設定。" });
        AddHotkeyField(panel, "播放端點循環", _hotkeys.PlaybackHotkey, value => _hotkeys.PlaybackHotkey = value);
        AddHotkeyField(panel, "錄音端點循環", _hotkeys.RecordingHotkey, value => _hotkeys.RecordingHotkey = value);
        panel.Children.Add(Button("套用快捷鍵", (_, _) => { if (_hotkeys.Register(_hotkeyTarget)) Save(); _error.Text = _hotkeys.ErrorMessage ?? "快捷鍵已套用。"; })); panel.Children.Add(_error); return panel;
    }

    private UIElement BuildGeneralPage()
    {
        var panel = new StackPanel { Margin = new Thickness(18) }; var check = new System.Windows.Controls.CheckBox { Content = "登入 Windows 時啟動 AudioSwitch", IsChecked = _general.AutostartEnabled };
        check.Checked += (_, _) => { _general.AutostartEnabled = true; _error.Text = _general.ErrorMessage ?? ""; Save(); }; check.Unchecked += (_, _) => { _general.AutostartEnabled = false; _error.Text = _general.ErrorMessage ?? ""; Save(); };
        panel.Children.Add(check); panel.Children.Add(Button("識別所有螢幕", (_, _) => _identifyScreens())); panel.Children.Add(_error); return panel;
    }

    private void AddHotkeyField(System.Windows.Controls.Panel panel, string label, string? value, Action<string?> set)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4) }); var box = new WpfTextBox { Text = value ?? "", IsReadOnly = true, Margin = new Thickness(4), Tag = set };
        box.PreviewKeyDown += (_, e) => { e.Handled = true; if (e.Key == Key.Escape) { box.Text = ""; ((Action<string?>)box.Tag!)(null); return; } var mods = Keyboard.Modifiers; var parts = new List<string>(); if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl"); if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt"); if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift"); if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win"); if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return; parts.Add(e.Key.ToString()); box.Text = string.Join("+", parts); ((Action<string?>)box.Tag!)(box.Text); };
        panel.Children.Add(box);
    }

    private void RefreshDevices()
    {
        try
        {
            _snapshot = _snapshots.Capture(); _tree = TopologyBuilder.Build(_snapshot, _configuration.Ports); _deviceTree.Items.Clear();
            var rootItems = _tree.Roots.ToDictionary(root => root.Id, root => new TreeViewItem { Header = root.Name, IsExpanded = true }, StringComparer.Ordinal);
            foreach (var item in rootItems.Values) _deviceTree.Items.Add(item);
            foreach (var endpoint in _tree.Endpoints)
            {
                var rootId = FindEndpointRoot(_tree.Roots, endpoint.EndpointId);
                if (rootId is null || !rootItems.TryGetValue(rootId, out var rootItem)) continue;
                var audioEndpoint = _snapshot.Endpoints.FirstOrDefault(item => item.EndpointId.Equals(endpoint.EndpointId, StringComparison.OrdinalIgnoreCase));
                if (audioEndpoint is null) continue;
                var byId = _snapshot.Devnodes.ToDictionary(node => node.InstanceId, StringComparer.OrdinalIgnoreCase);
                var chain = new List<DeviceNode>(); var currentId = audioEndpoint.DevnodeId; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (!string.IsNullOrWhiteSpace(currentId) && seen.Add(currentId) && byId.TryGetValue(currentId, out var current)) { chain.Add(current); currentId = current.ParentId; }
                var usbAncestors = chain.Where(node => node.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) && !node.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase) && !node.InstanceId.Contains("ROOT_HUB", StringComparison.OrdinalIgnoreCase)).Reverse().ToArray();
                ItemsControl parent = rootItem;
                foreach (var node in usbAncestors)
                {
                    var existing = parent.Items.OfType<TreeViewItem>().FirstOrDefault(child => child.Tag is string id && id.Equals(node.InstanceId, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        var hasUsbChildren = _snapshot.Devnodes.Any(child => child.ParentId?.Equals(node.InstanceId, StringComparison.OrdinalIgnoreCase) == true && child.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) && !child.InstanceId.Contains("ROOT_HUB", StringComparison.OrdinalIgnoreCase) && !child.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase));
                        var label = hasUsbChildren ? "Dock／Hub（未定義）" : node.Name ?? "USB 裝置";
                        existing = new TreeViewItem { Header = label, Tag = node.InstanceId, IsExpanded = true }; parent.Items.Add(existing);
                    }
                    parent = existing;
                }
                parent.Items.Add(new TreeViewItem { Header = audioEndpoint.FriendlyName ?? "（無名稱）", Tag = endpoint.EndpointId });
            }
            foreach (var display in _snapshot.DisplayPaths)
            {
                var group = _tree.DisplayGroups.FirstOrDefault(item => item.PathId == display.PathId);
                var rootId = group?.InterfaceId ?? "未辨識";
                if (rootItems.TryGetValue(rootId, out var root)) root.Items.Add(new TreeViewItem { Header = display.TargetName ?? "顯示器", IsExpanded = true });
            }
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private static string? FindEndpointRoot(IEnumerable<TopologyRoot> roots, string endpointId) => roots.FirstOrDefault(root => ContainsEndpoint(root.Children, endpointId))?.Id;
    private static bool ContainsEndpoint(IEnumerable<TopologyNode> nodes, string endpointId) => nodes.Any(node => node.EndpointId?.Equals(endpointId, StringComparison.OrdinalIgnoreCase) == true || ContainsEndpoint(node.Children, endpointId));

    private static void AddNode(ItemsControl parent, TopologyNode node)
    {
        var item = new TreeViewItem { Header = node.Name, Tag = node.EndpointId, IsExpanded = true }; foreach (var child in node.Children) AddNode(item, child); parent.Items.Add(item);
    }

    private void SelectDevice(string endpointId)
    {
        if (_snapshot is null) return; _selectedEndpoint = _snapshot.Endpoints.FirstOrDefault(endpoint => endpoint.EndpointId.Equals(endpointId, StringComparison.OrdinalIgnoreCase)); if (_selectedEndpoint is null) return;
        _deviceDetails.Children.Clear(); _deviceDetails.Children.Add(Button("識別螢幕", (_, _) => _identifyScreens())); _deviceDetails.Children.Add(new TextBlock { Text = _selectedEndpoint.FriendlyName ?? endpointId, FontSize = 18, FontWeight = FontWeights.Bold });
        AddDetail("Flow / State", $"{_selectedEndpoint.Flow} / {_selectedEndpoint.State}"); AddDetail("Endpoint ID", endpointId); AddDetail("裝置節點", _selectedEndpoint.DevnodeId ?? "未解析");
        var topologyEndpoint = _tree?.Endpoints.FirstOrDefault(item => item.EndpointId.Equals(endpointId, StringComparison.OrdinalIgnoreCase));
        AddDetail("對應接口", topologyEndpoint?.InterfaceId is { } interfaceId ? _configuration.Ports.FindInterface(interfaceId)?.Name ?? interfaceId : "未對應（拓樸位置不足以判定）");
        _highlightedInterfaceId = topologyEndpoint?.InterfaceId; RefreshPortDiagram();
        var nodes = _snapshot.Devnodes.ToDictionary(item => item.InstanceId, StringComparer.OrdinalIgnoreCase); var pldRows = new List<string>(); var currentId = _selectedEndpoint.DevnodeId; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrWhiteSpace(currentId) && seen.Add(currentId) && nodes.TryGetValue(currentId, out var node)) { if (node.PhysicalDeviceLocationRaw is { Length: > 0 } raw) pldRows.Add($"{node.InstanceId}: {Convert.ToHexString(raw)}"); currentId = node.ParentId; }
        AddDetail("祖先鏈 _PLD 原始資料", pldRows.Count == 0 ? "不可用" : string.Join(Environment.NewLine, pldRows));
        var alias = new WpfTextBox { Text = _configuration.Aliases.OriginalDescriptions.ContainsKey(endpointId) ? _nameWriter.ReadDeviceDescription(endpointId) : _selectedEndpoint.FriendlyName ?? "", Margin = new Thickness(3) };
        _deviceDetails.Children.Add(new TextBlock { Text = "端點別名（Windows 裝置描述）" }); _deviceDetails.Children.Add(alias);
        _deviceDetails.Children.Add(Button("套用別名", (_, _) => ApplyAlias(endpointId, alias.Text))); _deviceDetails.Children.Add(Button("清除別名", (_, _) => ClearAlias(endpointId)));
        if (_selectedEndpoint.Flow.Equals("render", StringComparison.OrdinalIgnoreCase)) _deviceDetails.Children.Add(Button("播放試音", async (_, _) => { try { await _probe.PlayTestToneAsync(endpointId, TimeSpan.FromSeconds(2)); } catch (Exception ex) { _error.Text = ex.Message; } }));
        if (_selectedEndpoint.Flow.Equals("capture", StringComparison.OrdinalIgnoreCase)) { _listenButton = Button(_listening ? "停止聽" : "聽", async (_, _) => { if (_listening) await StopListeningAsync(); else StartListening(endpointId); }); _deviceDetails.Children.Add(_listenButton); }
        var endpointPort = new WpfComboBox { ItemsSource = _diagram.Interfaces.Select(port => new { port.Id, port.Name }).ToArray(), DisplayMemberPath = "Name", SelectedValuePath = "Id", Margin = new Thickness(3) };
        _deviceDetails.Children.Add(new TextBlock { Text = "這個裝置插在…（以目前裝置路徑校準）" }); _deviceDetails.Children.Add(endpointPort);
        _deviceDetails.Children.Add(Button("校準目前路徑", (_, _) =>
        {
            try
            {
                if (endpointPort.SelectedValue is not string portId) { _error.Text = "請先選擇接口。"; return; }
                var current = _snapshots.Capture(); var result = _configuration.Ports.CalibrateExistingEndpoint(portId, current, endpointId);
                _error.Text = result.Status == "ambiguous" ? "此接口已有相同傳輸類型的另一條路徑，請選擇其他接口。" : result.Status == "no_candidate" ? "目前裝置沒有可校準的連接路徑。" : $"已校準：{string.Join("、", result.Transports)}";
                if (result.Status == "calibrated") { Save(); RefreshPorts(); RefreshDevices(); }
            }
            catch (Exception exception) { _error.Text = exception.Message; }
        }));
        _deviceDetails.Children.Add(Button("重新整理拓樸", (_, _) => RefreshDevices()));
    }

    private void AddDetail(string label, string value) => _deviceDetails.Children.Add(new TextBlock { Text = $"{label}: {value}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3) });
    private void ApplyAlias(string endpointId, string alias)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(alias)) { _error.Text = "別名不可空白；請使用「清除別名」。"; return; }
            var members = AliasEndpointGrouping.GetMembers(_tree!.Endpoints.First(item => item.EndpointId == endpointId), _tree.Endpoints);
            var originals = members.ToDictionary(id => id, id => _configuration.Aliases.OriginalDescriptions.GetValueOrDefault(id) ?? _nameWriter.ReadDeviceDescription(id), StringComparer.OrdinalIgnoreCase);
            var changed = new List<string>();
            try { foreach (var member in members) { _nameWriter.WriteDeviceDescription(member, alias); changed.Add(member); } }
            catch (Exception writeError)
            {
                var restoreErrors = new List<string>();
                foreach (var member in changed.AsEnumerable().Reverse())
                    try { _nameWriter.WriteDeviceDescription(member, originals[member]); } catch (Exception restoreError) { restoreErrors.Add(restoreError.Message); }
                throw restoreErrors.Count == 0 ? writeError : new AggregateException("別名寫入失敗且部分原名稱還原失敗。", [writeError, .. restoreErrors.Select(error => new InvalidOperationException(error))]);
            }
            _configuration.Aliases.SetAlias(members, originals); Save(); RefreshDevices();
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }
    private void ClearAlias(string endpointId)
    {
        try
        {
            var selected = _tree!.Endpoints.First(item => item.EndpointId == endpointId); var ids = AliasEndpointGrouping.GetMembers(selected, _tree.Endpoints);
            foreach (var id in ids) if (_configuration.Aliases.OriginalDescriptions.TryGetValue(id, out var original)) _nameWriter.WriteDeviceDescription(id, original);
            _configuration.Aliases.ClearAlias(ids); Save(); RefreshDevices();
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private void StartListening(string endpointId) { try { _probe.StartPeakSampling(endpointId); _listening = true; if (_listenButton is not null) _listenButton.Content = "停止聽"; _peakTimer.Start(); _error.Text = "收音峰值：0%"; } catch (Exception ex) { _error.Text = ex.Message; } }
    private void UpdatePeak() { try { if (_selectedEndpoint is not null) _error.Text = $"收音峰值：{_probe.ReadPeakLevel(_selectedEndpoint.EndpointId):P0}"; } catch (Exception ex) { _error.Text = ex.Message; _ = StopListeningAsync(); } }
    private async Task StopListeningAsync() { _peakTimer.Stop(); if (!_listening) return; _listening = false; if (_listenButton is not null) _listenButton.Content = "聽"; try { await _probe.StopPeakSamplingAsync(); } catch (Exception ex) { _error.Text = ex.Message; } }

    private void RefreshPorts()
    {
        var selected = _portPicker.SelectedValue as string; _portPicker.ItemsSource = _diagram.Interfaces.Select(port => new { port.Id, port.Name }).ToArray(); _portPicker.DisplayMemberPath = "Name"; _portPicker.SelectedValuePath = "Id"; _portList.ItemsSource = null; _portList.ItemsSource = _diagram.Interfaces.ToArray();
        if (selected is not null) _portPicker.SelectedValue = selected;
        _portPicker.SelectionChanged -= OnPortPickerSelectionChanged; _portPicker.SelectionChanged += OnPortPickerSelectionChanged;
        if (_portPicker.SelectedValue is string selectedId && _configuration.Ports.FindInterface(selectedId) is { } selectedPort) _portNameEditor.Text = selectedPort.Name;
        if (_portPicker.SelectedValue is string id && _configuration.Ports.FindInterface(id) is { } port) _calibrationStatus.Text = _diagram.GetCalibrationStatus(port);
        RefreshPortDiagram();
    }
    private void OnPortPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_portPicker.SelectedValue is not string id || _configuration.Ports.FindInterface(id) is not { } port) return;
        _portNameEditor.Text = port.Name; _portNameEditor.SelectAll();
    }

    private void CompleteCalibrationDetection()
    {
        try
        {
            if (_calibration.State != CalibrationWorkflowState.AwaitingDevice) { _calibrationStatus.Text = "請先開始校準，再接入一個裝置。"; return; }
            var result = _calibration.CompleteDeviceStep();
            _calibrationStatus.Text = result?.Status == "ambiguous" ? "請只插入一個裝置後重試" : _calibration.ErrorMessage ?? (result?.Status ?? "已偵測，請選擇或新增對應接口，再套用校準。");
            if (result?.Status == "calibrated") FinishCalibration();
        }
        catch (Exception exception) { _calibrationStatus.Text = exception.Message; }
    }

    private void BeginCalibrationAtSelectedPort()
    {
        try
        {
            if (_portPicker.SelectedValue is not string id || _configuration.Ports.FindInterface(id) is not { } port)
            { _calibrationStatus.Text = "請先選擇接口。"; return; }
            if (port.Type == "audio-jack") { _calibrationStatus.Text = "3.5mm 由 KS jack 自動對應，無需插入校準。"; return; }
            _calibration.BeginAtPort(id); _calibrationStatus.Text = "請接入一個裝置，完成後按「完成裝置偵測」。";
        }
        catch (Exception exception) { _calibrationStatus.Text = exception.Message; }
    }

    private void ApplyCalibrationToSelectedPort()
    {
        try
        {
            if (_calibration.State != CalibrationWorkflowState.AwaitingPortPlacement || _portPicker.SelectedValue is not string id)
            { _calibrationStatus.Text = "請先偵測新裝置，再選擇接口。"; return; }
            var result = _calibration.SelectPort(id);
            _calibrationStatus.Text = result?.Status == "ambiguous" ? "請只插入一個裝置後重試" : _calibration.ErrorMessage ?? result?.Status ?? "校準失敗。";
            if (result?.Status == "calibrated") FinishCalibration();
        }
        catch (Exception exception) { _calibrationStatus.Text = exception.Message; }
    }

    private void FinishCalibration()
    {
        RefreshPorts(); RefreshDevices();
        if (!string.IsNullOrWhiteSpace(_calibrationAlias.Text))
            foreach (var endpoint in _calibration.NewAudioEndpoints) ApplyAlias(endpoint.EndpointId, _calibrationAlias.Text);
        Save();
    }
    private void RefreshPortDiagram()
    {
        foreach (var (edgeName, zone) in _diagramZones)
        {
            if (zone.Child is not StackPanel content) continue;
            while (content.Children.Count > 1) content.Children.RemoveAt(1);
            var highlighted = false;
            foreach (var port in _diagram.Interfaces.Where(item => string.Equals(item.Edge, edgeName, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Order))
            {
                var selected = port.Id == _highlightedInterfaceId;
                highlighted |= selected;
                content.Children.Add(new TextBlock { Text = port.Name, TextWrapping = TextWrapping.Wrap, Background = selected ? System.Windows.Media.Brushes.Gold : System.Windows.Media.Brushes.Transparent, ToolTip = _diagram.GetCalibrationStatus(port) });
            }
            zone.BorderBrush = highlighted ? System.Windows.Media.Brushes.DarkOrange : System.Windows.Media.Brushes.SlateGray;
            zone.BorderThickness = highlighted ? new Thickness(3) : new Thickness(1);
        }
    }
    private static bool TryConnector(string value, out PortConnectorType connector)
    {
        connector = value switch { "USB-A" => PortConnectorType.UsbA, "USB-C" => PortConnectorType.UsbC, "HDMI" => PortConnectorType.Hdmi, "3.5mm" => PortConnectorType.AudioJack, _ => (PortConnectorType)(-1) };
        return Enum.IsDefined(connector);
    }
    private static int DropOrder(PortEdge edge, Border zone, System.Windows.DragEventArgs e, int count)
    {
        if (count == 0) return 0;
        var point = e.GetPosition(zone);
        var ratio = edge is PortEdge.Left or PortEdge.Right
            ? point.Y / Math.Max(1, zone.ActualHeight)
            : point.X / Math.Max(1, zone.ActualWidth);
        return Math.Clamp((int)Math.Round(ratio * count), 0, count);
    }
    private void SelectPort(string id) { _portPicker.SelectedValue = id; RefreshPorts(); _portNameEditor.SelectAll(); _portNameEditor.Focus(); }
    private void Save() { try { ConfigurationStore.Save(_dataDirectory, _configuration); } catch (Exception ex) { _error.Text = ex.Message; } }
    private static WpfButton Button(string text, RoutedEventHandler action) { var button = new WpfButton { Content = text, Margin = new Thickness(3), Padding = new Thickness(7, 4, 7, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left }; button.Click += action; return button; }

}

