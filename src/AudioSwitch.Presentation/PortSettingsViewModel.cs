using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

public enum PortCalibrationPhase
{
    Idle,
    WaitingForDeviceAtPort,
    WaitingForNewDevice,
    WaitingForPlacement
}

public sealed class PortSettingsViewModel : INotifyPropertyChanged
{
    private readonly IAudioTopologySnapshotSource _snapshotSource;
    private readonly PortTable _portTable;
    private readonly DeviceSettingsViewModel _devices;
    private readonly Action _save;
    private readonly IDefaultEndpointWriter? _defaults;
    private ActiveDevices _active = ActiveDevices.None;
    private string? _selectedId;
    private string _nameText = string.Empty;
    private string? _message;
    private bool _isError;
    private PortCalibrationPhase _phase;
    private IReadOnlyList<AudioEndpoint> _detectedEndpoints = [];
    private string _aliasText = string.Empty;

    public PortSettingsViewModel(IAudioTopologySnapshotSource snapshotSource, PortTable portTable, MachineIdentity? machine,
        DeviceSettingsViewModel devices, Action save, IDefaultEndpointWriter? defaults = null)
    {
        _snapshotSource = snapshotSource;
        _defaults = defaults;
        _portTable = portTable;
        _devices = devices;
        _save = save;
        Diagram = new PortDiagramViewModel(portTable, machine);
        Calibration = new CalibrationWorkflowViewModel(snapshotSource, portTable);
    }

    public PortDiagramViewModel Diagram { get; }
    public CalibrationWorkflowViewModel Calibration { get; }

    /// <summary>Where the current default playback and recording devices are, shown on the laptop drawing.</summary>
    public ActiveDevices Active { get => _active; private set { if (_active == value) return; _active = value; OnPropertyChanged(); } }

    /// <summary>Reads the current defaults again, after a switch or a device change.</summary>
    public void RefreshActiveDevices()
    {
        if (_defaults is null) return;
        try
        {
            var snapshot = _snapshotSource.Capture();
            Active = ActiveDevices.Find(snapshot, TopologyBuilder.Build(snapshot, _portTable),
                _defaults.GetDefaultEndpointId(AudioEndpointFlow.Render, AudioEndpointRole.Multimedia),
                _defaults.GetDefaultEndpointId(AudioEndpointFlow.Capture, AudioEndpointRole.Multimedia));
        }
        catch (Exception exception)
        {
            Active = ActiveDevices.None;
            ShowMessage(exception.Message, isError: true);
        }
    }
    public bool IsEnabled => Diagram.IsEnabled;
    public string? DisabledReason => Diagram.IsEnabled ? null : Loc.F("接口表屬於另一台電腦，無法在這台電腦上編輯。{0}", Diagram.Warning);

    public PortDefinition? SelectedInterface => _selectedId is null ? null : _portTable.FindInterface(_selectedId);
    public bool HasSelection => SelectedInterface is not null;
    public string NameText { get => _nameText; set => Set(ref _nameText, value); }
    public string SelectedTypeText => SelectedInterface is { } port ? PortDiagramViewModel.ConnectorLabel(port.Type) : string.Empty;
    public string SelectedStatusText => SelectedInterface is { } port ? Diagram.GetCalibrationStatus(port) : string.Empty;
    public PortCalibrationState SelectedState => SelectedInterface is { } port ? Diagram.GetCalibrationState(port) : PortCalibrationState.Uncalibrated;
    public bool CanCalibrateSelected => Phase == PortCalibrationPhase.Idle && SelectedInterface is { Type: not "audio-jack" };
    public bool CanMoveSelected => Phase == PortCalibrationPhase.Idle && SelectedInterface is not null;

    public PortCalibrationPhase Phase { get => _phase; private set => Set(ref _phase, value); }
    public bool IsCalibrating => Phase != PortCalibrationPhase.Idle;
    public bool CanStartDetection => IsEnabled && !IsCalibrating;
    public bool ShowIdleHint => IsEnabled && !IsCalibrating && SelectedInterface is null;
    public bool IsWaitingForDevice => Phase is PortCalibrationPhase.WaitingForDeviceAtPort or PortCalibrationPhase.WaitingForNewDevice;
    public bool IsWaitingForUnplug => IsWaitingForDevice && Calibration.State == CalibrationWorkflowState.AwaitingUnplug;
    public bool CanCompleteDetection => IsWaitingForDevice && Calibration.State == CalibrationWorkflowState.AwaitingDevice;
    public bool IsWaitingForPlacement => Phase == PortCalibrationPhase.WaitingForPlacement;
    public bool CanApplyDetectedToSelected => IsWaitingForPlacement && SelectedInterface is { Type: not "audio-jack" };
    public IReadOnlyList<string> CalibrationSteps => BuildSteps();

    public IReadOnlyList<AudioEndpoint> DetectedEndpoints { get => _detectedEndpoints; private set => Set(ref _detectedEndpoints, value); }
    public bool CanEnterAlias => Phase == PortCalibrationPhase.Idle && DetectedEndpoints.Count > 0;
    public string DetectedEndpointNames => Loc.Join(DetectedEndpoints.Select(endpoint => endpoint.FriendlyName ?? Loc.T("音訊裝置")));
    public string AliasText { get => _aliasText; set => Set(ref _aliasText, value); }

    public string? Message { get => _message; private set => Set(ref _message, value); }
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }

    /// <summary>Raised after a new port is placed so the view can focus the name box with the suggested name selected.</summary>
    public event EventHandler? NameEditRequested;
    public event EventHandler? DiagramChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Select(string? interfaceId)
    {
        _selectedId = interfaceId is not null && _portTable.FindInterface(interfaceId) is not null ? interfaceId : null;
        NameText = SelectedInterface?.Name ?? string.Empty;
        RaiseState();
    }

    public PortDefinition? Add(PortConnectorType type, PortEdge edge, int order)
    {
        if (!Guard()) return null;
        try
        {
            var created = Diagram.AddInterface(type, edge, order);
            Select(created.Id);
            if (Phase == PortCalibrationPhase.WaitingForPlacement && type != PortConnectorType.AudioJack)
            {
                ApplyDetectedToSelected();
            }
            else
            {
                Persist();
                ShowMessage(null);
            }
            DiagramChanged?.Invoke(this, EventArgs.Empty);
            NameEditRequested?.Invoke(this, EventArgs.Empty);
            return created;
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
            return null;
        }
    }

    public void Move(string interfaceId, PortEdge edge, int order)
    {
        if (!Guard()) return;
        try
        {
            Diagram.MoveInterface(interfaceId, edge, order);
            Persist();
            Select(interfaceId);
            DiagramChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
    }

    public void MoveSelectedBy(int delta)
    {
        if (SelectedInterface is not { } port) return;
        Move(port.Id, PortDiagramViewModel.ParseEdge(port.Edge), Math.Max(0, (port.Order ?? 0) + delta));
    }

    public void MoveSelectedToEdge(PortEdge edge)
    {
        if (SelectedInterface is not { } port || PortDiagramViewModel.ParseEdge(port.Edge) == edge) return;
        Move(port.Id, edge, int.MaxValue);
    }

    public void SaveName()
    {
        if (!Guard() || SelectedInterface is not { } port) return;
        if (string.IsNullOrWhiteSpace(NameText))
        {
            ShowMessage(Loc.T("接口名稱不可空白。"), isError: true);
            return;
        }
        try
        {
            Diagram.RenameInterface(port.Id, NameText.Trim());
            Persist();
            ShowMessage(Loc.T("名稱已儲存。"));
            RaiseState();
            DiagramChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
    }

    public void DeleteSelected()
    {
        if (!Guard() || SelectedInterface is not { } port) return;
        try
        {
            Diagram.DeleteInterface(port.Id);
            Persist();
            Select(null);
            ShowMessage(Loc.F("已刪除「{0}」。", port.Name));
            DiagramChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
    }

    public void BeginCalibration()
    {
        if (!Guard() || !CanCalibrateSelected || SelectedInterface is not { } port) return;
        try
        {
            Calibration.BeginAtPort(port.Id);
            DetectedEndpoints = [];
            Phase = PortCalibrationPhase.WaitingForDeviceAtPort;
            ShowMessage(null);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
        RaiseState();
    }

    public void BeginDetectNewDevice()
    {
        if (!Guard() || IsCalibrating) return;
        try
        {
            Calibration.BeginBeforePortPlacement();
            DetectedEndpoints = [];
            Phase = PortCalibrationPhase.WaitingForNewDevice;
            ShowMessage(null);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
        RaiseState();
    }

    public void ConfirmUnplugged()
    {
        if (!IsWaitingForUnplug) return;
        try
        {
            Calibration.ConfirmUnplugged();
            ShowMessage(null);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
        RaiseState();
    }

    public void CompleteDetection()
    {
        if (!CanCompleteDetection) return;
        try
        {
            var result = Calibration.CompleteDeviceStep();
            if (Calibration.State == CalibrationWorkflowState.AwaitingPortPlacement)
            {
                Phase = PortCalibrationPhase.WaitingForPlacement;
                ShowMessage(null);
            }
            else
            {
                HandleResult(result);
            }
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
        RaiseState();
    }

    public void ApplyDetectedToSelected()
    {
        if (!IsWaitingForPlacement || SelectedInterface is not { } port) return;
        try
        {
            HandleResult(Calibration.SelectPort(port.Id));
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
        RaiseState();
    }

    public void CancelCalibration()
    {
        Calibration.Cancel();
        Phase = PortCalibrationPhase.Idle;
        ShowMessage(Loc.T("已取消校準。"));
        RaiseState();
    }

    public void ApplyDetectedAlias()
    {
        if (!CanEnterAlias || string.IsNullOrWhiteSpace(AliasText)) return;
        try
        {
            var tree = TopologyBuilder.Build(_snapshotSource.Capture(), _portTable);
            var target = DetectedEndpoints.Select(endpoint => tree.Endpoints.FirstOrDefault(item =>
                    item.EndpointId.Equals(endpoint.EndpointId, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(endpoint => endpoint is not null);
            if (target is null)
            {
                ShowMessage(Loc.T("找不到剛才偵測到的裝置，請確認仍插著。"), isError: true);
                return;
            }
            if (!_devices.ApplyAlias(target, tree.Endpoints, AliasText.Trim()))
            {
                ShowMessage(_devices.ErrorMessage ?? Loc.T("名稱套用失敗。"), isError: true);
                return;
            }
            DetectedEndpoints = [];
            AliasText = string.Empty;
            ShowMessage(Loc.T("裝置名稱已套用。"));
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, isError: true);
        }
        RaiseState();
    }

    public void SkipAlias()
    {
        DetectedEndpoints = [];
        AliasText = string.Empty;
        RaiseState();
    }

    private void HandleResult(CalibrationResult? result)
    {
        if (result?.Status == "calibrated")
        {
            Phase = PortCalibrationPhase.Idle;
            DetectedEndpoints = Calibration.NewAudioEndpoints;
            var name = result.InterfaceId is { } id ? _portTable.FindInterface(id)?.Name ?? id : Loc.T("接口");
            Persist();
            if (result.InterfaceId is not null) Select(result.InterfaceId);
            var displays = result.DisplayConnectors?.Count ?? 0;
            var details = result.Transports.Select(Loc.T).Concat(displays > 0 ? [Loc.F("{0} 個螢幕輸出", displays)] : Array.Empty<string>()).ToArray();
            ShowMessage(details.Length > 0 ? Loc.F("已校準「{0}」：{1}。", name, Loc.Join(details)) : Loc.F("已校準「{0}」。", name));
            DiagramChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        // The workflow stays active so the user can unplug extra devices and retry, or cancel.
        if (result?.Status == "ambiguous")
            ShowMessage(Loc.T("偵測到不只一個裝置。請只插入一個裝置，再按「完成偵測」。"), isError: true);
        else
            ShowMessage(Calibration.ErrorMessage ?? Loc.T("沒有偵測到新插入的裝置。"), isError: true);
        if (Calibration.State == CalibrationWorkflowState.Idle) Phase = PortCalibrationPhase.Idle;
        else if (Calibration.State == CalibrationWorkflowState.AwaitingUnplug && Phase == PortCalibrationPhase.WaitingForPlacement)
            Phase = PortCalibrationPhase.WaitingForNewDevice;
    }

    private IReadOnlyList<string> BuildSteps()
    {
        var target = Phase == PortCalibrationPhase.WaitingForDeviceAtPort ? SelectedInterface : null;
        var isDisplay = target?.Type is "hdmi" or "displayport";
        var isUsbC = target?.Type == "usb-c";
        if (IsWaitingForUnplug)
        {
            return
            [
                Loc.T(isDisplay ? "1. 拔掉要校準的螢幕線。" : isUsbC ? "1. 拔掉要校準的 USB 裝置或 Dock。" : target is not null ? "1. 拔掉要校準的 USB 裝置。" : "1. 拔掉要校準的裝置。"),
                Loc.T("2. 拔掉後（或本來就沒接）按「下一步」。")
            ];
        }
        if (CanCompleteDetection)
        {
            return
            [
                isDisplay ? Loc.F("1. 把螢幕接上「{0}」。", target!.Name) : target is not null ? Loc.F("1. 把它插入「{0}」。", target.Name) : Loc.T("1. 把它插入要定義的接口。"),
                Loc.T(isDisplay ? "2. 畫面出現後按「完成偵測」。" : isUsbC ? "2. 接著螢幕的話，等畫面出現後再按「完成偵測」。" : "2. 插好後按「完成偵測」。")
            ];
        }
        return IsWaitingForPlacement
            ?
            [
                Loc.T("已偵測到裝置。"),
                Loc.T("在示意圖上點選它插著的接口，再按「套用到這個接口」；"),
                Loc.T("或從上方把接口類型拖到示意圖上的對應位置。")
            ]
            : [];
    }

    private bool Guard()
    {
        if (Diagram.IsEnabled) return true;
        ShowMessage(DisabledReason, isError: true);
        return false;
    }

    private void Persist()
    {
        try
        {
            _save();
        }
        catch (Exception exception)
        {
            ShowMessage(Loc.F("設定檔儲存失敗：{0}", exception.Message), isError: true);
        }
    }

    private void ShowMessage(string? message, bool isError = false)
    {
        Message = message;
        IsError = isError && message is not null;
    }

    private void RaiseState()
    {
        foreach (var name in new[]
        {
            nameof(SelectedInterface), nameof(HasSelection), nameof(SelectedTypeText), nameof(SelectedStatusText), nameof(SelectedState),
            nameof(CanCalibrateSelected), nameof(CanMoveSelected), nameof(IsCalibrating), nameof(IsWaitingForDevice),
            nameof(IsWaitingForUnplug), nameof(CanCompleteDetection),
            nameof(IsWaitingForPlacement), nameof(CanApplyDetectedToSelected), nameof(CalibrationSteps), nameof(CanEnterAlias),
            nameof(DetectedEndpointNames), nameof(ShowIdleHint), nameof(CanStartDetection)
        })
            OnPropertyChanged(name);
    }

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
