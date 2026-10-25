using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.Presentation.Tests;

public sealed class HotkeySettingsViewModelTests
{
    [Fact]
    public void PlaybackCycleMovesToNextActiveEndpointAndSkipsUnavailable()
    {
        var source = new FakeEndpointSource(
            Endpoint("one", "render", "active"), Endpoint("gone", "render", "unplugged"), Endpoint("two", "render", "active"));
        var writer = new FakeWriter { DefaultRender = "one" };
        var model = new HotkeySettingsViewModel(new FakeHotkeyService(), source, writer, new AudioSwitchSettings());

        var result = model.Cycle(AudioEndpointFlow.Render);

        Assert.Equal("two", writer.LastSelectedEndpoint);
        Assert.Equal(new HotkeyCycleResult(true, "two"), result);
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public void CycleDoesNothingWhenNoActiveEndpointIsAvailable()
    {
        var writer = new FakeWriter();
        var model = new HotkeySettingsViewModel(new FakeHotkeyService(), new FakeEndpointSource(), writer, new AudioSwitchSettings());

        var result = model.Cycle(AudioEndpointFlow.Capture);

        Assert.Null(writer.LastSelectedEndpoint);
        Assert.Equal("目前沒有可用的錄音端點。", model.ErrorMessage);
        Assert.Equal(new HotkeyCycleResult(false, Error: "目前沒有可用的錄音端點。"), result);
    }

    [Fact]
    public void RegistrationFailureDoesNotPersistNewHotkeys()
    {
        var settings = new AudioSwitchSettings { PlaybackHotkey = "Ctrl+Alt+P" };
        var service = new FakeHotkeyService { Result = new(false, "快捷鍵已被使用") };
        var model = new HotkeySettingsViewModel(service, new FakeEndpointSource(), new FakeWriter(), settings)
        {
            PlaybackHotkey = "Ctrl+Shift+P"
        };

        Assert.False(model.Register((IntPtr)1));

        Assert.Equal("Ctrl+Alt+P", settings.PlaybackHotkey);
        Assert.StartsWith("快捷鍵已被使用", model.ErrorMessage);
    }

    [Fact]
    public void AutostartDefaultsOffAndEnablingCallsPlatformService()
    {
        var settings = new AudioSwitchSettings();
        var service = new FakeAutostartService();
        var model = new GeneralSettingsViewModel(service, settings);

        Assert.False(model.AutostartEnabled);
        model.AutostartEnabled = true;

        Assert.Equal([true], service.Calls);
        Assert.True(settings.AutostartEnabled);
    }

    [Fact]
    public void FailedAutostartWriteKeepsPreviousSetting()
    {
        var settings = new AudioSwitchSettings();
        var model = new GeneralSettingsViewModel(new FakeAutostartService { Failure = new UnauthorizedAccessException("denied") }, settings);

        model.AutostartEnabled = true;

        Assert.False(model.AutostartEnabled);
        Assert.False(settings.AutostartEnabled);
        Assert.Equal("denied", model.ErrorMessage);
    }

    private static AudioEndpoint Endpoint(string id, string flow, string state) => new() { EndpointId = id, Flow = flow, State = state, FriendlyName = id };

    private sealed class FakeHotkeyService : IHotkeyService
    {
        public HotkeyRegistrationResult Result { get; set; } = new(true, null);
        public HotkeyRegistrationResult Register(IntPtr targetWindow, string? playbackHotkey, string? recordingHotkey) => Result;
        public void Unregister() { }
        public void Dispose() { }
    }

    private sealed class FakeAutostartService : IAutostartService
    {
        public List<bool> Calls { get; } = [];
        public Exception? Failure { get; init; }
        public void SetEnabled(bool enabled) { if (Failure is not null) throw Failure; Calls.Add(enabled); }
    }

    private sealed class FakeEndpointSource(params AudioEndpoint[] endpoints) : IAudioEndpointSource
    {
        public IReadOnlyList<PlatformError> LastErrors => [];
        public event EventHandler<EndpointNotificationEventArgs>? EndpointChanged { add { } remove { } }
        public event EventHandler<EndpointNotificationEventArgs>? DefaultEndpointChanged { add { } remove { } }
        public IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints() => endpoints;
        public void Dispose() { }
    }

    private sealed class FakeWriter : IDefaultEndpointWriter
    {
        public string? DefaultRender { get; set; }
        public string? LastSelectedEndpoint { get; private set; }
        public string? GetDefaultEndpointId(AudioEndpointFlow flow, AudioEndpointRole role) => flow == AudioEndpointFlow.Render ? DefaultRender : null;
        public EndpointSwitchResult SetDefaultEndpoint(string endpointId, AudioEndpointFlow flow)
        {
            LastSelectedEndpoint = endpointId;
            if (flow == AudioEndpointFlow.Render) DefaultRender = endpointId;
            var roles = Enum.GetValues<AudioEndpointRole>().Select(role => new EndpointRoleChangeResult(endpointId, flow, role, true, endpointId, null)).ToArray();
            return new EndpointSwitchResult(endpointId, flow, roles);
        }
        public EndpointRoleChangeResult SetDefaultEndpointRole(string endpointId, AudioEndpointFlow flow, AudioEndpointRole role) =>
            new(endpointId, flow, role, true, endpointId, null);
        public void Dispose() { }
    }
}
