namespace AudioSwitch.Presentation;

public enum PanelToggleAction
{
    Ignore,
    Open,
    Close
}

/// <summary>
/// Decides what a tray click does. A click that closed the panel by taking its focus does not reopen it, and clicks that
/// follow each other faster than <paramref name="clickDebounce"/> are ignored: only the first click of a burst acts, and
/// every further click in the burst restarts the wait.
/// </summary>
public sealed class PanelToggleBehavior(TimeSpan suppressionWindow, TimeSpan clickDebounce)
{
    private DateTimeOffset? _lastFocusLoss;
    private DateTimeOffset? _lastClick;

    public PanelToggleBehavior(TimeSpan suppressionWindow) : this(suppressionWindow, TimeSpan.Zero) { }

    public void RecordFocusLoss(DateTimeOffset timestamp) => _lastFocusLoss = timestamp;

    public PanelToggleAction OnTrayClick(bool panelIsOpen, DateTimeOffset timestamp)
    {
        var inBurst = _lastClick is { } previous && timestamp >= previous && timestamp - previous < clickDebounce;
        _lastClick = timestamp;
        if (inBurst) return PanelToggleAction.Ignore;

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
