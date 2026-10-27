using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using AudioSwitch.Core;

namespace AudioSwitch.Platform.Windows;

/// <summary>Active display paths through QueryDisplayConfig and DisplayConfigGetDeviceInfo: target and source names, adapter path, desktop position and EDID IDs.</summary>
public sealed class WindowsDisplayPathSource : IDisplayPathSource
{
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const int ErrorInsufficientBuffer = 122;
    private readonly WindowsDeviceTreeSource _deviceTree;

    public WindowsDisplayPathSource(WindowsDeviceTreeSource? deviceTree = null) => _deviceTree = deviceTree ?? new WindowsDeviceTreeSource();

    public IReadOnlyList<DisplayPath> GetActivePaths()
    {
        var rows = QueryPaths();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.AdapterName))
            {
                continue;
            }
            try
            {
                row.AdapterInstanceId = _deviceTree.GetDeviceInterfaceInstanceId(row.AdapterName);
            }
            catch (Exception exception)
            {
                row.Errors.Add($"adapter PnP resolution: {exception.GetType().Name}: {exception.Message}");
            }
        }
        return rows;
    }

    // The buffer sizes can change between the size query and the query when a screen connects, so the query is retried.
    private static List<DisplayPath> QueryPaths()
    {
        var status = NativeMethods.GetDisplayConfigBufferSizes(QueryOnlyActivePaths, out var pathCount, out var modeCount);
        if (status != 0) throw DisplayConfigError("GetDisplayConfigBufferSizes", status);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var paths = new PathInfo[Math.Max(1, checked((int)pathCount))];
            var modes = new ModeInfo[Math.Max(1, checked((int)modeCount))];
            var actualPathCount = pathCount;
            var actualModeCount = modeCount;
            status = NativeMethods.QueryDisplayConfig(QueryOnlyActivePaths, ref actualPathCount, paths,
                ref actualModeCount, modes, IntPtr.Zero);
            if (status == ErrorInsufficientBuffer)
            {
                pathCount = actualPathCount;
                modeCount = actualModeCount;
                continue;
            }
            if (status != 0) throw DisplayConfigError("QueryDisplayConfig", status);

            var result = new List<DisplayPath>(checked((int)actualPathCount));
            for (var index = 0; index < actualPathCount; index++)
            {
                try
                {
                    result.Add(ReadPath(paths[index], modes, actualModeCount));
                }
                catch (Exception exception)
                {
                    var path = paths[index];
                    var luid = path.TargetInfo.AdapterId;
                    result.Add(new DisplayPath
                    {
                        PathId = $"{luid.HighPart}:{luid.LowPart}:{path.TargetInfo.Id}",
                        OutputTechnology = path.TargetInfo.OutputTechnology,
                        Errors = [$"path query: {exception.GetType().Name}: {exception.Message}"]
                    });
                }
            }
            return result;
        }
        throw new InvalidOperationException("QueryDisplayConfig buffers changed repeatedly.");
    }

    // Reads one path's target (screen name, monitor path, EDID IDs), source (GDI name, desktop position) and adapter path.
    private static DisplayPath ReadPath(PathInfo path, ModeInfo[] modes, uint modeCount)
    {
        var errors = new List<string>();
        var target = new TargetName
        {
            Header = new DeviceInfoHeader { Type = 2, Size = checked((uint)Marshal.SizeOf<TargetName>()), AdapterId = path.TargetInfo.AdapterId, Id = path.TargetInfo.Id },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty
        };
        var targetStatus = GetDeviceInfo(ref target);
        if (targetStatus != 0) errors.Add($"target info error {targetStatus}");

        var source = new SourceName
        {
            Header = new DeviceInfoHeader { Type = 1, Size = checked((uint)Marshal.SizeOf<SourceName>()), AdapterId = path.SourceInfo.AdapterId, Id = path.SourceInfo.Id },
            ViewGdiDeviceName = string.Empty
        };
        var sourceStatus = GetDeviceInfo(ref source);
        if (sourceStatus != 0) errors.Add($"source info error {sourceStatus}");

        var adapter = new AdapterName
        {
            Header = new DeviceInfoHeader { Type = 4, Size = checked((uint)Marshal.SizeOf<AdapterName>()), AdapterId = path.TargetInfo.AdapterId, Id = 0 },
            AdapterDevicePath = string.Empty
        };
        var adapterStatus = GetDeviceInfo(ref adapter);
        if (adapterStatus != 0) errors.Add($"adapter info error {adapterStatus}");

        Dictionary<string, JsonElement>? sourcePosition = null;
        var modeIndex = path.SourceInfo.ModeInfoIndex;
        if (modeIndex != uint.MaxValue && modeIndex < modeCount && modeIndex < modes.Length && modes[modeIndex].InfoType == 1)
        {
            var mode = modes[modeIndex].Mode.SourceMode;
            sourcePosition = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["x"] = JsonSerializer.SerializeToElement(mode.PositionX),
                ["y"] = JsonSerializer.SerializeToElement(mode.PositionY)
            };
        }

        var luid = path.TargetInfo.AdapterId;
        var targetName = targetStatus == 0 ? NullIfEmpty(target.MonitorFriendlyDeviceName) : null;
        return new DisplayPath
        {
            PathId = $"{luid.HighPart}:{luid.LowPart}:{path.TargetInfo.Id}",
            OutputTechnology = path.TargetInfo.OutputTechnology,
            ConnectorInstance = targetStatus == 0 ? checked((int)target.ConnectorInstance) : null,
            SourceName = sourceStatus == 0 ? NullIfEmpty(source.ViewGdiDeviceName) : null,
            TargetName = targetName,
            MonitorDevicePath = targetStatus == 0 ? NullIfEmpty(target.MonitorDevicePath) : null,
            EdidManufacturerId = targetStatus == 0 ? target.EdidManufacturerId : null,
            EdidProductCodeId = targetStatus == 0 ? target.EdidProductCodeId : null,
            AdapterLuid = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["high"] = JsonSerializer.SerializeToElement(luid.HighPart),
                ["low"] = JsonSerializer.SerializeToElement(luid.LowPart)
            },
            AdapterName = adapterStatus == 0 ? NullIfEmpty(adapter.AdapterDevicePath) : null,
            SourcePosition = sourcePosition,
            Primary = sourcePosition is not null && sourcePosition.TryGetValue("x", out var x) && x.GetInt32() == 0 &&
                sourcePosition.TryGetValue("y", out var y) && y.GetInt32() == 0,
            Properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["target_id"] = JsonSerializer.SerializeToElement(path.TargetInfo.Id),
                ["source_id"] = JsonSerializer.SerializeToElement(path.SourceInfo.Id)
            },
            Errors = errors
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int GetDeviceInfo<T>(ref T request) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(request, buffer, false);
            var status = NativeMethods.DisplayConfigGetDeviceInfo(buffer);
            if (status == 0)
            {
                request = Marshal.PtrToStructure<T>(buffer);
            }
            return status;
        }
        finally
        {
            Marshal.DestroyStructure<T>(buffer);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static Win32Exception DisplayConfigError(string operation, int status) => new(status, $"{operation} returned {status}.");

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufacturerId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string AdapterDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Region
    {
        public uint Width;
        public uint Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SignalInfo
    {
        public ulong PixelRate;
        public Rational HorizontalSyncFrequency;
        public Rational VerticalSyncFrequency;
        public Region ActiveSize;
        public Region TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public int PositionX;
        public int PositionY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TargetMode
    {
        public SignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct ModeUnion
    {
        [FieldOffset(0)] public TargetMode TargetMode;
        [FieldOffset(0)] public SourceMode SourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public ModeUnion Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public PathSourceInfo SourceInfo;
        public PathTargetInfo TargetInfo;
        public uint Flags;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [In, Out] PathInfo[] pathArray,
            ref uint numModeInfoArrayElements, [In, Out] ModeInfo[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);
    }
}
