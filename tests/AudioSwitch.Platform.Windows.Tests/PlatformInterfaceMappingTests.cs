using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;
using System.Reflection;

namespace AudioSwitch.Platform.Windows.Tests;

public sealed class PlatformInterfaceMappingTests
{
    private static readonly HashSet<string> NonPlatformInterfaces = new(StringComparer.Ordinal);
    private static readonly HashSet<string> NotYetImplementedInterfaces = new(StringComparer.Ordinal)
    {
        "ITrayAnchor"
    };

    [Fact]
    public void EveryCorePlatformInterfaceHasAWindowsImplementation()
    {
        var coreInterfaces = typeof(IAudioEndpointSource).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface && type.IsPublic)
            .Where(type => !NonPlatformInterfaces.Contains(type.Name) && !NotYetImplementedInterfaces.Contains(type.Name))
            .ToArray();
        var implementations = typeof(WindowsAudioEndpointSource).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .ToArray();

        var missing = coreInterfaces
            .Where(contract => !implementations.Any(implementation => contract.IsAssignableFrom(implementation)))
            .Select(contract => contract.FullName)
            .ToArray();

        Assert.Empty(missing);
    }
}
