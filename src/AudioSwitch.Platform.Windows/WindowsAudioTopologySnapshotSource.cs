using AudioSwitch.Core;

namespace AudioSwitch.Platform.Windows;

public sealed class WindowsAudioTopologySnapshotSource : IAudioTopologySnapshotSource
{
    private readonly IAudioEndpointSource _endpoints;
    private readonly IDeviceTreeSource _deviceTree;
    private readonly IJackInfoSource _jacks;
    private readonly IDisplayPathSource _displays;
    private readonly IMachineIdentitySource _machine;

    public WindowsAudioTopologySnapshotSource(IAudioEndpointSource? endpoints = null, IDeviceTreeSource? deviceTree = null,
        IJackInfoSource? jacks = null, IDisplayPathSource? displays = null, IMachineIdentitySource? machine = null)
    {
        var tree = deviceTree as WindowsDeviceTreeSource ?? new WindowsDeviceTreeSource();
        _endpoints = endpoints ?? new WindowsAudioEndpointSource();
        _deviceTree = deviceTree ?? tree;
        _jacks = jacks ?? new WindowsJackInfoSource(tree);
        _displays = displays ?? new WindowsDisplayPathSource(tree);
        _machine = machine ?? new WindowsMachineIdentitySource();
    }

    public Snapshot Capture()
    {
        var snapshot = new Snapshot { SchemaVersion = 1, CapturedAt = DateTimeOffset.UtcNow.ToString("O") };
        Try("machine", "identity", () => snapshot.Machine = _machine.ReadIdentity(), snapshot);
        foreach (var error in _machine.LastErrors)
        {
            AddError(snapshot, error.Scope, error.Property, error.Message);
        }

        IReadOnlyList<AudioEndpoint> endpoints = [];
        Try("audio", "enumerate_endpoints", () => endpoints = _endpoints.EnumerateActiveEndpoints(), snapshot);
        snapshot.Endpoints = endpoints.ToList();
        foreach (var error in _endpoints.LastErrors)
        {
            AddError(snapshot, error.Scope, error.Property, error.Message);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in snapshot.Endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.DevnodeId))
            {
                AddError(snapshot, "device_tree", endpoint.EndpointId, "No devnode ID was resolved.");
            }
            else
            {
                try
                {
                    foreach (var node in _deviceTree.GetAncestors(endpoint.DevnodeId))
                    {
                        if (seen.Add(node.InstanceId)) snapshot.Devnodes.Add(node);
                    }
                }
                catch (Exception exception)
                {
                    AddError(snapshot, "device_tree", endpoint.EndpointId, exception);
                }
            }

            try
            {
                snapshot.Jacks.AddRange(_jacks.GetEndpointJackInfo(endpoint.EndpointId));
            }
            catch (Exception exception)
            {
                AddError(snapshot, "jack_info", endpoint.EndpointId, exception);
            }
        }

        try
        {
            snapshot.DisplayPaths = _displays.GetActivePaths().ToList();
        }
        catch (Exception exception)
        {
            AddError(snapshot, "display_paths", "active", exception);
        }
        return snapshot;
    }

    private static void Try(string scope, string property, Action action, Snapshot snapshot)
    {
        try { action(); }
        catch (Exception exception) { AddError(snapshot, scope, property, exception); }
    }

    private static void AddError(Snapshot snapshot, string scope, string property, Exception exception) =>
        AddError(snapshot, scope, property, $"{exception.GetType().Name}: {exception.Message}");

    private static void AddError(Snapshot snapshot, string scope, string property, string message) =>
        snapshot.Errors.Add(new Dictionary<string, string>
        {
            ["scope"] = scope,
            ["property"] = property,
            ["error"] = message
        });
}
