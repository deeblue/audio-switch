namespace AudioSwitch.App.Tests;

public sealed class AppVersionTests
{
    [Fact]
    public void BuildCarriesVersionCommitAndBuildTime()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Version);
        Assert.Matches(@"^[0-9a-f]{7,}( 含未提交修改)?，建置於 \d{4}-\d{2}-\d{2} \d{2}:\d{2}$", AppVersion.BuildText);
        Assert.StartsWith($"版本 {AppVersion.Version}（", AppVersion.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTextMarksLocalChangesAndSkipsMissingParts()
    {
        Assert.Equal("abc1234 含未提交修改，建置於 2026-10-27 10:00", AppVersion.FormatBuild("abc1234", true, "2026-10-27 10:00"));
        Assert.Equal("abc1234", AppVersion.FormatBuild("abc1234", false, null));
        Assert.Equal(string.Empty, AppVersion.FormatBuild(null, true, " "));
    }
}
