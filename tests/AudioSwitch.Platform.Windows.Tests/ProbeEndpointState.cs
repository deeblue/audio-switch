using AudioSwitch.Core;

namespace AudioSwitch.Platform.Windows.Tests;

internal static class ProbeEndpointState
{
    public static bool IsActive(AudioEndpoint endpoint) =>
        endpoint.State.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
        endpoint.State.Equals("AudioDeviceState.Active", StringComparison.OrdinalIgnoreCase) ||
        endpoint.State == "1";
}
