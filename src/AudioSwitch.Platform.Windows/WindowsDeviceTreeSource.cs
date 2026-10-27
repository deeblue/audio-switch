using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AudioSwitch.Core;

namespace AudioSwitch.Platform.Windows;

/// <summary>Reads the PnP device tree with CfgMgr32: a device's ancestors and their properties, the present devices of a bus, and the instance ID behind a device interface.</summary>
public sealed class WindowsDeviceTreeSource : IDeviceTreeSource
{
    private static readonly DevPropKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly DevPropKey DeviceNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 10);
    private static readonly DevPropKey BusReportedDescriptionKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 4);
    private static readonly DevPropKey DeviceClassKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 9);
    private static readonly DevPropKey LocationInfoKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 15);
    private static readonly DevPropKey LocationPathsKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 37);
    private static readonly DevPropKey ContainerIdKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    private static readonly DevPropKey CapabilitiesKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 17);
    private static readonly DevPropKey RemovalPolicyKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 33);
    private static readonly DevPropKey PhysicalDeviceLocationKey = new(new Guid("540B947E-8B40-45BC-A8A2-6A0B894CBDA2"), 9);
    private static readonly DevPropKey DeviceInterfaceInstanceIdKey = new(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256);

    public IReadOnlyList<DeviceNode> GetAncestors(string instanceId, IReadOnlySet<string>? known = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var result = NativeMethods.CM_Locate_DevNodeW(out var current, instanceId, 0);
        if (result != 0)
        {
            throw ConfigManagerError("CM_Locate_DevNodeW", result);
        }

        var nodes = new List<DeviceNode>();
        var seen = new HashSet<uint>();
        while (true)
        {
            if (!seen.Add(current))
            {
                if (nodes.Count > 0)
                {
                    nodes[^1].Errors.Add(Error("parent", new InvalidOperationException("Device parent cycle detected.")));
                }
                break;
            }
            var currentId = GetDeviceId(current);
            // Reading a node takes a dozen property calls; ancestors shared with devices already read are skipped.
            if (known?.Contains(currentId) == true) break;
            var node = ReadNode(currentId, current);
            nodes.Add(node);
            result = NativeMethods.CM_Get_Parent(out var parent, current, 0);
            if (result == 0x0D) // CR_NO_SUCH_DEVNODE means this is the root.
            {
                break;
            }
            if (result != 0)
            {
                node.Errors.Add(Error("parent", ConfigManagerError("CM_Get_Parent", result)));
                break;
            }

            try
            {
                node.ParentId = GetDeviceId(parent);
            }
            catch (Exception exception)
            {
                node.Errors.Add(Error("parent_id", exception));
                break;
            }
            current = parent;
        }
        return nodes;
    }

    public IReadOnlyList<string> GetPresentDeviceIds(string enumerator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enumerator);
        const uint flags = CmGetIdListFilterEnumerator | CmGetIdListFilterPresent;
        while (true)
        {
            var status = NativeMethods.CM_Get_Device_ID_List_SizeW(out var length, enumerator, flags);
            if (status != 0)
            {
                throw ConfigManagerError("CM_Get_Device_ID_List_SizeW", status);
            }

            var buffer = new char[length];
            status = NativeMethods.CM_Get_Device_ID_ListW(enumerator, buffer, length, flags);
            if (status == CrBufferSmall)
            {
                continue; // A device arrived between the two calls.
            }
            if (status != 0)
            {
                throw ConfigManagerError("CM_Get_Device_ID_ListW", status);
            }
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
    }

    public string GetDeviceInterfaceInstanceId(string interfacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfacePath);
        var (value, type) = ReadInterfaceProperty(interfacePath, DeviceInterfaceInstanceIdKey);
        if (type != DevPropTypeString || value is not string instanceId)
        {
            throw new InvalidOperationException($"CM_Get_Device_Interface_PropertyW returned DEVPROP_TYPE 0x{type:X}, expected STRING.");
        }
        return instanceId;
    }

    private static DeviceNode ReadNode(string instanceId, uint devinst)
    {
        var node = new DeviceNode { InstanceId = instanceId };
        var friendlyName = ReadDeviceProperty(node, devinst, "friendly_name", FriendlyNameKey);
        var deviceName = ReadDeviceProperty(node, devinst, "device_name", DeviceNameKey);
        ReadDeviceProperty(node, devinst, "bus_reported_description", BusReportedDescriptionKey);
        ReadDeviceProperty(node, devinst, "class", DeviceClassKey);
        ReadDeviceProperty(node, devinst, "location_info", LocationInfoKey);
        var locationPaths = ReadDeviceProperty(node, devinst, "location_paths", LocationPathsKey);
        if (locationPaths is string[] paths)
        {
            node.LocationPaths = paths.ToList();
        }

        var containerId = ReadDeviceProperty(node, devinst, "container_id", ContainerIdKey);
        if (containerId is string container)
        {
            node.ContainerId = container;
        }

        var capabilities = ReadDeviceProperty(node, devinst, "capabilities", CapabilitiesKey);
        if (capabilities is uint capabilityBits)
        {
            node.Properties["removable"] = JsonSerializer.SerializeToElement((capabilityBits & 0x00000004) != 0);
        }
        ReadDeviceProperty(node, devinst, "removal_policy", RemovalPolicyKey);
        var pld = ReadDeviceProperty(node, devinst, "physical_device_location", PhysicalDeviceLocationKey);
        if (pld is byte[] rawPld)
        {
            node.PhysicalDeviceLocationRaw = rawPld;
        }
        node.Name = friendlyName as string ?? deviceName as string;
        return node;
    }

    private static object? ReadDeviceProperty(DeviceNode node, uint devinst, string name, DevPropKey key)
    {
        try
        {
            var (value, _) = ReadDevNodeProperty(devinst, key);
            if (value is not null)
            {
                node.Properties[name] = JsonSerializer.SerializeToElement(value);
            }
            return value;
        }
        catch (Exception exception)
        {
            node.Errors.Add(Error(name, exception));
            return null;
        }
    }

    private static (object? Value, uint Type) ReadDevNodeProperty(uint devinst, DevPropKey key)
    {
        var size = 0u;
        var status = NativeMethods.CM_Get_DevNode_PropertyW(devinst, ref key, out var type, IntPtr.Zero, ref size, 0);
        if (status == CrBufferSmall && size > 0)
        {
            return ReadPropertyBuffer(devinst, key, type, size);
        }
        throw ConfigManagerError("CM_Get_DevNode_PropertyW size query", status);
    }

    private static (object? Value, uint Type) ReadPropertyBuffer(uint devinst, DevPropKey key, uint type, uint size)
    {
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            var actualSize = size;
            var status = NativeMethods.CM_Get_DevNode_PropertyW(devinst, ref key, out type, buffer, ref actualSize, 0);
            if (status != 0)
            {
                throw ConfigManagerError("CM_Get_DevNode_PropertyW", status);
            }
            var bytes = new byte[actualSize];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return (DecodeProperty(bytes, type), type);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (object? Value, uint Type) ReadInterfaceProperty(string interfacePath, DevPropKey key)
    {
        var size = 0u;
        var status = NativeMethods.CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out var type, IntPtr.Zero, ref size, 0);
        if (status != CrBufferSmall || size == 0)
        {
            throw ConfigManagerError("CM_Get_Device_Interface_PropertyW size query", status);
        }
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            var actualSize = size;
            status = NativeMethods.CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out type, buffer, ref actualSize, 0);
            if (status != 0)
            {
                throw ConfigManagerError("CM_Get_Device_Interface_PropertyW", status);
            }
            var bytes = new byte[actualSize];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return (DecodeProperty(bytes, type), type);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static object? DecodeProperty(byte[] bytes, uint type)
    {
        if (type == DevPropTypeStringList)
        {
            return Encoding.Unicode.GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }

        switch (type & DevPropTypeMask)
        {
            case DevPropTypeString:
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            case DevPropTypeGuid when bytes.Length >= 16:
                return new Guid(bytes.AsSpan(0, 16)).ToString("D");
            case DevPropTypeInt32 when bytes.Length >= 4:
                return BitConverter.ToInt32(bytes, 0);
            case DevPropTypeUInt32 when bytes.Length >= 4:
                return BitConverter.ToUInt32(bytes, 0);
            case DevPropTypeBinary:
                return bytes;
            default:
                return null;
        }
    }

    private static string GetDeviceId(uint devinst)
    {
        var buffer = new StringBuilder(4096);
        var status = NativeMethods.CM_Get_Device_IDW(devinst, buffer, checked((uint)buffer.Capacity), 0);
        if (status != 0)
        {
            throw ConfigManagerError("CM_Get_Device_IDW", status);
        }
        return buffer.ToString();
    }

    private static Dictionary<string, string> Error(string property, Exception exception) =>
        new() { ["property"] = property, ["error"] = $"{exception.GetType().Name}: {exception.Message}" };

    private static Win32Exception ConfigManagerError(string operation, int status) =>
        new(status, $"{operation} returned CR 0x{status:X4}");

    private const int CrBufferSmall = 0x1A;
    private const uint CmGetIdListFilterEnumerator = 0x00000001;
    private const uint CmGetIdListFilterPresent = 0x00000100;
    private const uint DevPropTypeMask = 0x0FFF;
    private const uint DevPropTypeInt32 = 0x0006;
    private const uint DevPropTypeUInt32 = 0x0007;
    private const uint DevPropTypeGuid = 0x000D;
    private const uint DevPropTypeString = 0x0012;
    private const uint DevPropTypeBinary = 0x0003;
    private const uint DevPropTypeStringList = 0x2012;

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public DevPropKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    private static class NativeMethods
    {
        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern int CM_Locate_DevNodeW(out uint devinst, string deviceId, uint flags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern int CM_Get_Device_IDW(uint devinst, StringBuilder buffer, uint bufferLength, uint flags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern int CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern int CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint bufferLength, uint flags);

        [DllImport("CfgMgr32.dll")]
        internal static extern int CM_Get_Parent(out uint parent, uint devinst, uint flags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern int CM_Get_DevNode_PropertyW(uint devinst, ref DevPropKey propertyKey, out uint propertyType,
            IntPtr propertyBuffer, ref uint propertyBufferSize, uint flags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern int CM_Get_Device_Interface_PropertyW(string interfacePath, ref DevPropKey propertyKey,
            out uint propertyType, IntPtr propertyBuffer, ref uint propertyBufferSize, uint flags);
    }

}
