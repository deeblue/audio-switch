using System.Windows.Markup;
using AudioSwitch.Core;

namespace AudioSwitch.App;

/// <summary>
/// Interface text in XAML, written in Traditional Chinese and shown in the current language: <c>Text="{app:Loc '設定'}"</c>.
/// Windows are rebuilt when the language changes, so the text is resolved once when the XAML loads.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string text) : MarkupExtension
{
    [ConstructorArgument("text")]
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
