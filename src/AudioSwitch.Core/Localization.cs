using System.Globalization;

namespace AudioSwitch.Core;

/// <summary>The interface languages.</summary>
public enum AppLanguage
{
    TraditionalChinese,
    English
}

/// <summary>
/// Interface text is written in Traditional Chinese in the source; <see cref="EnglishText"/> maps each text to English.
/// Every Chinese string literal in the product code must have an entry there (a test scans the sources).
/// </summary>
public static class Loc
{
    /// <summary>Values stored in settings.json; <c>null</c> follows the Windows display language.</summary>
    public const string TraditionalChineseSetting = "zh-Hant";
    public const string EnglishSetting = "en";

    private static readonly AsyncLocal<AppLanguage?> ScopedLanguage = new();
    private static AppLanguage _language = AppLanguage.TraditionalChinese;

    public static AppLanguage Current => ScopedLanguage.Value ?? _language;

    /// <summary>Raised after <see cref="SetLanguage"/> changes the process-wide language.</summary>
    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(AppLanguage language)
    {
        if (_language == language) return;
        _language = language;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Uses <paramref name="language"/> for the current call flow only, for tests and for comparing names across languages.</summary>
    public static IDisposable Use(AppLanguage language)
    {
        var previous = ScopedLanguage.Value;
        ScopedLanguage.Value = language;
        return new Scope(() => ScopedLanguage.Value = previous);
    }

    /// <summary>The language for a stored setting: Chinese Windows gets Traditional Chinese, any other Windows language gets English.</summary>
    public static AppLanguage Resolve(string? setting, CultureInfo windowsLanguage) => setting switch
    {
        TraditionalChineseSetting => AppLanguage.TraditionalChinese,
        EnglishSetting => AppLanguage.English,
        _ => windowsLanguage.TwoLetterISOLanguageName == "zh" ? AppLanguage.TraditionalChinese : AppLanguage.English
    };

    public static string T(string text) =>
        Current == AppLanguage.English && EnglishText.Table.TryGetValue(text, out var english) ? english : text;

    /// <summary>Translates a composite format string such as <c>"已刪除「{0}」。"</c> and fills it in.</summary>
    public static string F(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(format), args);

    /// <summary>Joins list items with the separator of the current language.</summary>
    public static string Join(IEnumerable<string> items) => string.Join(Current == AppLanguage.English ? ", " : "、", items);

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
