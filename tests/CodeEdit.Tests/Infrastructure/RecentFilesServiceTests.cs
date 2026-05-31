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
        _svc.Add("/home/user/foo.cs", RecentKind.File);
        var list = _svc.Load();
        Assert.Single(list);
        Assert.Equal("/home/user/foo.cs", list[0].Path);
        Assert.Equal(RecentKind.File, list[0].Kind);
    }

    [Fact]
    public void Add_SecondPath_PrependsMostRecent()
    {
        _svc.Add("/home/user/a.cs", RecentKind.File);
        _svc.Add("/home/user/b.cs", RecentKind.File);
        var list = _svc.Load();
        Assert.Equal("/home/user/b.cs", list[0].Path);
        Assert.Equal("/home/user/a.cs", list[1].Path);
    }

    [Fact]
    public void Add_DuplicatePath_MovesToFront()
    {
        _svc.Add("/home/user/a.cs", RecentKind.File);
        _svc.Add("/home/user/b.cs", RecentKind.File);
        _svc.Add("/home/user/a.cs", RecentKind.File);
        var list = _svc.Load();
        Assert.Equal(2, list.Count);
        Assert.Equal("/home/user/a.cs", list[0].Path);
        Assert.Equal("/home/user/b.cs", list[1].Path);
    }

    [Fact]
    public void Add_ExceedsMax_OldestEntryDropped()
    {
        var svc = MakeSvc(recentFilesMax: 3);
        svc.Add("/a", RecentKind.File);
        svc.Add("/b", RecentKind.File);
        svc.Add("/c", RecentKind.File);
        svc.Add("/d", RecentKind.File);
        var list = svc.Load();
        Assert.Equal(3, list.Count);
        Assert.DoesNotContain(list, e => e.Path == "/a");
        Assert.Equal("/d", list[0].Path);
    }

    [Fact]
    public void Add_RelativePath_StoredAsAbsolute()
    {
        _svc.Add("somefile.cs", RecentKind.File);
        var list = _svc.Load();
        Assert.Single(list);
        Assert.True(Path.IsPathRooted(list[0].Path));
    }

    [Fact]
    public void Add_Folder_StoresKindFolder()
    {
        _svc.Add("/home/user/projects", RecentKind.Folder);
        var list = _svc.Load();
        Assert.Single(list);
        Assert.Equal(RecentKind.Folder, list[0].Kind);
    }

    // ── Clear ──────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        _svc.Add("/a.cs", RecentKind.File);
        _svc.Add("/b.cs", RecentKind.File);
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
