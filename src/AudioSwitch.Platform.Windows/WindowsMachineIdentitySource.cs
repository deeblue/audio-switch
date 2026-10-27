using AudioSwitch.Core;
using Microsoft.Win32;

namespace AudioSwitch.Platform.Windows;

/// <summary>Manufacturer, model and machine type from the BIOS information in the registry.</summary>
public sealed class WindowsMachineIdentitySource : IMachineIdentitySource
{
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
    private readonly List<PlatformError> _lastErrors = [];

    public IReadOnlyList<PlatformError> LastErrors => _lastErrors;

    public MachineIdentity ReadIdentity()
    {
        _lastErrors.Clear();
        RegistryKey? key = null;
        try
        {
            key = Registry.LocalMachine.OpenSubKey(BiosKey, writable: false);
        }
        catch (Exception exception)
        {
            _lastErrors.Add(new PlatformError("machine", BiosKey, $"{exception.GetType().Name}: {exception.Message}"));
        }

        using (key)
        {
            var model = ReadString(key, "SystemSKU");
            if (model is not null && model.Contains("ThinkPad", StringComparison.OrdinalIgnoreCase) && model.Contains("_FM_", StringComparison.Ordinal))
            {
                model = model[(model.IndexOf("_FM_", StringComparison.Ordinal) + 4)..];
            }
            return new MachineIdentity
            {
                Manufacturer = ReadString(key, "SystemManufacturer"),
                Model = model,
                MtM = ReadString(key, "SystemProductName")
            };
        }
    }

    private string? ReadString(RegistryKey? key, string name)
    {
        try
        {
            return key?.GetValue(name) as string is { Length: > 0 } value ? value.Trim() : null;
        }
        catch (Exception exception)
        {
            _lastErrors.Add(new PlatformError("machine", name, $"{exception.GetType().Name}: {exception.Message}"));
            return null;
        }
    }
}
