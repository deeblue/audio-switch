using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using AudioSwitch.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioSwitch.Platform.Windows;

/// <summary>Jack information of an endpoint through its DeviceTopology connector: IKsJackDescription (connector type and location), IKsJackDescription2 (capabilities) and IKsJackSinkInformation (the screen behind display audio).</summary>
public sealed class WindowsJackInfoSource : IJackInfoSource
{
    private static readonly Guid JackDescriptionIid = new("4509F757-2D46-4637-8E62-CE7DB944F57B");
    private static readonly Guid JackDescription2Iid = new("478F3A9B-E0C9-4827-9228-6F5505FFE76A");
    private static readonly Guid JackSinkInformationIid = new("D9BD72ED-290F-4581-9FF3-61027A8FE532");
    private static readonly FieldInfo NativePartField = typeof(Part).GetField("partInterface", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("NAudio Part no longer exposes its native IPart projection.");

    private readonly WindowsDeviceTreeSource _deviceTree;

    public WindowsJackInfoSource(WindowsDeviceTreeSource? deviceTree = null) => _deviceTree = deviceTree ?? new WindowsDeviceTreeSource();

    public IReadOnlyList<JackRecord> GetEndpointJackInfo(string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(endpointId);
        var records = new List<JackRecord>();
        var errors = new List<Dictionary<string, string>>();
        Part part;
        try
        {
            // The endpoint's connector leads to the adapter's part, which implements the KS jack interfaces.
            var endpointConnector = device.DeviceTopology.GetConnector(0);
            part = endpointConnector.ConnectedTo.Part;
        }
        catch (Exception exception)
        {
            return [new JackRecord { EndpointId = endpointId, Errors = [Error("device_topology", exception)] }];
        }

        // NAudio does not expose the native IPart, which the jack interfaces are activated from.
        var nativePart = NativePartField.GetValue(part);
        if (nativePart is null)
        {
            return [new JackRecord { EndpointId = endpointId, Errors = [Error("device_topology_part", new InvalidOperationException("Native IPart interface was unavailable."))] }];
        }

        string? adapterDeviceId = null;
        string? adapterInstanceId = null;
        try
        {
            adapterDeviceId = part.DeviceTopology.DeviceId;
        }
        catch (Exception exception)
        {
            errors.Add(Error("topology_device_id", exception));
        }
        if (!string.IsNullOrWhiteSpace(adapterDeviceId))
        {
            try
            {
                adapterInstanceId = _deviceTree.GetDeviceInterfaceInstanceId(adapterDeviceId);
            }
            catch (Exception exception)
            {
                errors.Add(Error("topology_instance_id", exception));
            }
        }

        using var jackDescription = Activate(nativePart, JackDescriptionIid, endpointId, "IKsJackDescription", errors);
        using var jackDescription2 = Activate(nativePart, JackDescription2Iid, endpointId, "IKsJackDescription2", errors);
        using var jackSink = Activate(nativePart, JackSinkInformationIid, endpointId, "IKsJackSinkInformation", errors);

        var jackCount = ReadCount(jackDescription, endpointId, "jack_count", errors);
        var jack2Count = ReadCount(jackDescription2, endpointId, "jack2_count", errors);
        var sink = ReadSink(jackSink, endpointId, errors);
        // One record per jack; display audio carries its sink on the first record.
        var count = Math.Max(Math.Max(jackCount, jack2Count), sink is null ? 0u : 1u);
        for (uint index = 0; index < count; index++)
        {
            var jack = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            int? capabilities = null;
            var recordErrors = new List<Dictionary<string, string>>();
            if (jackDescription is not null && index < jackCount)
            {
                try
                {
                    var description = ReadDescription(jackDescription.Pointer, index);
                    jack = ToJack(description);
                }
                catch (Exception exception)
                {
                    var error = Error($"jack_{index}", exception);
                    recordErrors.Add(error);
                    errors.Add(error);
                }
            }
            if (jackDescription2 is not null && index < jack2Count)
            {
                try
                {
                    capabilities = checked((int)ReadDescription2(jackDescription2.Pointer, index).JackCapabilities);
                }
                catch (Exception exception)
                {
                    var error = Error($"jack2_{index}", exception);
                    recordErrors.Add(error);
                    errors.Add(error);
                }
            }
            records.Add(new JackRecord
            {
                EndpointId = endpointId,
                Jack = jack,
                Capabilities = capabilities,
                Sink = index == 0 ? sink : null,
                AdapterDeviceId = adapterDeviceId,
                AdapterInstanceId = adapterInstanceId,
                Errors = recordErrors
            });
        }

        if (records.Count == 0 && errors.Count > 0)
        {
            records.Add(new JackRecord { EndpointId = endpointId, AdapterDeviceId = adapterDeviceId, AdapterInstanceId = adapterInstanceId, Errors = errors });
        }
        return records;
    }

    private static ActivatedInterface? Activate(object part, Guid iid, string endpointId, string name,
        ICollection<Dictionary<string, string>> errors)
    {
        IntPtr activated = IntPtr.Zero;
        IntPtr queried = IntPtr.Zero;
        try
        {
            var status = InvokePartActivate(part, iid, out activated);
            Marshal.ThrowExceptionForHR(status);
            var queryIid = iid;
            status = Marshal.QueryInterface(activated, in queryIid, out queried);
            Marshal.ThrowExceptionForHR(status);
            return new ActivatedInterface(queried);
        }
        catch (Exception exception)
        {
            errors.Add(Error(name, exception, endpointId));
            if (queried != IntPtr.Zero) Marshal.Release(queried);
            return null;
        }
        finally
        {
            if (activated != IntPtr.Zero) Marshal.Release(activated);
        }
    }

    private static int InvokePartActivate(object part, Guid iid, out IntPtr activated)
    {
        // NAudio 3.3.0 keeps its native IPart projection private; invoke its public COM projection method reflectively.
        var method = NativePartField.FieldType.GetMethod("Activate")
            ?? throw new MissingMethodException(NativePartField.FieldType.FullName, "Activate");
        var parameters = method.GetParameters();
        var context = Enum.ToObject(parameters[0].ParameterType, 1); // CLSCTX_INPROC_SERVER
        object?[] arguments = [context, iid, IntPtr.Zero];
        var result = method.Invoke(part, arguments);
        activated = arguments[2] is IntPtr pointer ? pointer : IntPtr.Zero;
        return result is int status ? status : throw new InvalidOperationException("IPart.Activate returned no HRESULT.");
    }

    private static uint ReadCount(ActivatedInterface? iface, string endpointId, string property,
        ICollection<Dictionary<string, string>> errors)
    {
        if (iface is null) return 0;
        try
        {
            var method = GetMethod<CountDelegate>(iface.Pointer, 3);
            Marshal.ThrowExceptionForHR(method(iface.Pointer, out var count));
            return count;
        }
        catch (Exception exception)
        {
            errors.Add(Error(property, exception, endpointId));
            return 0;
        }
    }

    private static NativeJackDescription ReadDescription(IntPtr iface, uint index)
    {
        var method = GetMethod<JackDescriptionDelegate>(iface, 4);
        Marshal.ThrowExceptionForHR(method(iface, index, out var description));
        return description;
    }

    private static NativeJackDescription2 ReadDescription2(IntPtr iface, uint index)
    {
        var method = GetMethod<JackDescription2Delegate>(iface, 4);
        Marshal.ThrowExceptionForHR(method(iface, index, out var description));
        return description;
    }

    private static Dictionary<string, JsonElement>? ReadSink(ActivatedInterface? iface, string endpointId,
        ICollection<Dictionary<string, string>> errors)
    {
        if (iface is null) return null;
        try
        {
            var method = GetMethod<JackSinkInformationDelegate>(iface.Pointer, 3);
            Marshal.ThrowExceptionForHR(method(iface.Pointer, out var sink));
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["connection_type"] = JsonSerializer.SerializeToElement(sink.ConnectionType),
                ["manufacturer_id"] = JsonSerializer.SerializeToElement(sink.ManufacturerId),
                ["product_id"] = JsonSerializer.SerializeToElement(sink.ProductId),
                ["audio_latency"] = JsonSerializer.SerializeToElement(sink.AudioLatency),
                ["hdcp_capable"] = JsonSerializer.SerializeToElement(sink.HdcpCapable != 0),
                ["ai_capable"] = JsonSerializer.SerializeToElement(sink.AiCapable != 0),
                ["sink_description_length"] = JsonSerializer.SerializeToElement(sink.SinkDescriptionLength),
                ["sink_description"] = JsonSerializer.SerializeToElement((sink.SinkDescription ?? string.Empty).Split('\0')[0]),
                ["port_id"] = JsonSerializer.SerializeToElement(new { high = sink.PortId.HighPart, low = sink.PortId.LowPart })
            };
        }
        catch (Exception exception)
        {
            errors.Add(Error("sink_information", exception, endpointId));
            return null;
        }
    }

    private static Dictionary<string, JsonElement> ToJack(NativeJackDescription description) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["channel_mapping"] = JsonSerializer.SerializeToElement(description.ChannelMapping),
        ["color"] = JsonSerializer.SerializeToElement(description.Color),
        ["connection_type"] = JsonSerializer.SerializeToElement(description.ConnectionType),
        ["geo_location"] = JsonSerializer.SerializeToElement(description.GeoLocation),
        ["general_location"] = JsonSerializer.SerializeToElement(description.GeneralLocation),
        ["port_connection"] = JsonSerializer.SerializeToElement(description.PortConnection),
        ["is_connected"] = JsonSerializer.SerializeToElement(description.IsConnected != 0)
    };

    private static T GetMethod<T>(IntPtr instance, int methodIndex) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(vtable, methodIndex * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }

    private static Dictionary<string, string> Error(string property, Exception exception, string? scope = null) =>
        new() { ["scope"] = scope ?? string.Empty, ["property"] = property, ["error"] = $"{exception.GetType().Name}: {exception.Message}" };

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CountDelegate(IntPtr self, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int JackDescriptionDelegate(IntPtr self, uint index, out NativeJackDescription description);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int JackDescription2Delegate(IntPtr self, uint index, out NativeJackDescription2 description);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int JackSinkInformationDelegate(IntPtr self, out NativeJackSinkInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeJackDescription
    {
        public uint ChannelMapping;
        public uint Color;
        public uint ConnectionType;
        public uint GeoLocation;
        public uint GeneralLocation;
        public uint PortConnection;
        public int IsConnected;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeJackDescription2
    {
        public uint DeviceStateInfo;
        public uint JackCapabilities;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeLuid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeJackSinkInformation
    {
        public uint ConnectionType;
        public ushort ManufacturerId;
        public ushort ProductId;
        public ushort AudioLatency;
        public int HdcpCapable;
        public int AiCapable;
        public byte SinkDescriptionLength;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? SinkDescription;
        public NativeLuid PortId;
    }

    private sealed class ActivatedInterface(IntPtr pointer) : IDisposable
    {
        public IntPtr Pointer { get; } = pointer;
        public void Dispose()
        {
            if (Pointer != IntPtr.Zero) Marshal.Release(Pointer);
        }
    }
}
