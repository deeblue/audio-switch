using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

/// <summary>The tray panel: the endpoints of one flow grouped under their port or category, switching the default and playing a test tone.</summary>
public sealed class QuickSwitchPanelViewModel : INotifyPropertyChanged
{
    private readonly IDefaultEndpointWriter _writer;
    private readonly IAudioTopologySnapshotSource _snapshotSource;
    private readonly IAudioProbe _probe;
    private readonly PortTable _portTable;
    private AudioEndpointFlow _flow;
    private string? _errorMessage;
    private bool _hasUnconfiguredUsbEndpoints;

    public ObservableCollection<EndpointGroupViewModel> Groups { get; } = [];
    public AudioEndpointFlow Flow => _flow;
    public bool HasUnconfiguredUsbEndpoints { get => _hasUnconfiguredUsbEndpoints; private set => Set(ref _hasUnconfiguredUsbEndpoints, value); }
    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    public QuickSwitchPanelViewModel(IAudioTopologySnapshotSource snapshotSource, PortTable portTable,
        IDefaultEndpointWriter writer, IAudioProbe probe)
    {
        _snapshotSource = snapshotSource;
        _portTable = portTable;
        _writer = writer;
        _probe = probe;
    }

    public void SelectFlow(AudioEndpointFlow flow)
    {
        _flow = flow;
        Refresh();
    }

    public void Refresh()
    {
        try
        {
            var snapshot = _snapshotSource.Capture();
            Refresh(TopologyBuilder.Build(snapshot, _portTable), _flow);
            if (snapshot.Errors.Count > 0)
                ReportError(new InvalidOperationException(string.Join(Loc.T("；"), snapshot.Errors.SelectMany(error => error.Values))));
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    public void Refresh(TopologyTree tree, AudioEndpointFlow flow)
    {
        _flow = flow;
        ErrorMessage = null;
        Groups.Clear();
        HasUnconfiguredUsbEndpoints = false;
        var defaultId = _writer.GetDefaultEndpointId(flow, AudioEndpointRole.Multimedia);
        var endpointFlows = tree.Endpoints.ToDictionary(endpoint => endpoint.EndpointId, endpoint => endpoint.Flow, StringComparer.OrdinalIgnoreCase);
        foreach (var root in tree.Roots)
        {
            var rows = new List<EndpointViewModel>();
            Visit(root.Children, rows, defaultId, flow, endpointFlows);
            if (rows.Count > 0)
            {
                Groups.Add(new EndpointGroupViewModel(root.Name, rows));
                if (root.Id == TopologyBuilder.UnconfiguredUsbRootId) HasUnconfiguredUsbEndpoints = true;
            }
        }
    }

    public void SelectEndpoint(string endpointId)
    {
        ErrorMessage = null;
        try
        {
            var result = _writer.SetDefaultEndpoint(endpointId, _flow);
            var readback = _writer.GetDefaultEndpointId(_flow, AudioEndpointRole.Multimedia);
            if (!result.Success || !string.Equals(readback, endpointId, StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = string.Join(" ", result.Roles.Where(role => !role.Success).Select(role => role.Error).Where(message => !string.IsNullOrWhiteSpace(message)));
                if (string.IsNullOrWhiteSpace(ErrorMessage)) ErrorMessage = Loc.T("Windows 未確認切換結果，請重試。");
            }
            foreach (var endpoint in Groups.SelectMany(group => group.Endpoints))
                endpoint.IsDefault = string.Equals(endpoint.EndpointId, readback, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task<bool> PlayToneAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        try
        {
            await _probe.PlayTestToneAsync(endpointId, TimeSpan.FromSeconds(2), cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            ReportError(exception);
            return false;
        }
    }

    public void ReportError(Exception exception) => ErrorMessage = exception.Message;

    public bool TryMarkFirstRunNotice(AudioSwitchSettings settings)
    {
        if (settings.FirstRunNotificationShown) return false;
        settings.FirstRunNotificationShown = true;
        return true;
    }

    private static void Visit(IEnumerable<TopologyNode> nodes, ICollection<EndpointViewModel> endpoints, string? defaultId,
        AudioEndpointFlow flow, IReadOnlyDictionary<string, string> endpointFlows)
    {
        foreach (var node in nodes)
        {
            if (node.EndpointId is not null && endpointFlows.TryGetValue(node.EndpointId, out var endpointFlow) &&
                string.Equals(endpointFlow, flow == AudioEndpointFlow.Render ? "render" : "capture", StringComparison.OrdinalIgnoreCase))
                endpoints.Add(new EndpointViewModel(node.EndpointId, node.Name, flow, string.Equals(node.EndpointId, defaultId, StringComparison.OrdinalIgnoreCase)));
            Visit(node.Children, endpoints, defaultId, flow, endpointFlows);
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>A port or category heading with its endpoints.</summary>
public sealed class EndpointGroupViewModel(string name, IEnumerable<EndpointViewModel> endpoints)
{
    public string Name { get; } = name;
    public IReadOnlyList<EndpointViewModel> Endpoints { get; } = endpoints.ToArray();
}

/// <summary>One endpoint in the panel.</summary>
public sealed class EndpointViewModel(string endpointId, string name, AudioEndpointFlow flow, bool isDefault) : INotifyPropertyChanged
{
    private bool _isDefault = isDefault;
    public string EndpointId { get; } = endpointId;
    public string Name { get; } = name;
    public AudioEndpointFlow Flow { get; } = flow;
    public bool IsDefault
    {
        get => _isDefault;
        internal set { if (_isDefault == value) return; _isDefault = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDefault))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
