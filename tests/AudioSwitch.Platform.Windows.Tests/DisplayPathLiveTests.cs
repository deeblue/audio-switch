using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;

namespace AudioSwitch.Platform.Windows.Tests;

public sealed class DisplayPathLiveTests
{
    [LiveFact]
    public void ActiveDisplayPathIdentifiersAndConnectorFieldsMatchProbeCapture()
    {
        var expected = SnapshotJson.Read(File.ReadAllText(EndpointSourceLiveTestsPath.ProbeSnapshot));
        var source = new WindowsDisplayPathSource();

        var actual = source.GetActivePaths();

        Assert.Equal(expected.DisplayPaths.Select(path => path.PathId).Order(StringComparer.Ordinal),
            actual.Select(path => path.PathId).Order(StringComparer.Ordinal));
        foreach (var path in expected.DisplayPaths)
        {
            var found = Assert.Single(actual, item => item.PathId == path.PathId);
            Assert.Equal(path.OutputTechnology, found.OutputTechnology);
            Assert.Equal(path.ConnectorInstance, found.ConnectorInstance);
            Assert.Equal(path.TargetName, found.TargetName);
            Assert.Equal(path.AdapterName, found.AdapterName);
            Assert.Equal(path.EdidManufacturerId, found.EdidManufacturerId);
            Assert.Equal(path.EdidProductCodeId, found.EdidProductCodeId);
        }
    }
}
