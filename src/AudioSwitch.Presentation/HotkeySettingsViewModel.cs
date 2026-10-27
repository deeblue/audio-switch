using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

/// <summary>The outcome of one shortcut press.</summary>
public sealed record HotkeyCycleResult(bool Success, string? EndpointName = null, string? Error = null);

/// <summary>The Shortcuts page and the shortcut action: registers the two shortcuts, keeping the previous ones if registration fails, and moves the default to the next available endpoint.</summary>
public sealed class HotkeySettingsViewModel : INotifyPropertyChanged
{
    private readonly IHotkeyService _hotkeyService;
    private readonly IAudioEndpointSource _endpointSource;
    private readonly IDefaultEndpointWriter _endpointWriter;
    private readonly AudioSwitchSettings _settings;
    private string? _playbackHotkey;
    private string? _recordingHotkey;
    private string? _errorMessage;

    public string? PlaybackHotkey { get => _playbackHotkey; set => Set(ref _playbackHotkey, value); }
    public string? RecordingHotkey { get => _recordingHotkey; set => Set(ref _recordingHotkey, value); }
    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    public HotkeySettingsViewModel(IHotkeyService hotkeyService, IAudioEndpointSource endpointSource,
        IDefaultEndpointWriter endpointWriter, AudioSwitchSettings settings)
    {
        _hotkeyService = hotkeyService;
        _endpointSource = endpointSource;
        _endpointWriter = endpointWriter;
        _settings = settings;
        _playbackHotkey = settings.PlaybackHotkey;
        _recordingHotkey = settings.RecordingHotkey;
    }

    public bool Register(IntPtr targetWindow)
    {
        var result = _hotkeyService.Register(targetWindow, EmptyToNull(PlaybackHotkey), EmptyToNull(RecordingHotkey));
        if (!result.Success)
        {
            var error = result.Error ?? Loc.T("無法註冊快捷鍵。");
            var restore = _hotkeyService.Register(targetWindow, _settings.PlaybackHotkey, _settings.RecordingHotkey);
            if (restore.Success)
                ErrorMessage = _settings.PlaybackHotkey is null && _settings.RecordingHotkey is null ? error : Loc.F("{0} 已保留原本的快捷鍵。", error);
            else if (string.IsNullOrWhiteSpace(restore.Error) || string.Equals(restore.Error, result.Error, StringComparison.Ordinal))
                ErrorMessage = Loc.F("{0} 原本的快捷鍵也無法重新註冊。", error);
            else
                ErrorMessage = Loc.F("{0} 原本的快捷鍵也無法重新註冊：{1}", error, restore.Error);
            return false;
        }
        _settings.PlaybackHotkey = EmptyToNull(PlaybackHotkey);
        _settings.RecordingHotkey = EmptyToNull(RecordingHotkey);
        ErrorMessage = null;
        return true;
    }

    public HotkeyCycleResult Cycle(AudioEndpointFlow flow)
    {
        try
        {
            var expectedFlow = flow == AudioEndpointFlow.Render ? "render" : "capture";
            var endpoints = _endpointSource.EnumerateActiveEndpoints().Where(endpoint =>
                IsActive(endpoint) && endpoint.Flow.Equals(expectedFlow, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (endpoints.Length == 0)
            {
                ErrorMessage = Loc.T(flow == AudioEndpointFlow.Render ? "目前沒有可用的播放端點。" : "目前沒有可用的錄音端點。");
                return new HotkeyCycleResult(false, Error: ErrorMessage);
            }
            var currentId = _endpointWriter.GetDefaultEndpointId(flow, AudioEndpointRole.Multimedia);
            var currentIndex = Array.FindIndex(endpoints, endpoint => endpoint.EndpointId.Equals(currentId, StringComparison.OrdinalIgnoreCase));
            var target = endpoints[(currentIndex + 1) % endpoints.Length];
            var result = _endpointWriter.SetDefaultEndpoint(target.EndpointId, flow);
            var readback = _endpointWriter.GetDefaultEndpointId(flow, AudioEndpointRole.Multimedia);
            if (!result.Success || !target.EndpointId.Equals(readback, StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = string.Join(" ", result.Roles.Where(role => !role.Success).Select(role => role.Error).Where(message => !string.IsNullOrWhiteSpace(message)));
                if (string.IsNullOrWhiteSpace(ErrorMessage)) ErrorMessage = Loc.T("Windows 未確認快捷鍵切換結果。");
                return new HotkeyCycleResult(false, Error: ErrorMessage);
            }
            ErrorMessage = null;
            return new HotkeyCycleResult(true, EndpointName: target.FriendlyName);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return new HotkeyCycleResult(false, Error: ErrorMessage);
        }
    }

    private static bool IsActive(AudioEndpoint endpoint) => endpoint.State.Equals("active", StringComparison.OrdinalIgnoreCase) ||
        endpoint.State.EndsWith(".active", StringComparison.OrdinalIgnoreCase);

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
