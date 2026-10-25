using System.Reflection;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioSwitch.Platform.Windows;

/// <summary>Writes only PKEY_Device_DeviceDesc, as required for endpoint aliases.</summary>
public sealed class WindowsEndpointNameWriter : IDisposable
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
        var getStore = typeof(MMDevice).GetMethod("GetPropertyInformation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MMDevice).FullName, "GetPropertyInformation");
        var accessType = getStore.GetParameters()[0].ParameterType;
        _ = getStore.Invoke(device, [Enum.Parse(accessType, "ReadWrite")]);
        var store = device.Properties;

        var variantType = typeof(PropVariant);
        var variant = Activator.CreateInstance(variantType);
        var typeField = variantType.GetField("vt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)
            ?? throw new MissingFieldException(variantType.FullName, "vt");
        var pointerField = variantType.GetField("pointerValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)
            ?? throw new MissingFieldException(variantType.FullName, "pointerValue");
        var textPointer = Marshal.StringToCoTaskMemUni(value);
        typeField.SetValue(variant, Convert.ChangeType(31, typeField.FieldType)); // VT_LPWSTR
        pointerField.SetValue(variant, textPointer);
        var setValue = typeof(PropertyStore).GetMethod("SetValue", [typeof(PropertyKey), variantType])
            ?? throw new MissingMethodException(typeof(PropertyStore).FullName, "SetValue");
        try
        {
            setValue.Invoke(store, [DeviceDescriptionKey, variant]);
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
