using CodeEdit.Domain;
using CodeEdit.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeEdit.Tests.Infrastructure;

public sealed class RecentFilesServiceTests : IDisposable
{
    private readonly string             _tempDir;
    private readonly RecentFilesService _svc;

    public RecentFilesServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _svc = MakeSvc(recentFilesMax: 10);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private RecentFilesService MakeSvc(int recentFilesMax) =>
        new(NullLogger<RecentFilesService>.Instance,
            new EditorSettings(TabWidth: 4, InsertSpaces: true, RecentFilesMax: recentFilesMax),
            _tempDir);

    // ── Load ───────────────────────────────────────────────────────────────

    [Fact]
    public void Load_NoFile_ReturnsEmpty()
    {
        Assert.Empty(_svc.Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(_tempDir, "recent.json"), "not json");
        Assert.Empty(_svc.Load());
    }

    // ── Add ────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_SinglePath_IsFirstEntry()
    {
        _svc.Add("/home/user/foo.cs");
        var list = _svc.Load();
        Assert.Single(list);
        Assert.Equal("/home/user/foo.cs", list[0]);
    }

    [Fact]
    public void Add_SecondPath_PrependsMostRecent()
    {
        _svc.Add("/home/user/a.cs");
        _svc.Add("/home/user/b.cs");
        var list = _svc.Load();
        Assert.Equal("/home/user/b.cs", list[0]);
        Assert.Equal("/home/user/a.cs", list[1]);
    }

    [Fact]
    public void Add_DuplicatePath_MovesToFront()
    {
        _svc.Add("/home/user/a.cs");
        _svc.Add("/home/user/b.cs");
        _svc.Add("/home/user/a.cs");
        var list = _svc.Load();
        Assert.Equal(2, list.Count);
        Assert.Equal("/home/user/a.cs", list[0]);
        Assert.Equal("/home/user/b.cs", list[1]);
    }

    [Fact]
    public void Add_ExceedsMax_OldestEntryDropped()
    {
        var svc = MakeSvc(recentFilesMax: 3);
        svc.Add("/a");
        svc.Add("/b");
        svc.Add("/c");
        svc.Add("/d");
        var list = svc.Load();
        Assert.Equal(3, list.Count);
        Assert.DoesNotContain("/a", list);
        Assert.Equal("/d", list[0]);
    }

    [Fact]
    public void Add_RelativePath_StoredAsAbsolute()
    {
        // Path.GetFullPath will resolve relative to cwd; just verify it's absolute
        _svc.Add("somefile.cs");
        var list = _svc.Load();
        Assert.Single(list);
        Assert.True(Path.IsPathRooted(list[0]));
    }

    // ── Clear ──────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        _svc.Add("/a.cs");
        _svc.Add("/b.cs");
        _svc.Clear();
        Assert.Empty(_svc.Load());
    }

    [Fact]
    public void Clear_OnEmptyList_DoesNotThrow()
    {
        var ex = Record.Exception(() => _svc.Clear());
        Assert.Null(ex);
    }
}
