using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Views;

/// <summary>Segoe Fluent Icons glyphs used across the app.</summary>
public static class Glyphs
{
    public const string Speaker = "";
    public const string Microphone = "";
    public const string Monitor = "";
    public const string Bluetooth = "";
    public const string UsbDevice = "";
    public const string Hub = "";
    public const string Laptop = "";
    public const string Unknown = "";
    public const string Virtual = "";
    public const string ChevronUp = "";
    public const string ChevronDown = "";
    public const string ChevronLeft = "";
    public const string ChevronRight = "";

    public static string ForNode(DeviceNodeViewModel node) => node.Kind switch
    {
        DeviceNodeKind.Playback => Speaker,
        DeviceNodeKind.Recording => Microphone,
        DeviceNodeKind.Display => Monitor,
        DeviceNodeKind.DockHub => Hub,
        DeviceNodeKind.Device => UsbDevice,
        DeviceNodeKind.Category => node.CategoryId switch
        {
            "內建" => Laptop,
            "藍牙／無線" => Bluetooth,
            "虛擬" => Virtual,
            TopologyBuilder.UnconfiguredUsbRootId => UsbDevice,
            _ => Unknown
        },
        _ => string.Empty
    };
}
