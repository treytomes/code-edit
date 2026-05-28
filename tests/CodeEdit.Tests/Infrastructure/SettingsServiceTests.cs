using CodeEdit.Domain;
using CodeEdit.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeEdit.Tests.Infrastructure;

public sealed class SettingsServiceTests : IDisposable
{
    // Route the service to a temp dir so tests are isolated from real user settings.
    private readonly string        _tempDir;
    private readonly SettingsService _svc;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _svc = new SettingsService(NullLogger<SettingsService>.Instance, _tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string SettingsPath => Path.Combine(_tempDir, "settings.json");

    // ── Missing file ───────────────────────────────────────────────────────

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var s = _svc.Load();
        Assert.Equal(EditorSettings.Default.TabWidth,     s.TabWidth);
        Assert.Equal(EditorSettings.Default.InsertSpaces, s.InsertSpaces);
    }

    // ── Valid JSON ─────────────────────────────────────────────────────────

    [Fact]
    public void Load_ValidJson_ReturnsSpecifiedValues()
    {
        File.WriteAllText(SettingsPath, """{"tabWidth": 2, "insertSpaces": false}""");
        var s = _svc.Load();
        Assert.Equal(2, s.TabWidth);
        Assert.False(s.InsertSpaces);
    }

    [Fact]
    public void Load_OnlyTabWidth_InsertSpacesIsDefault()
    {
        File.WriteAllText(SettingsPath, """{"tabWidth": 8}""");
        var s = _svc.Load();
        Assert.Equal(8, s.TabWidth);
        Assert.Equal(EditorSettings.Default.InsertSpaces, s.InsertSpaces);
    }

    [Fact]
    public void Load_OnlyInsertSpaces_TabWidthIsDefault()
    {
        File.WriteAllText(SettingsPath, """{"insertSpaces": false}""");
        var s = _svc.Load();
        Assert.Equal(EditorSettings.Default.TabWidth, s.TabWidth);
        Assert.False(s.InsertSpaces);
    }

    [Fact]
    public void Load_AllowsTrailingCommasAndComments()
    {
        File.WriteAllText(SettingsPath, """
            {
                // tab width
                "tabWidth": 3,
                "insertSpaces": true,
            }
            """);
        var s = _svc.Load();
        Assert.Equal(3, s.TabWidth);
        Assert.True(s.InsertSpaces);
    }

    [Fact]
    public void Load_RecentFilesMax_IsRead()
    {
        File.WriteAllText(SettingsPath, """{"recentFilesMax": 5}""");
        var s = _svc.Load();
        Assert.Equal(5, s.RecentFilesMax);
    }

    [Fact]
    public void Load_RecentFilesMaxZero_FallsBackToDefault()
    {
        File.WriteAllText(SettingsPath, """{"recentFilesMax": 0}""");
        var s = _svc.Load();
        Assert.Equal(EditorSettings.Default.RecentFilesMax, s.RecentFilesMax);
    }

    // ── Invalid / edge cases ───────────────────────────────────────────────

    [Fact]
    public void Load_TabWidthZero_FallsBackToDefault()
    {
        File.WriteAllText(SettingsPath, """{"tabWidth": 0}""");
        var s = _svc.Load();
        Assert.Equal(EditorSettings.Default.TabWidth, s.TabWidth);
    }

    [Fact]
    public void Load_TabWidthNegative_FallsBackToDefault()
    {
        File.WriteAllText(SettingsPath, """{"tabWidth": -1}""");
        var s = _svc.Load();
        Assert.Equal(EditorSettings.Default.TabWidth, s.TabWidth);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaults()
    {
        File.WriteAllText(SettingsPath, "not json at all");
        var s = _svc.Load();
        Assert.Equal(EditorSettings.Default.TabWidth,     s.TabWidth);
        Assert.Equal(EditorSettings.Default.InsertSpaces, s.InsertSpaces);
    }

    [Fact]
    public void Load_EmptyObject_ReturnsDefaults()
    {
        File.WriteAllText(SettingsPath, "{}");
        var s = _svc.Load();
        Assert.Equal(EditorSettings.Default.TabWidth,     s.TabWidth);
        Assert.Equal(EditorSettings.Default.InsertSpaces, s.InsertSpaces);
    }
}
