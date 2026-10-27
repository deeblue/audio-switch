namespace AudioSwitch.Core;

/// <summary>settings.json: shortcuts, start with Windows, interface language, the first-run notice and dock names.</summary>
public sealed class AudioSwitchSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? PlaybackHotkey { get; set; }
    public string? RecordingHotkey { get; set; }
    public bool AutostartEnabled { get; set; }
    public bool FirstRunNotificationShown { get; set; }

    /// <summary><see cref="Loc.TraditionalChineseSetting"/>, <see cref="Loc.EnglishSetting"/>, or <c>null</c> to follow Windows.</summary>
    public string? Language { get; set; }

    /// <summary>User-given dock and hub names, keyed by <see cref="TopologyNode.DockKey"/>; docks of the same model share one name.</summary>
    public Dictionary<string, string> DockNames { get; set => field = value ?? []; } = [];
}
