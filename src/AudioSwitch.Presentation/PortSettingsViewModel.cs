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
    private string? _selectedId;
    private string _nameText = string.Empty;
    private string? _message;
    private bool _isError;
    private PortCalibrationPhase _phase;
    private IReadOnlyList<AudioEndpoint> _detectedEndpoints = [];
    private string _aliasText = string.Empty;

    public PortSettingsViewModel(IAudioTopologySnapshotSource snapshotSource, PortTable portTable, MachineIdentity? machine,
        DeviceSettingsViewModel devices, Action save)
    {
        _snapshotSource = snapshotSource;
        _portTable = portTable;
        _devices = devices;
        _save = save;
        Diagram = new PortDiagramViewModel(portTable, machine);
        Calibration = new CalibrationWorkflowViewModel(snapshotSource, portTable);
    }

    public PortDiagramViewModel Diagram { get; }
    public CalibrationWorkflowViewModel Calibration { get; }
    public bool IsEnabled => Diagram.IsEnabled;
    public string? DisabledReason => Diagram.IsEnabled ? null : $"接口表屬於另一台電腦，無法在這台電腦上編輯。{Diagram.Warning}";

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
    public bool IsWaitingForPlacement => Phase == PortCalibrationPhase.WaitingForPlacement;
    public bool CanApplyDetectedToSelected => IsWaitingForPlacement && SelectedInterface is { Type: not "audio-jack" };
    public IReadOnlyList<string> CalibrationSteps => BuildSteps();

    public IReadOnlyList<AudioEndpoint> DetectedEndpoints { get => _detectedEndpoints; private set => Set(ref _detectedEndpoints, value); }
    public bool CanEnterAlias => Phase == PortCalibrationPhase.Idle && DetectedEndpoints.Count > 0;
    public string DetectedEndpointNames => string.Join("、", DetectedEndpoints.Select(endpoint => endpoint.FriendlyName ?? "音訊裝置"));
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
            ShowMessage("接口名稱不可空白。", isError: true);
            return;
        }
        try
        {
            Diagram.RenameInterface(port.Id, NameText.Trim());
            Persist();
            ShowMessage("名稱已儲存。");
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
            ShowMessage($"已刪除「{port.Name}」。");
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

    public void CompleteDetection()
    {
        if (!IsWaitingForDevice) return;
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
        ShowMessage("已取消校準。");
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
                ShowMessage("找不到剛才偵測到的裝置，請確認仍插著。", isError: true);
                return;
            }
            if (!_devices.ApplyAlias(target, tree.Endpoints, AliasText.Trim()))
            {
                ShowMessage(_devices.ErrorMessage ?? "名稱套用失敗。", isError: true);
                return;
            }
            DetectedEndpoints = [];
            AliasText = string.Empty;
            ShowMessage("裝置名稱已套用。");
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
            var name = result.InterfaceId is { } id ? _portTable.FindInterface(id)?.Name ?? id : "接口";
            Persist();
            if (result.InterfaceId is not null) Select(result.InterfaceId);
            ShowMessage(result.Transports.Count > 0 ? $"已校準「{name}」：{string.Join("、", result.Transports)}。" : $"已校準「{name}」。");
            DiagramChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        // The workflow stays active so the user can unplug extra devices and retry, or cancel.
        if (result?.Status == "ambiguous")
            ShowMessage("偵測到不只一個裝置。請只插入一個裝置，再按「完成偵測」。", isError: true);
        else
            ShowMessage(Calibration.ErrorMessage ?? "沒有偵測到新插入的裝置。請確認是在開始校準之後才插入，再按「完成偵測」。", isError: true);
        if (Calibration.State == CalibrationWorkflowState.Idle) Phase = PortCalibrationPhase.Idle;
    }

    private IReadOnlyList<string> BuildSteps()
    {
        var target = SelectedInterface;
        var isDisplay = target?.Type is "hdmi" or "displayport";
        return Phase switch
        {
            PortCalibrationPhase.WaitingForDeviceAtPort => isDisplay
                ?
                [
                    "1. 先拔掉要用來校準的螢幕線。",
                    $"2. 把螢幕接上「{target?.Name}」。",
                    "3. 畫面出現後按「完成偵測」。"
                ]
                :
                [
                    "1. 先拔掉要用來校準的 USB 裝置。",
                    $"2. 把它插入「{target?.Name}」。",
                    "3. 插好後按「完成偵測」。"
                ],
            PortCalibrationPhase.WaitingForNewDevice =>
            [
                "1. 先拔掉要用來校準的裝置。",
                "2. 把它插入要定義的接口。",
                "3. 插好後按「完成偵測」。"
            ],
            PortCalibrationPhase.WaitingForPlacement =>
            [
                "已偵測到裝置。",
                "在示意圖上點選它插著的接口，再按「套用到這個接口」；",
                "或從上方把接口類型拖到示意圖上的對應位置。"
            ],
            _ => []
        };
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
            ShowMessage($"設定檔儲存失敗：{exception.Message}", isError: true);
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
