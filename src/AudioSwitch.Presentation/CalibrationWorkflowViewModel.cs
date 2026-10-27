using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

public enum CalibrationWorkflowState
{
    Idle,
    AwaitingUnplug,
    AwaitingDevice,
    AwaitingPortPlacement,
    Completed
}

public sealed class CalibrationWorkflowViewModel
{
    private readonly IAudioTopologySnapshotSource _snapshotSource;
    private readonly PortTable _portTable;
    private Snapshot? _before;
    private Snapshot? _after;
    private string? _interfaceId;

    public CalibrationWorkflowState State { get; private set; }
    public CalibrationResult? Result { get; private set; }
    public string? ErrorMessage { get; private set; }
    public IReadOnlyList<AudioEndpoint> NewAudioEndpoints { get; private set; } = [];

    public CalibrationWorkflowViewModel(IAudioTopologySnapshotSource snapshotSource, PortTable portTable)
    {
        _snapshotSource = snapshotSource;
        _portTable = portTable;
    }

    public void BeginAtPort(string interfaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceId);
        if (_portTable.FindInterface(interfaceId) is null) throw new ArgumentException($"找不到接口 ID：{interfaceId}", nameof(interfaceId));
        Reset();
        _interfaceId = interfaceId;
        State = CalibrationWorkflowState.AwaitingUnplug;
    }

    public void BeginBeforePortPlacement()
    {
        Reset();
        State = CalibrationWorkflowState.AwaitingUnplug;
    }

    /// <summary>Captures the baseline once the user has unplugged the device, so plugging it back in shows up as a change.</summary>
    public void ConfirmUnplugged()
    {
        if (State != CalibrationWorkflowState.AwaitingUnplug)
            throw new InvalidOperationException("目前沒有等待拔除裝置的校準流程。");
        _before = _snapshotSource.Capture();
        ErrorMessage = null;
        State = CalibrationWorkflowState.AwaitingDevice;
    }

    public CalibrationResult? CompleteDeviceStep()
    {
        if (State != CalibrationWorkflowState.AwaitingDevice || _before is null)
            throw new InvalidOperationException("目前沒有等待裝置接入的校準流程。");
        try
        {
            _after = _snapshotSource.Capture();
            var beforeIds = _before.Endpoints.Select(endpoint => endpoint.EndpointId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            NewAudioEndpoints = _after.Endpoints.Where(endpoint => IsActive(endpoint) && !beforeIds.Contains(endpoint.EndpointId)).ToArray();
            if (_interfaceId is null)
            {
                State = CalibrationWorkflowState.AwaitingPortPlacement;
                ErrorMessage = NewAudioEndpoints.Count > 0 ? "已偵測新音訊端點，請選擇或新增對應接口。" : "已偵測裝置，請選擇或新增對應接口。";
                return null;
            }
            return ApplyCalibration(_interfaceId, _before, _after);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return null;
        }
    }

    public CalibrationResult? SelectPort(string interfaceId)
    {
        if (State != CalibrationWorkflowState.AwaitingPortPlacement || _before is null || _after is null)
            throw new InvalidOperationException("請先完成裝置接入偵測，再選擇接口位置。");
        if (_portTable.FindInterface(interfaceId) is null) throw new ArgumentException($"找不到接口 ID：{interfaceId}", nameof(interfaceId));
        return ApplyCalibration(interfaceId, _before, _after);
    }

    public void Cancel() => Reset();

    private CalibrationResult? ApplyCalibration(string interfaceId, Snapshot before, Snapshot after)
    {
        try
        {
            Result = _portTable.Calibrate(interfaceId, before, after);
            if (Result.Status == "calibrated")
            {
                State = CalibrationWorkflowState.Completed;
                ErrorMessage = null;
            }
            else if (Result.Status == "ambiguous")
            {
                ErrorMessage = "請只插入一個裝置後重試。";
            }
            else
            {
                // Retrying against the same baseline cannot succeed once the device is plugged in; start over from unplugging.
                _before = null;
                _after = null;
                State = CalibrationWorkflowState.AwaitingUnplug;
                ErrorMessage = "沒有偵測到新插入的裝置。請再拔掉一次，按「下一步」後再插入。";
            }
            return Result;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return null;
        }
    }

    private void Reset()
    {
        _before = null;
        _after = null;
        _interfaceId = null;
        Result = null;
        ErrorMessage = null;
        NewAudioEndpoints = [];
        State = CalibrationWorkflowState.Idle;
    }

    private static bool IsActive(AudioEndpoint endpoint) => endpoint.State.Equals("active", StringComparison.OrdinalIgnoreCase) ||
        endpoint.State.EndsWith(".active", StringComparison.OrdinalIgnoreCase);
}
