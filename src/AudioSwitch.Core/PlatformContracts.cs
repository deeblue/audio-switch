namespace AudioSwitch.Core;

/// <summary>An error met while reading from the platform, kept with the data instead of thrown.</summary>
public sealed record PlatformError(string Scope, string Property, string Message);

/// <summary>An endpoint change reported by Windows: added, removed, a state or property change, or a new default.</summary>
public sealed class EndpointNotificationEventArgs(string? endpointId, string changeKind) : EventArgs
{
    public string? EndpointId { get; } = endpointId;
    public string ChangeKind { get; } = changeKind;
}

/// <summary>Lists the active audio endpoints and reports endpoint changes.</summary>
public interface IAudioEndpointSource : IDisposable
{
    IReadOnlyList<PlatformError> LastErrors { get; }
    IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints();
    event EventHandler<EndpointNotificationEventArgs>? EndpointChanged;
    event EventHandler<EndpointNotificationEventArgs>? DefaultEndpointChanged;
}

/// <summary>Reads the device tree: a device's ancestors and the present devices of a bus.</summary>
public interface IDeviceTreeSource
{
    /// <summary>The device and its ancestors up to the root, or up to (not including) the first one in <paramref name="known"/>.</summary>
    IReadOnlyList<DeviceNode> GetAncestors(string instanceId, IReadOnlySet<string>? known = null);

    /// <summary>Instance IDs of the present devices that the given bus enumerator (for example "USB") created.</summary>
    IReadOnlyList<string> GetPresentDeviceIds(string enumerator);
}

/// <summary>Reads the jack and sink information of an endpoint.</summary>
public interface IJackInfoSource
{
    IReadOnlyList<JackRecord> GetEndpointJackInfo(string endpointId);
}

/// <summary>Reads the active display paths.</summary>
public interface IDisplayPathSource
{
    IReadOnlyList<DisplayPath> GetActivePaths();
}

/// <summary>Reads the computer's manufacturer, model and machine type.</summary>
public interface IMachineIdentitySource
{
    IReadOnlyList<PlatformError> LastErrors { get; }
    MachineIdentity ReadIdentity();
}

/// <summary>Takes a snapshot of endpoints, device tree, jacks and display paths.</summary>
public interface IAudioTopologySnapshotSource
{
    Snapshot Capture();
}

/// <summary>The horizontal and vertical DPI of a monitor.</summary>
public sealed record MonitorDpi(int X, int Y);

/// <summary>Reads the DPI of the monitor at a point.</summary>
public interface IMonitorDpiSource
{
    MonitorDpi GetDpiForPoint(int x, int y);
}

/// <summary>Playback (render) or recording (capture).</summary>
public enum AudioEndpointFlow
{
    Render,
    Capture
}

/// <summary>The three default roles Windows keeps per flow; AudioSwitch switches all three together.</summary>
public enum AudioEndpointRole
{
    Console,
    Multimedia,
    Communications
}

/// <summary>The outcome of setting one role's default, with the default read back.</summary>
public sealed record EndpointRoleChangeResult(
    string EndpointId,
    AudioEndpointFlow Flow,
    AudioEndpointRole Role,
    bool Success,
    string? ActualDefaultEndpointId,
    string? Error);

/// <summary>The outcome of setting all three roles' default.</summary>
public sealed record EndpointSwitchResult(string EndpointId, AudioEndpointFlow Flow, IReadOnlyList<EndpointRoleChangeResult> Roles)
{
    public bool Success => Roles.Count == 3 && Roles.All(role => role.Success);
}

/// <summary>Reads and sets the default endpoint.</summary>
public interface IDefaultEndpointWriter : IDisposable
{
    string? GetDefaultEndpointId(AudioEndpointFlow flow, AudioEndpointRole role);
    EndpointSwitchResult SetDefaultEndpoint(string endpointId, AudioEndpointFlow flow);
    EndpointRoleChangeResult SetDefaultEndpointRole(string endpointId, AudioEndpointFlow flow, AudioEndpointRole role);
}

/// <summary>Reads and writes the endpoint name shown by Windows and other apps.</summary>
public interface IEndpointNameWriter : IDisposable
{
    string ReadDeviceDescription(string endpointId);
    string ReadFriendlyName(string endpointId);
    void WriteDeviceDescription(string endpointId, string value);
}

/// <summary>Plays a test tone on a playback endpoint and samples a recording endpoint's level.</summary>
public interface IAudioProbe : IDisposable
{
    Task PlayTestToneAsync(string endpointId, TimeSpan duration, CancellationToken cancellationToken = default);
    void StartPeakSampling(string endpointId);
    float ReadPeakLevel(string endpointId);
    Task StopPeakSamplingAsync(CancellationToken cancellationToken = default);
}

/// <summary>The outcome of registering the shortcuts.</summary>
public sealed record HotkeyRegistrationResult(bool Success, string? Error);

/// <summary>Registers and releases the global shortcuts.</summary>
public interface IHotkeyService : IDisposable
{
    HotkeyRegistrationResult Register(IntPtr targetWindow, string? playbackHotkey, string? recordingHotkey);
    void Unregister();
}

/// <summary>Turns starting with Windows on or off.</summary>
public interface IAutostartService
{
    void SetEnabled(bool enabled);
}
