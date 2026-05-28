using CodeEdit.Domain;

namespace CodeEdit.Tests.Domain;

public sealed class EditorSettingsTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var s = EditorSettings.Default;
        Assert.Equal(4, s.TabWidth);
        Assert.True(s.InsertSpaces);
        Assert.Equal(10, s.RecentFilesMax);
    }

    [Theory]
    [InlineData(4, "    ")]
    [InlineData(2, "  ")]
    [InlineData(1, " ")]
    public void IndentString_InsertSpaces_ReturnsCorrectSpaces(int tabWidth, string expected)
    {
        var s = new EditorSettings(tabWidth, InsertSpaces: true, RecentFilesMax: 10);
        Assert.Equal(expected, s.IndentString);
    }

    [Fact]
    public void IndentString_TabChar_ReturnsTab()
    {
        var s = new EditorSettings(TabWidth: 4, InsertSpaces: false, RecentFilesMax: 10);
        Assert.Equal("\t", s.IndentString);
    }
}
