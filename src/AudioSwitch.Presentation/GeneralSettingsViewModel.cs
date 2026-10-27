using System.ComponentModel;
using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

/// <summary>A choice in the language list; Setting is what is stored, null follows Windows.</summary>
public sealed record LanguageOption(string? Setting, string Label);

/// <summary>The General page: starting with Windows and the interface language.</summary>
public sealed class GeneralSettingsViewModel : INotifyPropertyChanged
{
    private readonly IAutostartService _autostartService;
    private readonly AudioSwitchSettings _settings;
    private string? _errorMessage;

    /// <summary>Following Windows is listed first; the two languages are always shown in their own script.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(null, Loc.T("跟隨 Windows")),
        new(Loc.TraditionalChineseSetting, "繁體中文"),
        new(Loc.EnglishSetting, "English")
    ];

    public LanguageOption SelectedLanguage
    {
        get => LanguageOptions.FirstOrDefault(option => option.Setting == _settings.Language) ?? LanguageOptions[0];
        set
        {
            if (value is null || value.Setting == _settings.Language) return;
            _settings.Language = value.Setting;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLanguage)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised after the stored language setting changes; the app then switches the interface language.</summary>
    public event EventHandler? LanguageChanged;

    public bool AutostartEnabled
    {
        get => _settings.AutostartEnabled;
        set
        {
            try
            {
                _autostartService.SetEnabled(value);
                _settings.AutostartEnabled = value;
                ErrorMessage = null;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutostartEnabled)));
            }
            catch (Exception exception) { ErrorMessage = exception.Message; }
        }
    }

    public string? ErrorMessage { get => _errorMessage; private set { if (_errorMessage == value) return; _errorMessage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage))); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public GeneralSettingsViewModel(IAutostartService autostartService, AudioSwitchSettings settings)
    {
        _autostartService = autostartService;
        _settings = settings;
    }
}
