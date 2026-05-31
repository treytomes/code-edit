using CodeEdit.Domain;
using CodeEdit.Infrastructure.Theme;

namespace CodeEdit.Tests.Infrastructure;

public sealed class UserThemeTests
{
    [Fact]
    public void FromDefaults_MatchesDefaultDarkThemeValuesForAllRoles()
    {
        var src   = new DefaultDarkTheme();
        var theme = UserTheme.FromDefaults();

        Assert.Equal(src.Normal,      theme.Normal);
        Assert.Equal(src.Selection,   theme.Selection);
        Assert.Equal(src.LineNumber,  theme.LineNumber);
        Assert.Equal(src.StatusBar,   theme.StatusBar);
        Assert.Equal(src.MenuBar,     theme.MenuBar);
        Assert.Equal(src.TabBar,      theme.TabBar);
        Assert.Equal(src.FileTree,    theme.FileTree);
        Assert.Equal(src.Dialog,      theme.Dialog);
        Assert.Equal(src.SearchMatch, theme.SearchMatch);

        foreach (TokenType tt in Enum.GetValues<TokenType>())
            if (tt != TokenType.Default)
                Assert.Equal(src.ForToken(tt), theme.ForToken(tt));
    }

    [Fact]
    public void Clone_ProducesDeepCopy_ModifyingCloneDoesNotAffectOriginal()
    {
        var original = UserTheme.FromDefaults("Original");
        var clone    = original.Clone();

        clone.Normal = ColorPair.Of(0x01, 0x02, 0x03, 0x04, 0x05, 0x06);

        Assert.NotEqual(original.Normal, clone.Normal);
    }

    [Fact]
    public void Clone_WithNewName_SetsCloneNameWithoutAffectingOriginal()
    {
        var original = UserTheme.FromDefaults("Original");
        var clone    = original.Clone("Renamed");

        Assert.Equal("Original", original.Name);
        Assert.Equal("Renamed",  clone.Name);
    }

    [Fact]
    public void ForToken_FallsBackToNormal_ForUnrecognisedTokenType()
    {
        var theme = UserTheme.FromDefaults();
        // TokenType.Default is the unrecognised sentinel
        Assert.Equal(theme.Normal, theme.ForToken(TokenType.Default));
    }

    [Fact]
    public void CopyConstructor_CopiesAllFieldsIncludingName()
    {
        var src   = new DefaultDarkTheme();
        var theme = new UserTheme(src, "Copied");

        Assert.Equal("Copied",       theme.Name);
        Assert.Equal(src.Normal,     theme.Normal);
        Assert.Equal(src.Selection,  theme.Selection);
        Assert.Equal(src.StatusBar,  theme.StatusBar);
    }
}
