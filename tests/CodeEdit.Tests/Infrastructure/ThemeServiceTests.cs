using CodeEdit.Domain;
using CodeEdit.Infrastructure.Settings;
using CodeEdit.Infrastructure.Theme;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeEdit.Tests.Infrastructure;

public sealed class ThemeServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private ThemeService Svc() => new(NullLogger<ThemeService>.Instance, _dir);

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void LoadAll_SeedsBuiltinThemeWhenDirectoryIsEmpty()
    {
        var themes = Svc().LoadAll();
        Assert.Single(themes);
        Assert.Equal("VS Code Dark+", themes[0].Name);
    }

    [Fact]
    public void Save_LoadByName_RoundTripsAllColorRolesAndName()
    {
        var svc   = Svc();
        var theme = UserTheme.FromDefaults("My Theme");
        theme.Normal = ColorPair.Of(0x11, 0x22, 0x33, 0x44, 0x55, 0x66);

        svc.Save(theme);
        var loaded = svc.LoadByName("My Theme");

        Assert.NotNull(loaded);
        Assert.Equal("My Theme", loaded!.Name);
        Assert.Equal(new Rgb(0x11, 0x22, 0x33), loaded.Normal.Foreground);
        Assert.Equal(new Rgb(0x44, 0x55, 0x66), loaded.Normal.Background);
        Assert.Equal(theme.Selection,   loaded.Selection);
        Assert.Equal(theme.LineNumber,  loaded.LineNumber);
        Assert.Equal(theme.StatusBar,   loaded.StatusBar);
        Assert.Equal(theme.MenuBar,     loaded.MenuBar);
        Assert.Equal(theme.TabBar,      loaded.TabBar);
        Assert.Equal(theme.FileTree,    loaded.FileTree);
        Assert.Equal(theme.Dialog,      loaded.Dialog);
        Assert.Equal(theme.SearchMatch, loaded.SearchMatch);
    }

    [Fact]
    public void Delete_RemovesFile_SubsequentLoadByNameReturnsNull()
    {
        var svc = Svc();
        svc.Save(UserTheme.FromDefaults("Temp"));
        Assert.NotNull(svc.LoadByName("Temp"));
        svc.Delete("Temp");
        Assert.Null(svc.LoadByName("Temp"));
    }

    [Fact]
    public void Export_WritesValidThemeFileToGivenPath()
    {
        var svc  = Svc();
        var dest = Path.Combine(_dir, "export.json");
        svc.Export(UserTheme.FromDefaults("Exported"), dest);
        Assert.True(File.Exists(dest));
        var text = File.ReadAllText(dest);
        Assert.Contains("Exported", text);
        Assert.Contains("#", text);
    }

    [Fact]
    public void Import_LoadsValidFileAndAddsItToThemesDirectory()
    {
        var svc    = Svc();
        var srcDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(srcDir);
        var src = Path.Combine(srcDir, "incoming.json");

        // Write a valid theme to a separate location
        svc.Export(UserTheme.FromDefaults("Incoming"), src);

        var imported = svc.Import(src);
        Assert.Equal("Incoming", imported.Name);
        Assert.NotNull(svc.LoadByName("Incoming"));

        Directory.Delete(srcDir, recursive: true);
    }

    [Fact]
    public void Import_ThrowsThemeImportException_OnMalformedJson()
    {
        var svc = Svc();
        Directory.CreateDirectory(_dir);
        var bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "{ not valid json }}}");
        Assert.Throws<ThemeImportException>(() => svc.Import(bad));
    }

    // ── Slugify ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("VS Code Dark+",  "vs-code-dark")]
    [InlineData("My Theme",       "my-theme")]
    [InlineData("My-Theme",       "my-theme")]
    [InlineData("hello world",    "hello-world")]
    [InlineData("Café Résumé",    "caf-rsum")]
    [InlineData("!@#$%",          "")]
    [InlineData("",               "")]
    [InlineData("a",              "a")]
    [InlineData("123",            "123")]
    public void Slugify_ProducesExpectedSlug(string name, string expected)
        => Assert.Equal(expected, ThemeService.Slugify(name));

    [Fact]
    public void Import_ThrowsThemeImportException_OnNameCollision()
    {
        var svc    = Svc();
        var srcDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(srcDir);
        var src = Path.Combine(srcDir, "dup.json");

        svc.Export(UserTheme.FromDefaults("Collision"), src);
        svc.Import(src);  // first import succeeds

        // Re-export with same name to a new file
        var src2 = Path.Combine(srcDir, "dup2.json");
        svc.Export(UserTheme.FromDefaults("Collision"), src2);
        Assert.Throws<ThemeImportException>(() => svc.Import(src2));

        Directory.Delete(srcDir, recursive: true);
    }
}
