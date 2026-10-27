using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

/// <summary>A current default device and the place it is drawn on the laptop: a port ID or a built-in marker ID.</summary>
public sealed record ActiveDevice(string DiagramId, string Name);

/// <summary>Where the current default playback and recording devices are, so the laptop drawing can mark them.</summary>
public sealed record ActiveDevices(ActiveDevice? Playback, ActiveDevice? Recording)
{
    public static ActiveDevices None { get; } = new(null, null);

    public static ActiveDevices Find(Snapshot snapshot, TopologyTree tree, string? playbackEndpointId, string? recordingEndpointId) =>
        new(Locate(snapshot, tree, playbackEndpointId), Locate(snapshot, tree, recordingEndpointId));

    /// <summary>
    /// The port an endpoint is on, or the built-in marker for a built-in speaker or microphone. Bluetooth, virtual and
    /// unmatched endpoints, and a headphone jack without a 3.5mm port, have no place on the drawing.
    /// </summary>
    public static string? DiagramId(Snapshot snapshot, TopologyEndpoint endpoint)
    {
        if (endpoint.InterfaceId is not null) return endpoint.InterfaceId;
        if (endpoint.Category != "C") return null;
        if (snapshot.Jacks.Any(jack => jack.EndpointId.Equals(endpoint.EndpointId, StringComparison.OrdinalIgnoreCase) && TopologyBuilder.IsPhysicalJack(jack)))
            return null;
        return endpoint.Flow.Equals("capture", StringComparison.OrdinalIgnoreCase)
            ? PortDiagramViewModel.BuiltInMicrophoneId
            : PortDiagramViewModel.BuiltInSpeakerId;
    }

    private static ActiveDevice? Locate(Snapshot snapshot, TopologyTree tree, string? endpointId)
    {
        var endpoint = endpointId is null
            ? null
            : tree.Endpoints.FirstOrDefault(item => item.EndpointId.Equals(endpointId, StringComparison.OrdinalIgnoreCase));
        return endpoint is not null && DiagramId(snapshot, endpoint) is { } id ? new ActiveDevice(id, endpoint.Name ?? endpoint.EndpointId) : null;
    }
}
