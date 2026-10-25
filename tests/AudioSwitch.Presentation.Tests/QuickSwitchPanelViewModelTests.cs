using AudioSwitch.Core;

namespace AudioSwitch.Presentation.Tests;

public sealed class QuickSwitchPanelViewModelTests
{
    [Fact]
    public void RefreshGroupsEndpointsByInterfaceAndShowsCurrentDefault()
    {
        var writer = new FakeWriter { DefaultRender = "one" };
        var model = CreateViewModel(writer);
        model.Refresh(Tree(("one", "耳機", "A"), ("two", "耳機", "B")), AudioEndpointFlow.Render);

        Assert.Equal(new[] { "A", "B" }, model.Groups.Select(group => group.Name));
        Assert.True(model.Groups[0].Endpoints[0].IsDefault);
        Assert.False(model.Groups[1].Endpoints[0].IsDefault);
    }

    [Fact]
    public void FailedSwitchShowsReasonAndDoesNotMoveDefaultMark()
    {
        var writer = new FakeWriter { DefaultRender = "one", Fail = true };
        var model = CreateViewModel(writer);
        model.Refresh(Tree(("one", "耳機", "A"), ("two", "喇叭", "B")), AudioEndpointFlow.Render);

        model.SelectEndpoint("two");

        Assert.Contains("拒絕切換", model.ErrorMessage);
        Assert.True(model.Groups[0].Endpoints.Single().IsDefault);
        Assert.False(model.Groups[1].Endpoints.Single().IsDefault);
    }

    [Fact]
    public void RefreshRemovesDisappearedEndpoint()
    {
        var model = CreateViewModel(new FakeWriter());
        model.Refresh(Tree(("one", "耳機", "A"), ("two", "喇叭", "B")), AudioEndpointFlow.Render);
        model.Refresh(Tree(("one", "耳機", "A")), AudioEndpointFlow.Render);
        Assert.Equal(new[] { "one" }, model.Groups.SelectMany(group => group.Endpoints).Select(endpoint => endpoint.EndpointId));
    }

    [Fact]
    public void RefreshOnlyShowsEndpointsForSelectedFlow()
    {
        var root = new TopologyRoot { Id = "A", Name = "A", Source = "test" };
        root.Children.Add(new TopologyNode { Name = "播放", EndpointId = "render", Kind = "endpoint" });
        root.Children.Add(new TopologyNode { Name = "錄音", EndpointId = "capture", Kind = "endpoint" });
        var tree = new TopologyTree
        {
            Roots = [root],
            DisplayGroups = [],
            Endpoints = [new("render", "播放", "render", "A", "A", null), new("capture", "錄音", "capture", "A", "A", null)]
        };
        var model = CreateViewModel(new FakeWriter());
        model.Refresh(tree, AudioEndpointFlow.Capture);
        Assert.Equal("capture", Assert.Single(Assert.Single(model.Groups).Endpoints).EndpointId);
    }

    [Fact]
    public void FirstRunNoticeIsReturnedOnceAndPersisted()
    {
        var settings = new AudioSwitchSettings();
        var model = CreateViewModel(new FakeWriter());
        Assert.True(model.TryMarkFirstRunNotice(settings));
        Assert.False(model.TryMarkFirstRunNotice(settings));
        Assert.True(settings.FirstRunNotificationShown);
    }

    [Fact]
    public void RefreshAndFlowChangeUseSnapshotInterfaceWithoutStartingPeakSampling()
    {
        var source = new FakeSnapshotSource();
        var probe = new FakeProbe();
        var model = CreateViewModel(new FakeWriter(), source, probe);

        model.Refresh();
        model.SelectFlow(AudioEndpointFlow.Capture);

        Assert.Equal(2, source.CaptureCalls);
        Assert.Equal(AudioEndpointFlow.Capture, model.Flow);
        Assert.Equal(0, probe.StartPeakSamplingCalls);
    }

    [Fact]
    public async Task PlayToneUsesProbeAndShowsFailureInViewModel()
    {
        var probe = new FakeProbe { ToneError = new InvalidOperationException("試音失敗") };
        var model = CreateViewModel(new FakeWriter(), probe: probe);

        Assert.False(await model.PlayToneAsync("endpoint"));

        Assert.Equal("endpoint", probe.LastToneEndpoint);
        Assert.Equal("試音失敗", model.ErrorMessage);
    }

    [Fact]
    public void SnapshotFailureIsExposedAsViewModelError()
    {
        var source = new FakeSnapshotSource { CaptureError = new InvalidOperationException("快照失敗") };
        var model = CreateViewModel(new FakeWriter(), source);

        model.Refresh();

        Assert.Equal("快照失敗", model.ErrorMessage);
    }

    private static TopologyTree Tree(params (string Id, string Name, string Root)[] values)
    {
        var roots = values.Select(value => value.Root).Distinct().Select(id => new TopologyRoot { Id = id, Name = id, Source = "test" }).ToArray();
        var endpoints = values.Select(value => new TopologyEndpoint(value.Id, value.Name, "render", "A", value.Root, null)).ToArray();
        foreach (var value in values)
            roots.Single(root => root.Id == value.Root).Children.Add(new TopologyNode { Name = value.Name, EndpointId = value.Id, Kind = "endpoint" });
        return new TopologyTree { Roots = roots, Endpoints = endpoints, DisplayGroups = [] };
    }

    private static QuickSwitchPanelViewModel CreateViewModel(FakeWriter writer, FakeSnapshotSource? source = null, FakeProbe? probe = null) =>
        new(source ?? new FakeSnapshotSource(), PortTable.Empty(), writer, probe ?? new FakeProbe());

    private sealed class FakeSnapshotSource : IAudioTopologySnapshotSource
    {
        public int CaptureCalls { get; private set; }
        public Exception? CaptureError { get; set; }
        public Snapshot Capture()
        {
            CaptureCalls++;
            if (CaptureError is not null) throw CaptureError;
            return new Snapshot();
        }
    }

    private sealed class FakeProbe : IAudioProbe
    {
        public Exception? ToneError { get; set; }
        public string? LastToneEndpoint { get; private set; }
        public int StartPeakSamplingCalls { get; private set; }
        public Task PlayTestToneAsync(string endpointId, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            LastToneEndpoint = endpointId;
            return ToneError is null ? Task.CompletedTask : Task.FromException(ToneError);
        }
        public void StartPeakSampling(string endpointId) => StartPeakSamplingCalls++;
        public float ReadPeakLevel(string endpointId) => 0;
        public Task StopPeakSamplingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeWriter : IDefaultEndpointWriter
    {
        public string? DefaultRender { get; set; }
        public bool Fail { get; set; }
        public string? GetDefaultEndpointId(AudioEndpointFlow flow, AudioEndpointRole role) => flow == AudioEndpointFlow.Render ? DefaultRender : null;
        public EndpointSwitchResult SetDefaultEndpoint(string endpointId, AudioEndpointFlow flow)
        {
            var roles = Enum.GetValues<AudioEndpointRole>().Select(role => SetDefaultEndpointRole(endpointId, flow, role)).ToArray();
            return new(endpointId, flow, roles);
        }
        public EndpointRoleChangeResult SetDefaultEndpointRole(string endpointId, AudioEndpointFlow flow, AudioEndpointRole role)
        {
            if (!Fail && flow == AudioEndpointFlow.Render) DefaultRender = endpointId;
            var actual = flow == AudioEndpointFlow.Render ? DefaultRender : null;
            return new(endpointId, flow, role, !Fail && actual == endpointId, actual, Fail ? "拒絕切換" : null);
        }
        public void Dispose() { }
    }
}
