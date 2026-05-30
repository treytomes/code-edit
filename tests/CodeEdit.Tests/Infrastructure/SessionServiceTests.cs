using CodeEdit.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeEdit.Tests.Infrastructure;

public sealed class SessionServiceTests : IDisposable
{
    private readonly string         _tempDir;
    private readonly SessionService _svc;

    public SessionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _svc = new SessionService(NullLogger<SessionService>.Instance, _tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Load ───────────────────────────────────────────────────────────────

    [Fact]
    public void Load_NoFile_ReturnsEmpty()
    {
        var data = _svc.Load();
        Assert.Empty(data.OpenFiles);
        Assert.Equal(0, data.ActiveIndex);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsEmpty()
    {
        var dir = Path.Combine(_tempDir, ".code-edit");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "session.json"), "not json");

        var data = _svc.Load();
        Assert.Empty(data.OpenFiles);
        Assert.Equal(0, data.ActiveIndex);
    }

    // ── Save / Load round-trip ─────────────────────────────────────────────

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var saved = new SessionData(
            ["/home/user/a.cs", "/home/user/b.cs"],
            1,
            "/home/user/project");

        _svc.Save(saved);
        var loaded = _svc.Load();

        Assert.Equal(saved.OpenFiles, loaded.OpenFiles);
        Assert.Equal(saved.ActiveIndex, loaded.ActiveIndex);
        Assert.Equal(saved.RootDir, loaded.RootDir);
    }

    [Fact]
    public void SaveLoad_RootDir_NullRoundTrips()
    {
        _svc.Save(new SessionData(["/a.cs"], 0, null));
        var loaded = _svc.Load();
        Assert.Null(loaded.RootDir);
    }

    [Fact]
    public void Save_CreatesDirectoryIfAbsent()
    {
        var dir = Path.Combine(_tempDir, ".code-edit");
        Assert.False(Directory.Exists(dir));

        _svc.Save(new SessionData(["/a.cs"], 0));

        Assert.True(File.Exists(Path.Combine(dir, "session.json")));
    }

    [Fact]
    public void Save_OverwritesPreviousSession()
    {
        _svc.Save(new SessionData(["/a.cs"], 0));
        _svc.Save(new SessionData(["/b.cs", "/c.cs"], 1));

        var loaded = _svc.Load();
        Assert.Equal(["/b.cs", "/c.cs"], loaded.OpenFiles);
        Assert.Equal(1, loaded.ActiveIndex);
    }

    [Fact]
    public void SaveLoad_ActiveIndex_PreservedAsWritten()
    {
        // SessionService stores and loads ActiveIndex as-is.
        // Clamping to the number of successfully restored files is AppBootstrap's responsibility.
        _svc.Save(new SessionData(["/a.cs", "/b.cs", "/c.cs"], 99));
        var loaded = _svc.Load();
        Assert.Equal(99, loaded.ActiveIndex);
    }
}
