using System.Runtime.InteropServices;
using AudioSwitch.Core;
using NAudio.CoreAudioApi;

namespace AudioSwitch.Platform.Windows;

/// <summary>Sets the default endpoint per role through the undocumented IPolicyConfig COM interface, as SoundSwitch does. Windows has no supported API for this, so an update may break it; every change is read back.</summary>
public sealed class WindowsDefaultEndpointWriter : IDefaultEndpointWriter
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private object? _policyConfigClient;
    private IPolicyConfig? _policyConfig;
    private bool _disposed;

    public string? GetDefaultEndpointId(AudioEndpointFlow flow, AudioEndpointRole role)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_enumerator.TryGetDefaultAudioEndpoint(ToDataFlow(flow), ToNaudioRole(role), out var endpoint) || endpoint is null)
        {
            return null;
        }

        using (endpoint)
        {
            return endpoint.ID;
        }
    }

    public EndpointSwitchResult SetDefaultEndpoint(string endpointId, AudioEndpointFlow flow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var results = Enum.GetValues<AudioEndpointRole>()
            .Select(role => SetDefaultEndpointRole(endpointId, flow, role))
            .ToArray();
        return new EndpointSwitchResult(endpointId, flow, results);
    }

    public EndpointRoleChangeResult SetDefaultEndpointRole(string endpointId, AudioEndpointFlow flow, AudioEndpointRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string? error = null;
        try
        {
            var status = GetPolicyConfig().SetDefaultEndpoint(endpointId, ToPolicyRole(role));
            if (status < 0)
            {
                error = Marshal.GetExceptionForHR(status)?.Message ?? $"IPolicyConfig.SetDefaultEndpoint returned HRESULT 0x{status:X8}.";
            }
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
        }

        string? actualEndpointId = null;
        try
        {
            actualEndpointId = GetDefaultEndpointId(flow, role);
        }
        catch (Exception exception)
        {
            error = AppendError(error, $"Readback failed: {exception.GetType().Name}: {exception.Message}");
        }

        if (!string.Equals(endpointId, actualEndpointId, StringComparison.OrdinalIgnoreCase))
        {
            error = AppendError(error, $"Readback returned '{actualEndpointId ?? "<none>"}' instead of the requested endpoint.");
        }

        return new EndpointRoleChangeResult(endpointId, flow, role, error is null, actualEndpointId, error);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _enumerator.Dispose();
        if (_policyConfigClient is not null && Marshal.IsComObject(_policyConfigClient))
        {
            Marshal.ReleaseComObject(_policyConfigClient);
        }
        _policyConfig = null;
        _policyConfigClient = null;
    }

    private IPolicyConfig GetPolicyConfig()
    {
        if (_policyConfig is not null)
        {
            return _policyConfig;
        }

        _policyConfigClient = new PolicyConfigClient();
        _policyConfig = (IPolicyConfig)_policyConfigClient;
        return _policyConfig;
    }

    private static DataFlow ToDataFlow(AudioEndpointFlow flow) => flow switch
    {
        AudioEndpointFlow.Render => DataFlow.Render,
        AudioEndpointFlow.Capture => DataFlow.Capture,
        _ => throw new ArgumentOutOfRangeException(nameof(flow))
    };

    private static Role ToNaudioRole(AudioEndpointRole role) => role switch
    {
        AudioEndpointRole.Console => Role.Console,
        AudioEndpointRole.Multimedia => Role.Multimedia,
        AudioEndpointRole.Communications => Role.Communications,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static PolicyConfigRole ToPolicyRole(AudioEndpointRole role) => role switch
    {
        AudioEndpointRole.Console => PolicyConfigRole.Console,
        AudioEndpointRole.Multimedia => PolicyConfigRole.Multimedia,
        AudioEndpointRole.Communications => PolicyConfigRole.Communications,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static string? AppendError(string? existing, string next) => existing is null ? next : $"{existing} {next}";

    // Interface IID and 12-slot vtable follow the public AudioDeviceCmdlets declaration:
    // https://github.com/frgnca/AudioDeviceCmdlets/blob/master/SOURCE/IPolicyConfig.cs
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod, IntPtr defaultValue, IntPtr minimumValue);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool effectStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool effectStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PolicyConfigRole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }

    // CLSID_CPolicyConfigClient from AudioDeviceCmdlets/SOURCE/PolicyConfigClient.cs.
    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    [ClassInterface(ClassInterfaceType.None)]
    private class PolicyConfigClient { }

    private enum PolicyConfigRole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }
}
