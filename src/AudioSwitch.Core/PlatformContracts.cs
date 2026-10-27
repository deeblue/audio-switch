namespace AudioSwitch.Core;

public sealed record PlatformError(string Scope, string Property, string Message);

public sealed class EndpointNotificationEventArgs(string? endpointId, string changeKind) : EventArgs
{
    public string? EndpointId { get; } = endpointId;
    public string ChangeKind { get; } = changeKind;
}

public interface IAudioEndpointSource : IDisposable
{
    IReadOnlyList<PlatformError> LastErrors { get; }
    IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints();
    event EventHandler<EndpointNotificationEventArgs>? EndpointChanged;
    event EventHandler<EndpointNotificationEventArgs>? DefaultEndpointChanged;
}

public interface IDeviceTreeSource
{
    IReadOnlyList<DeviceNode> GetAncestors(string instanceId);

    /// <summary>Instance IDs of the present devices that the given bus enumerator (for example "USB") created.</summary>
    IReadOnlyList<string> GetPresentDeviceIds(string enumerator);
}

public interface IJackInfoSource
{
    IReadOnlyList<JackRecord> GetEndpointJackInfo(string endpointId);
}

public interface IDisplayPathSource
{
    IReadOnlyList<DisplayPath> GetActivePaths();
}

public interface IMachineIdentitySource
{
    IReadOnlyList<PlatformError> LastErrors { get; }
    MachineIdentity ReadIdentity();
}

public interface IAudioTopologySnapshotSource
{
    Snapshot Capture();
}

public sealed record MonitorDpi(int X, int Y);

public interface IMonitorDpiSource
{
    MonitorDpi GetDpiForPoint(int x, int y);
}

public enum AudioEndpointFlow
{
    Render,
    Capture
}

public enum AudioEndpointRole
{
    Console,
    Multimedia,
    Communications
}

public sealed record EndpointRoleChangeResult(
    string EndpointId,
    AudioEndpointFlow Flow,
    AudioEndpointRole Role,
    bool Success,
    string? ActualDefaultEndpointId,
    string? Error);

public sealed record EndpointSwitchResult(string EndpointId, AudioEndpointFlow Flow, IReadOnlyList<EndpointRoleChangeResult> Roles)
{
    public bool Success => Roles.Count == 3 && Roles.All(role => role.Success);
}

public interface IDefaultEndpointWriter : IDisposable
{
    string? GetDefaultEndpointId(AudioEndpointFlow flow, AudioEndpointRole role);
    EndpointSwitchResult SetDefaultEndpoint(string endpointId, AudioEndpointFlow flow);
    EndpointRoleChangeResult SetDefaultEndpointRole(string endpointId, AudioEndpointFlow flow, AudioEndpointRole role);
}

public interface IEndpointNameWriter : IDisposable
{
    string ReadDeviceDescription(string endpointId);
    string ReadFriendlyName(string endpointId);
    void WriteDeviceDescription(string endpointId, string value);
}

public interface IAudioProbe : IDisposable
{
    Task PlayTestToneAsync(string endpointId, TimeSpan duration, CancellationToken cancellationToken = default);
    void StartPeakSampling(string endpointId);
    float ReadPeakLevel(string endpointId);
    Task StopPeakSamplingAsync(CancellationToken cancellationToken = default);
}

public sealed record HotkeyRegistrationResult(bool Success, string? Error);

public interface IHotkeyService : IDisposable
{
    HotkeyRegistrationResult Register(IntPtr targetWindow, string? playbackHotkey, string? recordingHotkey);
    void Unregister();
}

public interface IAutostartService
{
    void SetEnabled(bool enabled);
}
