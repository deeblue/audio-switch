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
