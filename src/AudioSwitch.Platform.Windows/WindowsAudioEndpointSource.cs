using System.Text.Json;
using AudioSwitch.Core;
using NAudio.CoreAudioApi;

namespace AudioSwitch.Platform.Windows;

public sealed class WindowsAudioEndpointSource : IAudioEndpointSource
{
    private static readonly PropertyKey ContainerIdKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    private static readonly PropertyKey StableIdKey = new(new Guid("DFF21CE1-F70F-11D0-B917-00A0C9223196"), 100);

    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDeviceNotificationClient _notificationClient;
    private readonly List<PlatformError> _lastErrors = [];
    private bool _disposed;

    public IReadOnlyList<PlatformError> LastErrors => _lastErrors;
    public event EventHandler<EndpointNotificationEventArgs>? EndpointChanged;
    public event EventHandler<EndpointNotificationEventArgs>? DefaultEndpointChanged;

    public WindowsAudioEndpointSource()
    {
        _enumerator = new MMDeviceEnumerator();
        _notificationClient = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notificationClient.DeviceAdded += OnDeviceAdded;
        _notificationClient.DeviceRemoved += OnDeviceRemoved;
        _notificationClient.DeviceStateChanged += OnDeviceStateChanged;
        _notificationClient.PropertyValueChanged += OnPropertyValueChanged;
        _notificationClient.DefaultDeviceChanged += OnDefaultDeviceChanged;
    }

    public IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lastErrors.Clear();
        using var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
        var endpoints = new List<AudioEndpoint>(collection.Count);
        foreach (var device in collection)
        {
            try
            {
                using (device)
                {
                    if (device.State != DeviceState.Active)
                    {
                        continue;
                    }

                    endpoints.Add(ReadEndpoint(device));
                }
            }
            catch (Exception exception)
            {
                _lastErrors.Add(new PlatformError("audio_endpoint", "read", $"{exception.GetType().Name}: {exception.Message}"));
            }
        }
        return endpoints;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _notificationClient.DeviceAdded -= OnDeviceAdded;
        _notificationClient.DeviceRemoved -= OnDeviceRemoved;
        _notificationClient.DeviceStateChanged -= OnDeviceStateChanged;
        _notificationClient.PropertyValueChanged -= OnPropertyValueChanged;
        _notificationClient.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        _notificationClient.Dispose();
        _enumerator.Dispose();
    }

    private AudioEndpoint ReadEndpoint(MMDevice device)
    {
        var endpoint = new AudioEndpoint
        {
            EndpointId = device.ID,
            Flow = device.DataFlow switch
            {
                DataFlow.Render => "render",
                DataFlow.Capture => "capture",
                _ => "unknown"
            },
            State = $"AudioDeviceState.{device.State}",
            DevnodeId = ResolveMmDeviceDevnodeId(device.ID),
            ResolutionMethod = "unresolved"
        };
        if (endpoint.DevnodeId is not null)
        {
            endpoint.ResolutionMethod = "mmdevapi_devnode";
        }

        TryRead(endpoint, "friendly_name", () => endpoint.FriendlyName = device.FriendlyName);
        TryRead(endpoint, "form_factor", () =>
        {
            if (device.Properties.TryGetValue<int>(PropertyKeys.PKEY_AudioEndpoint_FormFactor, out var formFactor))
            {
                endpoint.FormFactor = formFactor;
                endpoint.Properties["form_factor"] = JsonSerializer.SerializeToElement(formFactor);
            }
        });
        TryRead(endpoint, "container_id", () =>
        {
            if (device.Properties.TryGetValue<Guid>(ContainerIdKey, out var containerId))
            {
                endpoint.ContainerId = containerId.ToString("D");
                endpoint.Properties["container_id"] = JsonSerializer.SerializeToElement(endpoint.ContainerId);
            }
        });
        TryRead(endpoint, "stable_id", () =>
        {
            if (device.Properties.TryGetValue<string>(StableIdKey, out var stableId))
            {
                endpoint.StableId = stableId;
                endpoint.Properties["stable_id"] = JsonSerializer.SerializeToElement(stableId);
            }
        });
        if (endpoint.FriendlyName is not null)
        {
            endpoint.Properties["friendly_name"] = JsonSerializer.SerializeToElement(endpoint.FriendlyName);
        }

        foreach (var (roleName, role) in Roles)
        {
            try
            {
                var flow = endpoint.Flow == "render" ? DataFlow.Render : DataFlow.Capture;
                var isDefault = _enumerator.TryGetDefaultAudioEndpoint(flow, role, out var defaultDevice) &&
                    defaultDevice is not null && defaultDevice.ID.Equals(device.ID, StringComparison.OrdinalIgnoreCase);
                endpoint.Defaults[roleName] = isDefault;
                defaultDevice?.Dispose();
            }
            catch (Exception exception)
            {
                endpoint.Errors.Add(Error($"default_{roleName}", exception));
            }
        }

        return endpoint;
    }

    private static void TryRead(AudioEndpoint endpoint, string property, Action read)
    {
        try
        {
            read();
        }
        catch (Exception exception)
        {
            endpoint.Errors.Add(Error(property, exception));
        }
    }

    private static Dictionary<string, string> Error(string property, Exception exception) =>
        new() { ["property"] = property, ["error"] = $"{exception.GetType().Name}: {exception.Message}" };

    private static string? ResolveMmDeviceDevnodeId(string endpointId)
    {
        if (endpointId.StartsWith("{0.0.", StringComparison.OrdinalIgnoreCase) || endpointId.StartsWith("{0.1.", StringComparison.OrdinalIgnoreCase))
        {
            return "SWD\\MMDEVAPI\\" + endpointId;
        }

        var marker = "SWD#MMDEVAPI#";
        var markerIndex = endpointId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var tail = endpointId[(markerIndex + marker.Length)..].Split('#')[0];
            return "SWD\\MMDEVAPI\\" + tail.Replace('#', '\\');
        }

        return null;
    }

    private void OnDeviceAdded(object? sender, DeviceNotificationEventArgs args) =>
        EndpointChanged?.Invoke(this, new EndpointNotificationEventArgs(args.DeviceId, "added"));

    private void OnDeviceRemoved(object? sender, DeviceNotificationEventArgs args) =>
        EndpointChanged?.Invoke(this, new EndpointNotificationEventArgs(args.DeviceId, "removed"));

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs args) =>
        EndpointChanged?.Invoke(this, new EndpointNotificationEventArgs(args.DeviceId, $"state:{args.NewState}"));

    private void OnPropertyValueChanged(object? sender, DevicePropertyChangedEventArgs args) =>
        EndpointChanged?.Invoke(this, new EndpointNotificationEventArgs(args.DeviceId, "property_changed"));

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs args) =>
        DefaultEndpointChanged?.Invoke(this, new EndpointNotificationEventArgs(args.DeviceId, $"{args.Flow}:{args.Role}"));

    private static readonly (string Name, Role Value)[] Roles =
    [
        ("console", Role.Console),
        ("multimedia", Role.Multimedia),
        ("communications", Role.Communications)
    ];
}
