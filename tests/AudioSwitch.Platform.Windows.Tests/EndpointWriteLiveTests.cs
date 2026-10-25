using System.Diagnostics;
using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;

namespace AudioSwitch.Platform.Windows.Tests;

[Collection("Windows audio state mutation")]
public sealed class EndpointWriteLiveTests
{
    [LiveFact]
    public Task EndpointDescriptionCanBeChangedReadBackAndRestoredOnInternalPlaybackEndpoint() =>
        LiveStateSnapshotGuard.RunAsync(nameof(EndpointDescriptionCanBeChangedReadBackAndRestoredOnInternalPlaybackEndpoint), async () =>
        {
            using var endpointSource = new WindowsAudioEndpointSource();
            AssertAtLeastTwoPerFlow(endpointSource.EnumerateActiveEndpoints());
            var snapshot = new WindowsAudioTopologySnapshotSource().Capture();
            var tree = TopologyBuilder.Build(snapshot, PortTable.Empty());
            var internalEndpointIds = tree.Roots.Where(root => root.Id == "內建")
                .SelectMany(root => Flatten(root.Children))
                .Where(node => node.Kind == "endpoint" && node.EndpointId is not null)
                .Select(node => node.EndpointId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var endpoint = snapshot.Endpoints.FirstOrDefault(candidate =>
                internalEndpointIds.Contains(candidate.EndpointId) &&
                candidate.Flow.Equals("render", StringComparison.OrdinalIgnoreCase) &&
                candidate.State.EndsWith(".Active", StringComparison.OrdinalIgnoreCase) &&
                !snapshot.Jacks.Any(jack => jack.EndpointId.Equals(candidate.EndpointId, StringComparison.OrdinalIgnoreCase) &&
                    TopologyBuilder.IsPhysicalJack(jack)));
            Assert.True(endpoint is not null,
                "No active internal render endpoint without an IsPhysicalJack KS record was found; no other endpoint will be used.");

            using var writer = new WindowsEndpointNameWriter();
            var originalDescription = writer.ReadDeviceDescription(endpoint!.EndpointId);
            var originalFriendlyName = writer.ReadFriendlyName(endpoint.EndpointId);
            var marker = $"AudioSwitch test {Guid.NewGuid():N}";
            try
            {
                writer.WriteDeviceDescription(endpoint.EndpointId, marker);
                Assert.Equal(marker, writer.ReadDeviceDescription(endpoint.EndpointId));
                var (changed, actualName) = await PollFriendlyNameAsync(endpoint.EndpointId,
                    name => name.StartsWith(marker + " (", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
                Assert.True(changed, $"FriendlyName did not acquire the expected prefix within 3 seconds. Actual value: {actualName}");
            }
            finally
            {
                writer.WriteDeviceDescription(endpoint.EndpointId, originalDescription);
                Assert.Equal(originalDescription, writer.ReadDeviceDescription(endpoint.EndpointId));
                var (restored, actualName) = await PollFriendlyNameAsync(endpoint.EndpointId,
                    name => name.Equals(originalFriendlyName, StringComparison.Ordinal), TimeSpan.FromSeconds(3));
                Assert.True(restored, $"FriendlyName did not return to its original value within 3 seconds. Actual value: {actualName}");
            }
        });

    [LiveFact]
    public Task TestTonePlaysOnSpecifiedEndpointAndCapturePeakIsInRange() =>
        LiveStateSnapshotGuard.RunAsync(nameof(TestTonePlaysOnSpecifiedEndpointAndCapturePeakIsInRange), async () =>
        {
            using var endpoints = new WindowsAudioEndpointSource();
            using var probe = new WindowsAudioProbe();
            var active = endpoints.EnumerateActiveEndpoints();
            AssertAtLeastTwoPerFlow(active);
            var render = active.First(endpoint => endpoint.Flow == "render");
            var capture = active.First(endpoint => endpoint.Flow == "capture");
            await probe.PlayTestToneAsync(render.EndpointId, TimeSpan.FromSeconds(1));
            var peak = probe.ReadPeakLevel(capture.EndpointId);
            Assert.InRange(peak, 0f, 1f);
        });

    [LiveFact]
    public void DefaultEndpointSwitchChangesAndRestoresAllRolesForEachFlow() =>
        LiveStateSnapshotGuard.Run(nameof(DefaultEndpointSwitchChangesAndRestoresAllRolesForEachFlow), () =>
        {
            using var endpoints = new WindowsAudioEndpointSource();
            using var writer = new WindowsDefaultEndpointWriter();
            var flows = Enum.GetValues<AudioEndpointFlow>();
            var roles = Enum.GetValues<AudioEndpointRole>();
            var activeByFlow = flows.ToDictionary(flow => flow, flow => endpoints.EnumerateActiveEndpoints()
                .Where(endpoint => endpoint.Flow.Equals(flow.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray());
            var originalDefaults = flows.ToDictionary(flow => flow, flow => roles.ToDictionary(role => role,
                role => writer.GetDefaultEndpointId(flow, role)));

            foreach (var flow in flows)
            {
                var activeEndpoints = activeByFlow[flow];
                Assert.True(activeEndpoints.Length >= 2, $"Need two active {flow} endpoints for a reversible switch test.");
                Assert.All(originalDefaults[flow], pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"Missing original {flow}/{pair.Key} endpoint."));
                Assert.Contains(activeEndpoints, endpoint => originalDefaults[flow].Values.Any(originalId =>
                    !originalId!.Equals(endpoint.EndpointId, StringComparison.OrdinalIgnoreCase)));
            }

            try
            {
                foreach (var flow in flows)
                {
                    var activeEndpoints = activeByFlow[flow];
                    var target = activeEndpoints.First(endpoint => originalDefaults[flow].Values.Any(originalId =>
                        !originalId!.Equals(endpoint.EndpointId, StringComparison.OrdinalIgnoreCase)));
                    var result = writer.SetDefaultEndpoint(target.EndpointId, flow);
                    Assert.True(result.Success, string.Join(Environment.NewLine, result.Roles.Select(role => role.Error)));
                    Assert.All(result.Roles, role => Assert.Equal(target.EndpointId, role.ActualDefaultEndpointId));
                    Assert.All(roles, role => Assert.Equal(target.EndpointId, writer.GetDefaultEndpointId(flow, role)));
                }
            }
            finally
            {
                var restoreErrors = new List<string>();
                foreach (var flow in flows)
                {
                    foreach (var role in roles)
                    {
                        var originalId = originalDefaults[flow][role];
                        if (string.IsNullOrWhiteSpace(originalId))
                        {
                            restoreErrors.Add($"No saved original endpoint for {flow}/{role}.");
                            continue;
                        }
                        try
                        {
                            var restored = writer.SetDefaultEndpointRole(originalId, flow, role);
                            if (!restored.Success || !originalId.Equals(writer.GetDefaultEndpointId(flow, role), StringComparison.OrdinalIgnoreCase))
                            {
                                restoreErrors.Add($"Could not restore {flow}/{role}: {restored.Error}");
                            }
                        }
                        catch (Exception exception)
                        {
                            restoreErrors.Add($"Could not restore {flow}/{role}: {exception.GetType().Name}: {exception.Message}");
                        }
                    }
                }
                Assert.Empty(restoreErrors);
            }
        });

    private static void AssertAtLeastTwoPerFlow(IReadOnlyList<AudioEndpoint> endpoints)
    {
        foreach (var flow in Enum.GetValues<AudioEndpointFlow>())
        {
            Assert.True(endpoints.Count(endpoint => endpoint.Flow.Equals(flow.ToString(), StringComparison.OrdinalIgnoreCase)) >= 2,
                $"Need at least two active {flow} endpoints before running this live test.");
        }
    }

    private static async Task<(bool Matched, string Actual)> PollFriendlyNameAsync(string endpointId,
        Func<string, bool> predicate, TimeSpan timeout)
    {
        using var writer = new WindowsEndpointNameWriter();
        var timer = Stopwatch.StartNew();
        string actual;
        do
        {
            actual = writer.ReadFriendlyName(endpointId);
            if (predicate(actual)) return (true, actual);
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        while (timer.Elapsed < timeout);
        actual = writer.ReadFriendlyName(endpointId);
        return (predicate(actual), actual);
    }

    private static IEnumerable<TopologyNode> Flatten(IEnumerable<TopologyNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var descendant in Flatten(node.Children)) yield return descendant;
        }
    }
}
