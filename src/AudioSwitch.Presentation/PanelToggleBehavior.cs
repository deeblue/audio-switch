namespace AudioSwitch.Presentation;

public enum PanelToggleAction
{
    Ignore,
    Open,
    Close
}

public sealed class PanelToggleBehavior(TimeSpan suppressionWindow)
{
    private DateTimeOffset? _lastFocusLoss;

    public void RecordFocusLoss(DateTimeOffset timestamp) => _lastFocusLoss = timestamp;

    public PanelToggleAction OnTrayClick(bool panelIsOpen, DateTimeOffset timestamp)
    {
        if (panelIsOpen)
        {
            _lastFocusLoss = null;
            return PanelToggleAction.Close;
        }

        var recentlyClosed = _lastFocusLoss is { } closedAt && timestamp >= closedAt && timestamp - closedAt <= suppressionWindow;
        _lastFocusLoss = null;
        return recentlyClosed ? PanelToggleAction.Ignore : PanelToggleAction.Open;
    }
}
