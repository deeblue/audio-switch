using System.Runtime.InteropServices;
using AudioSwitch.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioSwitch.Platform.Windows;

/// <summary>Writes only PKEY_Device_DeviceDesc, as required for endpoint aliases.</summary>
public sealed class WindowsEndpointNameWriter : IEndpointNameWriter
{
    private static readonly PropertyKey DeviceDescriptionKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _disposed;

    public string ReadDeviceDescription(string endpointId)
    {
        using var device = GetDevice(endpointId);
        return device.Properties[DeviceDescriptionKey].Value as string
            ?? throw new InvalidOperationException("PKEY_Device_DeviceDesc is unavailable or is not a string.");
    }

    public string ReadFriendlyName(string endpointId)
    {
        using var device = GetDevice(endpointId);
        return device.FriendlyName;
    }

    public void WriteDeviceDescription(string endpointId, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(value);
        using var device = GetDevice(endpointId);
        device.GetPropertyInformation(StorageAccessMode.ReadWrite);
        var store = device.Properties;
        var textPointer = Marshal.StringToCoTaskMemUni(value);
        var variant = new PropVariant { vt = 31, pointerValue = textPointer }; // VT_LPWSTR
        try
        {
            store.SetValue(DeviceDescriptionKey, variant);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(textPointer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enumerator.Dispose();
    }

    private MMDevice GetDevice(string endpointId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        return _enumerator.GetDevice(endpointId);
    }
}
