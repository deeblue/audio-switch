using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AudioSwitch.Core;

namespace AudioSwitch.Core.Tests;

public sealed partial class LocalizationTests
{
    private static readonly string SourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));

    [Fact]
    public void EveryChineseStringInTheProductHasAnEnglishTranslation()
    {
        var problems = new List<string>();
        Assert.True(ChineseStrings().Count() > 200, "The source scan found too few strings; check SourceRoot.");
        foreach (var (file, text, interpolated) in ChineseStrings())
        {
            if (interpolated) problems.Add($"{file}: interpolated Chinese text, use Loc.F: {text}");
            else if (!EnglishText.Table.ContainsKey(text)) problems.Add($"{file}: no English text for \"{text}\"");
        }
        foreach (var file in SourceFiles("*.xaml"))
        {
            var rest = XmlComment().Replace(XamlLoc().Replace(File.ReadAllText(file), string.Empty), string.Empty);
            if (Chinese().IsMatch(rest)) problems.Add($"{Path.GetFileName(file)}: Chinese text outside {{app:Loc '…'}}");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryTranslationIsUsedAndKeepsItsPlaceholders()
    {
        var used = ChineseStrings().Select(item => item.Text).ToHashSet(StringComparer.Ordinal);
        var problems = EnglishText.Table
            .Where(pair => !used.Contains(pair.Key))
            .Select(pair => $"unused: {pair.Key}")
            .Concat(EnglishText.Table
                .Where(pair => !Placeholders(pair.Key).SetEquals(Placeholders(pair.Value)))
                .Select(pair => $"placeholders differ: {pair.Key} -> {pair.Value}"))
            .ToArray();

        Assert.True(problems.Length == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TextFollowsTheChosenLanguage()
    {
        using (Loc.Use(AppLanguage.English))
        {
            Assert.Equal("Settings", Loc.T("設定"));
            Assert.Equal("Deleted “Left USB-C”.", Loc.F("已刪除「{0}」。", "Left USB-C"));
            Assert.Equal("USB 2, USB 3", Loc.Join(["USB 2", "USB 3"]));
            Assert.Equal("Speakers (speaker)", Loc.T("Speakers (speaker)"));
        }
        using (Loc.Use(AppLanguage.TraditionalChinese))
        {
            Assert.Equal("設定", Loc.T("設定"));
            Assert.Equal("USB 2、USB 3", Loc.Join(["USB 2", "USB 3"]));
        }
    }

    [Theory]
    [InlineData(null, "zh-TW", AppLanguage.TraditionalChinese)]
    [InlineData(null, "zh-CN", AppLanguage.TraditionalChinese)]
    [InlineData(null, "en-US", AppLanguage.English)]
    [InlineData(null, "ja-JP", AppLanguage.English)]
    [InlineData("en", "zh-TW", AppLanguage.English)]
    [InlineData("zh-Hant", "de-DE", AppLanguage.TraditionalChinese)]
    public void LanguageFollowsWindowsUnlessOneIsChosen(string? setting, string windowsLanguage, AppLanguage expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, CultureInfo.GetCultureInfo(windowsLanguage)));

    private static IEnumerable<string> SourceFiles(string pattern) => Directory.EnumerateFiles(SourceRoot, pattern, SearchOption.AllDirectories)
        .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
        .Where(file => !file.EndsWith("EnglishText.cs", StringComparison.Ordinal));

    private static IEnumerable<(string File, string Text, bool Interpolated)> ChineseStrings()
    {
        foreach (var file in SourceFiles("*.cs"))
        {
            foreach (var (text, interpolated) in StringLiterals(File.ReadAllText(file)).Where(item => Chinese().IsMatch(item.Text)))
                yield return (Path.GetFileName(file), text, interpolated);
        }
        foreach (var file in SourceFiles("*.xaml"))
        {
            foreach (Match match in XamlLoc().Matches(File.ReadAllText(file)))
                yield return (Path.GetFileName(file), match.Groups[1].Value, false);
        }
    }

    /// <summary>String literals of a C# file, skipping comments and character literals; raw text without unescaping.</summary>
    private static IEnumerable<(string Text, bool Interpolated)> StringLiterals(string code)
    {
        var index = 0;
        while (index < code.Length)
        {
            if (code.AsSpan(index).StartsWith("//"))
            {
                var end = code.IndexOf('\n', index);
                index = end < 0 ? code.Length : end;
            }
            else if (code.AsSpan(index).StartsWith("/*"))
            {
                index = code.IndexOf("*/", index, StringComparison.Ordinal) + 2;
            }
            else if (code[index] == '\'')
            {
                index++;
                while (code[index] != '\'') index += code[index] == '\\' ? 2 : 1;
                index++;
            }
            else if (code[index] is '$' or '@' or '"')
            {
                var start = index;
                while (code[index] is '$' or '@') index++;
                if (code[index] != '"')
                {
                    index = start + 1;
                    continue;
                }
                var prefix = code[start..index];
                if (code.AsSpan(index).StartsWith("\"\"\""))
                {
                    var end = code.IndexOf("\"\"\"", index + 3, StringComparison.Ordinal);
                    yield return (code[(index + 3)..end], prefix.Contains('$'));
                    index = end + 3;
                    continue;
                }
                var verbatim = prefix.Contains('@');
                var text = new StringBuilder();
                index++;
                while (true)
                {
                    if (verbatim && code.AsSpan(index).StartsWith("\"\""))
                    {
                        text.Append("\"\"");
                        index += 2;
                    }
                    else if (!verbatim && code[index] == '\\')
                    {
                        text.Append(code, index, 2);
                        index += 2;
                    }
                    else if (code[index] == '"')
                    {
                        break;
                    }
                    else
                    {
                        text.Append(code[index++]);
                    }
                }
                index++;
                yield return (text.ToString(), prefix.Contains('$'));
            }
            else
            {
                index++;
            }
        }
    }

    private static HashSet<string> Placeholders(string text) => Placeholder().Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"[　-〿一-鿿＀-￯]")]
    private static partial Regex Chinese();

    [GeneratedRegex(@"\{app:Loc '([^']*)'\}")]
    private static partial Regex XamlLoc();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
