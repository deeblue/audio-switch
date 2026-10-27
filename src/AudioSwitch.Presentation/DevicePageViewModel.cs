using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

/// <summary>What a row of the device tree is.</summary>
public enum DeviceNodeKind
{
    Port,
    Category,
    DockHub,
    Device,
    Display,
    Playback,
    Recording
}

/// <summary>A row of the settings device tree.</summary>
public sealed class DeviceNodeViewModel
{
    public required string Name { get; init; }
    public required DeviceNodeKind Kind { get; init; }
    public string? EndpointId { get; init; }
    public string? ConnectorType { get; init; }
    public string? InterfaceId { get; init; }
    public string? DockKey { get; init; }
    /// <summary>For a category root such as 內建, its stable ID; the name is translated.</summary>
    public string? CategoryId { get; init; }
    public string GroupName { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
    public IReadOnlyList<string> PhysicalDeviceLocationRaw { get; init; } = [];
    public ObservableCollection<DeviceNodeViewModel> Children { get; } = [];
    public string KindLabel => Loc.T(Kind switch
    {
        DeviceNodeKind.Port => "接口",
        DeviceNodeKind.Category => "分類",
        DeviceNodeKind.DockHub => "Dock／Hub",
        DeviceNodeKind.Device => "USB 裝置",
        DeviceNodeKind.Display => "螢幕",
        DeviceNodeKind.Playback => IsDefault ? "播放裝置（目前預設）" : "播放裝置",
        DeviceNodeKind.Recording => IsDefault ? "錄音裝置（目前預設）" : "錄音裝置",
        _ => string.Empty
    });
}

/// <summary>The settings Devices page: the full device tree, the selected item's details and actions (set as default, test tone, level meter, endpoint and dock names, port assignment), and the current defaults for the laptop drawing.</summary>
public sealed class DevicePageViewModel : INotifyPropertyChanged
{
    private readonly IAudioTopologySnapshotSource _snapshotSource;
    private readonly IDefaultEndpointWriter _endpointWriter;
    private readonly IAudioProbe _probe;
    private readonly PortTable _portTable;
    private readonly Dictionary<string, string> _dockNames;
    private readonly Action _saveDockNames;
    private Snapshot? _snapshot;
    private TopologyTree? _tree;
    private DeviceNodeViewModel? _selectedNode;
    private string? _message;
    private bool _isError;
    private bool _isPlayingTone;
    private string _dockNameText = string.Empty;
    private ActiveDevices _active = ActiveDevices.None;

    public DevicePageViewModel(IAudioTopologySnapshotSource snapshotSource, IDefaultEndpointWriter endpointWriter,
        IAudioProbe probe, PortTable portTable, DeviceSettingsViewModel settings,
        Dictionary<string, string>? dockNames = null, Action? saveDockNames = null)
    {
        _snapshotSource = snapshotSource;
        _endpointWriter = endpointWriter;
        _probe = probe;
        _portTable = portTable;
        _dockNames = dockNames ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _saveDockNames = saveDockNames ?? (() => { });
        Settings = settings;
        Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DeviceSettingsViewModel.ErrorMessage) && Settings.ErrorMessage is { } error)
                ShowMessage(error, isError: true);
            if (args.PropertyName is nameof(DeviceSettingsViewModel.IsListening) or nameof(DeviceSettingsViewModel.ListeningEndpointId))
                OnPropertyChanged(nameof(IsListening));
        };
    }

    public DeviceSettingsViewModel Settings { get; }
    public ObservableCollection<DeviceNodeViewModel> Roots { get; } = [];
    public DeviceNodeViewModel? SelectedNode { get => _selectedNode; private set => Set(ref _selectedNode, value); }
    public string? Message { get => _message; private set => Set(ref _message, value); }
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }
    public bool IsPlayingTone { get => _isPlayingTone; private set => Set(ref _isPlayingTone, value); }
    public bool IsListening => Settings.IsListening && SelectedNode?.EndpointId is { } id &&
        string.Equals(Settings.ListeningEndpointId, id, StringComparison.OrdinalIgnoreCase);

    public bool HasSelection => SelectedNode is not null;
    public bool HasDevices => Roots.Count > 0;
    public bool IsEndpointSelected => SelectedNode?.Kind is DeviceNodeKind.Playback or DeviceNodeKind.Recording;
    public bool IsDockSelected => SelectedNode is { Kind: DeviceNodeKind.DockHub, DockKey: not null };
    public bool CanRestoreDockName => SelectedNode?.DockKey is { } key && _dockNames.ContainsKey(key);
    public string DockNameText { get => _dockNameText; set => Set(ref _dockNameText, value); }
    public bool CanTestTone => SelectedNode?.Kind == DeviceNodeKind.Playback && IsActive(SelectedEndpoint);
    public bool CanSetDefault => IsEndpointSelected && IsActive(SelectedEndpoint);
    public bool IsSelectedDefault => SelectedNode is { IsDefault: true, Kind: DeviceNodeKind.Playback or DeviceNodeKind.Recording };
    public bool CanListen => SelectedNode?.Kind == DeviceNodeKind.Recording && IsActive(SelectedEndpoint);
    public bool CanIdentifyScreen => SelectedNode?.Kind == DeviceNodeKind.Display || SelectedTopologyEndpoint?.Category == "B";
    public bool CanRestoreName => SelectedNode?.EndpointId is { } id && Settings.HasStoredOriginalName(id);
    public bool NeedsPortAssignment => SelectedTopologyEndpoint is { Category: "A", InterfaceId: null } && UsbPorts.Count > 0;
    public IReadOnlyList<PortDefinition> UsbPorts => _portTable.IsEnabled
        ? _portTable.Interfaces.Where(port => port.Type is "usb-a" or "usb-c").ToArray()
        : [];

    public string Title => SelectedNode?.Name ?? string.Empty;
    public string KindText => SelectedNode?.KindLabel ?? string.Empty;
    public string LocationText => SelectedNode?.GroupName ?? string.Empty;
    // Built-in speakers and microphones light up their fixed marker on the laptop drawing; a headphone jack does not.
    public string? SelectedInterfaceId => SelectedNode?.InterfaceId ??
        (SelectedTopologyEndpoint is { } endpoint && _snapshot is { } snapshot ? ActiveDevices.DiagramId(snapshot, endpoint) : null);

    /// <summary>Where the current default playback and recording devices are on the laptop drawing.</summary>
    public ActiveDevices Active { get => _active; private set => Set(ref _active, value); }
    public string StateText => SelectedEndpoint is { } endpoint ? StateLabel(endpoint.State) : string.Empty;
    public string TechnicalDetails => BuildTechnicalDetails();

    public event PropertyChangedEventHandler? PropertyChanged;

    private AudioEndpoint? SelectedEndpoint => SelectedNode?.EndpointId is { } id
        ? _snapshot?.Endpoints.FirstOrDefault(endpoint => endpoint.EndpointId.Equals(id, StringComparison.OrdinalIgnoreCase))
        : null;

    private TopologyEndpoint? SelectedTopologyEndpoint => SelectedNode?.EndpointId is { } id
        ? _tree?.Endpoints.FirstOrDefault(endpoint => endpoint.EndpointId.Equals(id, StringComparison.OrdinalIgnoreCase))
        : null;

    public void Refresh()
    {
        var selectedEndpointId = SelectedNode?.EndpointId;
        var selectedDockKey = SelectedNode?.DockKey;
        var selectedName = SelectedNode?.Name;
        try
        {
            _snapshot = _snapshotSource.Capture();
            _tree = TopologyBuilder.Build(_snapshot, _portTable, _dockNames);
            var renderDefault = _endpointWriter.GetDefaultEndpointId(AudioEndpointFlow.Render, AudioEndpointRole.Multimedia);
            var captureDefault = _endpointWriter.GetDefaultEndpointId(AudioEndpointFlow.Capture, AudioEndpointRole.Multimedia);
            Active = ActiveDevices.Find(_snapshot, _tree, renderDefault, captureDefault);
            Roots.Clear();
            foreach (var root in _tree.Roots)
            {
                var port = _portTable.IsEnabled ? _portTable.FindInterface(root.Id) : null;
                var rootNode = new DeviceNodeViewModel
                {
                    Name = root.Name,
                    Kind = port is null ? DeviceNodeKind.Category : DeviceNodeKind.Port,
                    ConnectorType = port?.Type,
                    InterfaceId = port?.Id,
                    CategoryId = port is null ? root.Id : null,
                    GroupName = root.Name
                };
                foreach (var child in root.Children)
                    rootNode.Children.Add(CreateNode(child, root.Name, port?.Id, renderDefault, captureDefault));
                if (rootNode.Children.Count > 0) Roots.Add(rootNode);
            }
            if (_snapshot.Errors.Count > 0)
                ShowMessage(string.Join(Loc.T("；"), _snapshot.Errors.SelectMany(error => error.Values)), isError: true);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
        var reselected = selectedEndpointId is not null
            ? Flatten(Roots).FirstOrDefault(node => string.Equals(node.EndpointId, selectedEndpointId, StringComparison.OrdinalIgnoreCase))
            : selectedDockKey is not null
                ? Flatten(Roots).FirstOrDefault(node => string.Equals(node.DockKey, selectedDockKey, StringComparison.OrdinalIgnoreCase))
                : Flatten(Roots).FirstOrDefault(node => node.EndpointId is null && node.Name == selectedName);
        SelectedNode = reselected;
        // The selected device is gone, for example unplugged: release what was opened for it, such as the level-meter stream.
        // The tree ignores the empty selection a rebuild produces, so this cannot be left to the view.
        if (reselected is null && selectedEndpointId is not null) _ = Settings.ClearSelectionAsync();
        // Keep a name the user is typing when the tree refreshes after a device change.
        if (reselected?.DockKey is null || !string.Equals(reselected.DockKey, selectedDockKey, StringComparison.OrdinalIgnoreCase))
            DockNameText = reselected?.DockKey is not null ? reselected.Name : string.Empty;
        OnPropertyChanged(nameof(HasDevices));
        RaiseSelectionProperties();
    }

    public async Task SelectAsync(DeviceNodeViewModel? node)
    {
        if (ReferenceEquals(node, SelectedNode)) return;
        SelectedNode = node;
        DockNameText = node?.DockKey is not null ? node.Name : string.Empty;
        Message = null;
        IsError = false;
        if (SelectedTopologyEndpoint is { } endpoint)
            await Settings.SelectEndpointAsync(endpoint).ConfigureAwait(true);
        else
            await Settings.ClearSelectionAsync().ConfigureAwait(true);
        RaiseSelectionProperties();
    }

    public DeviceNodeViewModel? FindEndpoint(string endpointId) =>
        Flatten(Roots).FirstOrDefault(node => string.Equals(node.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Makes the selected endpoint the default for all three roles, like choosing it in the quick-switch panel.</summary>
    public void SetAsDefault()
    {
        if (!CanSetDefault || SelectedNode?.EndpointId is not { } endpointId) return;
        var flow = SelectedNode.Kind == DeviceNodeKind.Recording ? AudioEndpointFlow.Capture : AudioEndpointFlow.Render;
        try
        {
            var result = _endpointWriter.SetDefaultEndpoint(endpointId, flow);
            var readback = _endpointWriter.GetDefaultEndpointId(flow, AudioEndpointRole.Multimedia);
            Refresh();
            if (result.Success && string.Equals(readback, endpointId, StringComparison.OrdinalIgnoreCase))
            {
                ShowMessage(Loc.T(flow == AudioEndpointFlow.Render ? "已設為預設播放裝置。" : "已設為預設錄音裝置。"), isError: false);
                return;
            }
            var error = string.Join(" ", result.Roles.Where(role => !role.Success).Select(role => role.Error).Where(message => !string.IsNullOrWhiteSpace(message)));
            ShowMessage(string.IsNullOrWhiteSpace(error) ? Loc.T("Windows 未確認切換結果，請重試。") : error, isError: true);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
    }

    public async Task PlayTestToneAsync()
    {
        if (!CanTestTone || SelectedNode?.EndpointId is not { } endpointId) return;
        IsPlayingTone = true;
        try
        {
            await _probe.PlayTestToneAsync(endpointId, TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            ShowMessage(Loc.T("試音完成。"), isError: false);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
        finally
        {
            IsPlayingTone = false;
        }
    }

    public async Task ToggleListeningAsync()
    {
        if (!CanListen || SelectedNode?.EndpointId is not { } endpointId) return;
        await Settings.ToggleListeningAsync(endpointId).ConfigureAwait(true);
        OnPropertyChanged(nameof(IsListening));
    }

    public void ApplyName()
    {
        if (_tree is null) return;
        if (!Settings.ApplyAlias(_tree.Endpoints)) return;
        ShowMessage(Loc.T("名稱已套用；Windows 設定與其他程式會顯示新名稱。"), isError: false);
        Refresh();
    }

    public void RestoreName()
    {
        if (_tree is null) return;
        if (!Settings.ClearAlias(_tree.Endpoints)) return;
        ShowMessage(Loc.T("已還原原名。"), isError: false);
        Refresh();
    }

    public void ApplyDockName()
    {
        if (SelectedNode?.DockKey is not { } key) return;
        var name = DockNameText.Trim();
        var hadName = _dockNames.TryGetValue(key, out var previous);
        if (name.Length == 0 || name == Loc.T(TopologyBuilder.DefaultDockName)) _dockNames.Remove(key);
        else _dockNames[key] = name;
        if (!TrySaveDockNames())
        {
            RestoreDockNameEntry(key, hadName ? previous : null);
            return;
        }
        Refresh();
        ShowMessage(Loc.T("名稱已套用；同型號的 Dock／Hub 都會顯示這個名稱。"), isError: false);
    }

    public void RestoreDockName()
    {
        if (SelectedNode?.DockKey is not { } key || !_dockNames.Remove(key, out var previous)) return;
        if (!TrySaveDockNames())
        {
            RestoreDockNameEntry(key, previous);
            return;
        }
        DockNameText = Loc.T(TopologyBuilder.DefaultDockName);
        Refresh();
        ShowMessage(Loc.F("已改回「{0}」。", Loc.T(TopologyBuilder.DefaultDockName)), isError: false);
    }

    // Puts back the name that was there before an edit that could not be saved, so memory matches the file.
    private void RestoreDockNameEntry(string key, string? previous)
    {
        if (previous is null) _dockNames.Remove(key);
        else _dockNames[key] = previous;
    }

    private bool TrySaveDockNames()
    {
        try
        {
            _saveDockNames();
            return true;
        }
        catch (Exception exception)
        {
            ShowMessage(Loc.F("名稱儲存失敗：{0}", exception.Message), isError: true);
            return false;
        }
    }

    public void AssignToPort(string interfaceId)
    {
        var result = Settings.CalibrateSelectedDevice(interfaceId);
        if (result?.Status != "calibrated") return;
        var name = _portTable.FindInterface(interfaceId)?.Name ?? interfaceId;
        Refresh();
        ShowMessage(Loc.F("已記錄：這個裝置插在「{0}」。", name), isError: false);
    }

    private DeviceNodeViewModel CreateNode(TopologyNode node, string groupName, string? interfaceId, string? renderDefault, string? captureDefault)
    {
        var endpoint = node.EndpointId is { } id
            ? _snapshot?.Endpoints.FirstOrDefault(item => item.EndpointId.Equals(id, StringComparison.OrdinalIgnoreCase))
            : null;
        var isCapture = endpoint?.Flow.Equals("capture", StringComparison.OrdinalIgnoreCase) == true;
        var kind = node.Kind switch
        {
            "dock-hub" => DeviceNodeKind.DockHub,
            "display" => DeviceNodeKind.Display,
            "endpoint" => isCapture ? DeviceNodeKind.Recording : DeviceNodeKind.Playback,
            _ => DeviceNodeKind.Device
        };
        var defaultId = isCapture ? captureDefault : renderDefault;
        var result = new DeviceNodeViewModel
        {
            Name = node.Name,
            Kind = kind,
            EndpointId = node.EndpointId,
            InterfaceId = interfaceId,
            DockKey = node.DockKey,
            GroupName = groupName,
            IsDefault = node.EndpointId is not null && string.Equals(node.EndpointId, defaultId, StringComparison.OrdinalIgnoreCase),
            PhysicalDeviceLocationRaw = node.PhysicalDeviceLocationRaw
        };
        foreach (var child in node.Children)
            result.Children.Add(CreateNode(child, groupName, interfaceId, renderDefault, captureDefault));
        return result;
    }

    private string BuildTechnicalDetails()
    {
        if (SelectedEndpoint is not { } endpoint) return string.Empty;
        var lines = new List<string>
        {
            Loc.F("端點 ID：{0}", endpoint.EndpointId),
            Loc.F("裝置節點：{0}", endpoint.DevnodeId ?? Loc.T("未解析")),
            Loc.F("分類：{0}", SelectedTopologyEndpoint?.Category ?? Loc.T("未知"))
        };
        var pld = SelectedNode?.PhysicalDeviceLocationRaw ?? [];
        lines.Add(pld.Count > 0 ? Loc.F("_PLD：{0}", string.Join(Environment.NewLine, pld)) : Loc.T("_PLD：不可用"));
        return string.Join(Environment.NewLine, lines);
    }

    private void RaiseSelectionProperties()
    {
        foreach (var name in new[]
        {
            nameof(HasSelection), nameof(IsEndpointSelected), nameof(IsDockSelected), nameof(CanRestoreDockName), nameof(CanSetDefault), nameof(IsSelectedDefault), nameof(CanTestTone), nameof(CanListen), nameof(CanIdentifyScreen),
            nameof(CanRestoreName), nameof(NeedsPortAssignment), nameof(UsbPorts), nameof(Title), nameof(KindText),
            nameof(LocationText), nameof(SelectedInterfaceId), nameof(StateText), nameof(TechnicalDetails), nameof(IsListening)
        })
            OnPropertyChanged(name);
    }

    private void ShowMessage(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }

    private static IEnumerable<DeviceNodeViewModel> Flatten(IEnumerable<DeviceNodeViewModel> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));

    private static bool IsActive(AudioEndpoint? endpoint) => endpoint is not null &&
        (endpoint.State.Equals("active", StringComparison.OrdinalIgnoreCase) || endpoint.State.EndsWith(".active", StringComparison.OrdinalIgnoreCase));

    internal static string StateLabel(string state) => Loc.T(state.ToLowerInvariant() switch
    {
        "active" or "audiodevicestate.active" => "使用中",
        "disabled" or "audiodevicestate.disabled" => "已停用",
        "notpresent" or "audiodevicestate.notpresent" => "未連接",
        "unplugged" or "audiodevicestate.unplugged" => "已拔除",
        _ => "狀態未知"
    });

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
