using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

public sealed class DeviceSettingsViewModel : INotifyPropertyChanged
{
    private readonly IAudioProbe _probe;
    private readonly IEndpointNameWriter _nameWriter;
    private readonly IAudioTopologySnapshotSource _snapshotSource;
    private readonly PortTable _portTable;
    private readonly AliasStore _aliases;
    private readonly Action _save;
    private TopologyEndpoint? _selectedEndpoint;
    private string _aliasText = string.Empty;
    private string? _listeningEndpointId;
    private float _peakLevel;
    private string? _errorMessage;

    public TopologyEndpoint? SelectedEndpoint { get => _selectedEndpoint; private set => Set(ref _selectedEndpoint, value); }
    public string? SelectedEndpointId => SelectedEndpoint?.EndpointId;
    public string AliasText { get => _aliasText; set => Set(ref _aliasText, value); }
    public bool IsListening => _listeningEndpointId is not null;
    public string? ListeningEndpointId => _listeningEndpointId;
    public float PeakLevel { get => _peakLevel; private set => Set(ref _peakLevel, value); }
    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public AliasStore Aliases => _aliases;
    public event PropertyChangedEventHandler? PropertyChanged;

    public DeviceSettingsViewModel(IAudioProbe probe, IEndpointNameWriter nameWriter,
        IAudioTopologySnapshotSource snapshotSource, PortTable portTable, AliasStore aliases, Action save)
    {
        _probe = probe;
        _nameWriter = nameWriter;
        _snapshotSource = snapshotSource;
        _portTable = portTable;
        _aliases = aliases;
        _save = save;
    }

    public async Task SelectEndpointAsync(TopologyEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!string.Equals(SelectedEndpointId, endpoint.EndpointId, StringComparison.OrdinalIgnoreCase))
            await StopListeningAsync().ConfigureAwait(true);
        SelectedEndpoint = endpoint;
        try
        {
            AliasText = _nameWriter.ReadDeviceDescription(endpoint.EndpointId);
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            AliasText = string.Empty;
            ErrorMessage = exception.Message;
        }
    }

    public bool ApplyAlias(IEnumerable<TopologyEndpoint> endpoints)
    {
        return ApplyAlias(SelectedEndpoint, endpoints, AliasText);
    }

    public bool ApplyAlias(TopologyEndpoint? selectedEndpoint, IEnumerable<TopologyEndpoint> endpoints, string aliasText)
    {
        if (selectedEndpoint is null) return Fail("請先選擇一個音訊端點。");
        if (string.IsNullOrWhiteSpace(aliasText)) return Fail("別名不可空白；請使用清除別名。");
        var members = AliasEndpointGrouping.GetMembers(selectedEndpoint, endpoints);
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var endpointId in members) current[endpointId] = _nameWriter.ReadDeviceDescription(endpointId);
            if (members.All(endpointId => string.Equals(current[endpointId], aliasText, StringComparison.Ordinal)))
            {
                ErrorMessage = null;
                return true;
            }

            var originals = members.ToDictionary(endpointId => endpointId,
                endpointId => _aliases.OriginalDescriptions.GetValueOrDefault(endpointId) ?? current[endpointId],
                StringComparer.OrdinalIgnoreCase);
            var changed = new List<string>();
            try
            {
                foreach (var endpointId in members)
                {
                    if (string.Equals(current[endpointId], aliasText, StringComparison.Ordinal)) continue;
                    _nameWriter.WriteDeviceDescription(endpointId, aliasText);
                    changed.Add(endpointId);
                }
            }
            catch (Exception writeError)
            {
                var rollbackErrors = RestoreInReverse(changed, current);
                return Fail(ComposeFailure("寫入別名失敗。", writeError, rollbackErrors));
            }

            _aliases.SetAlias(members, originals);
            _save();
            if (selectedEndpoint.EndpointId.Equals(SelectedEndpointId, StringComparison.OrdinalIgnoreCase)) AliasText = aliasText;
            ErrorMessage = null;
            return true;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    public bool ClearAlias(IEnumerable<TopologyEndpoint> endpoints)
    {
        if (SelectedEndpoint is null) return Fail("請先選擇一個音訊端點。");
        var members = AliasEndpointGrouping.GetMembers(SelectedEndpoint, endpoints)
            .Where(endpointId => _aliases.OriginalDescriptions.ContainsKey(endpointId)).ToArray();
        if (members.Length == 0)
        {
            ErrorMessage = null;
            return true;
        }

        try
        {
            var current = members.ToDictionary(endpointId => endpointId,
                endpointId => _nameWriter.ReadDeviceDescription(endpointId), StringComparer.OrdinalIgnoreCase);
            var originals = members.ToDictionary(endpointId => endpointId,
                endpointId => _aliases.OriginalDescriptions[endpointId], StringComparer.OrdinalIgnoreCase);
            var changed = new List<string>();
            try
            {
                foreach (var endpointId in members)
                {
                    _nameWriter.WriteDeviceDescription(endpointId, originals[endpointId]);
                    changed.Add(endpointId);
                }
            }
            catch (Exception writeError)
            {
                var rollbackErrors = RestoreInReverse(changed, current);
                return Fail(ComposeFailure("還原端點名稱失敗。", writeError, rollbackErrors));
            }

            _aliases.ClearAlias(members);
            _save();
            if (SelectedEndpointId is not null && originals.TryGetValue(SelectedEndpointId, out var restoredDescription)) AliasText = restoredDescription;
            ErrorMessage = null;
            return true;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    public CalibrationResult? CalibrateSelectedDevice(string interfaceId)
    {
        if (SelectedEndpoint is null)
        {
            ErrorMessage = "請先選擇一個音訊端點。";
            return null;
        }

        try
        {
            var result = _portTable.CalibrateExistingEndpoint(interfaceId, _snapshotSource.Capture(), SelectedEndpoint.EndpointId);
            if (result.Status == "calibrated")
            {
                _save();
                ErrorMessage = null;
            }
            else if (result.Status == "ambiguous") ErrorMessage = "此接口已有相同傳輸類型的另一條路徑，請選擇其他接口。";
            else ErrorMessage = "目前裝置沒有可校準的連接路徑。";
            return result;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return null;
        }
    }

    public async Task<bool> ToggleListeningAsync(string endpointId)
    {
        if (string.Equals(_listeningEndpointId, endpointId, StringComparison.OrdinalIgnoreCase))
        {
            await StopListeningAsync().ConfigureAwait(true);
            return false;
        }

        await StopListeningAsync().ConfigureAwait(true);
        try
        {
            _probe.StartPeakSampling(endpointId);
            _listeningEndpointId = endpointId;
            PeakLevel = 0;
            ErrorMessage = null;
            OnPropertyChanged(nameof(ListeningEndpointId));
            OnPropertyChanged(nameof(IsListening));
            return true;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    public void RefreshPeak()
    {
        if (_listeningEndpointId is null) return;
        try
        {
            PeakLevel = _probe.ReadPeakLevel(_listeningEndpointId);
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            _ = StopListeningAsync();
        }
    }

    public Task SetDevicePageActiveAsync(bool active) => active ? Task.CompletedTask : StopListeningAsync();

    public Task CloseAsync() => StopListeningAsync();

    private async Task StopListeningAsync()
    {
        if (_listeningEndpointId is null) return;
        _listeningEndpointId = null;
        OnPropertyChanged(nameof(ListeningEndpointId));
        OnPropertyChanged(nameof(IsListening));
        try
        {
            await _probe.StopPeakSamplingAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private IReadOnlyList<Exception> RestoreInReverse(IEnumerable<string> endpointIds, IReadOnlyDictionary<string, string> values)
    {
        var errors = new List<Exception>();
        foreach (var endpointId in endpointIds.Reverse())
        {
            try { _nameWriter.WriteDeviceDescription(endpointId, values[endpointId]); }
            catch (Exception exception) { errors.Add(exception); }
        }
        return errors;
    }

    private static string ComposeFailure(string message, Exception exception, IReadOnlyList<Exception> rollbackErrors) =>
        rollbackErrors.Count == 0
            ? $"{message} {exception.Message}"
            : $"{message} {exception.Message} 還原失敗：{string.Join("；", rollbackErrors.Select(error => error.Message))}";

    private bool Fail(string message)
    {
        ErrorMessage = message;
        return false;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
