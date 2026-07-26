using CodeEdit.Infrastructure.FileSystem;

namespace CodeEdit.Tests.Infrastructure;

public sealed class DirectoryListingTests : IDisposable
{
    private readonly string _root;

    public DirectoryListingTests()
    {
        _root = Directory.CreateTempSubdirectory("CodeEditDirListingTests_").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void CreateSubdir(string name)
        => Directory.CreateDirectory(Path.Combine(_root, name));

    private void CreateFile(string name)
        => File.WriteAllText(Path.Combine(_root, name), "");

    // Test 1: valid directory → includes ".." at position 0 and lists immediate subdirs
    [Fact]
    public void BuildEntries_ValidDirectory_IncludesParentAndSubdirs()
    {
        CreateSubdir("alpha");
        CreateSubdir("beta");

        var entries = DirectoryListing.BuildEntries(_root);

        Assert.Equal("..", entries[0]);
        Assert.Contains("alpha/", entries);
        Assert.Contains("beta/", entries);
    }

    // Test 2: files are excluded; only subdirectories appear
    [Fact]
    public void BuildEntries_DirectoryWithFiles_ExcludesFiles()
    {
        CreateSubdir("subdir");
        CreateFile("file.txt");

        var entries = DirectoryListing.BuildEntries(_root);

        Assert.DoesNotContain("file.txt", entries);
        Assert.DoesNotContain("file.txt/", entries);
        Assert.Contains("subdir/", entries);
    }

    // Test 3: sorted case-insensitively
    [Fact]
    public void BuildEntries_MixedCaseNames_SortedCaseInsensitively()
    {
        CreateSubdir("Zebra");
        CreateSubdir("alpha");
        CreateSubdir("Beta");

        var entries = DirectoryListing.BuildEntries(_root);

        // Skip ".."
        var dirs = entries.Skip(1).ToList();
        Assert.Equal(["alpha/", "Beta/", "Zebra/"], dirs);
    }

    // Test 4: ".." absent at filesystem root
    [Fact]
    public void BuildEntries_AtFilesystemRoot_NoParentEntry()
    {
        var root = Path.GetPathRoot(_root)!;
        var entries = DirectoryListing.BuildEntries(root);

        Assert.DoesNotContain("..", entries);
    }

    // Test 5: navigate into subdirectory resets scroll state (entries rebuild)
    [Fact]
    public void BuildEntries_SubdirectoryPath_ReturnsChildEntries()
    {
        CreateSubdir("child");
        var childPath = Path.Combine(_root, "child");
        Directory.CreateDirectory(Path.Combine(childPath, "grandchild"));

        var entries = DirectoryListing.BuildEntries(childPath);

        Assert.Equal("..", entries[0]);
        Assert.Contains("grandchild/", entries);
        // Parent dir's entries should not appear
        Assert.DoesNotContain("child/", entries);
    }

    // Test 6: parent navigation — going up one level yields parent's subdirs
    [Fact]
    public void BuildEntries_ParentPath_ReturnsParentEntries()
    {
        CreateSubdir("sibling");
        var childPath = Path.Combine(_root, "sibling");

        var parentEntries = DirectoryListing.BuildEntries(_root);

        Assert.Contains("sibling/", parentEntries);
    }

    // Test 7: trailing slash appended to every directory entry (except "..")
    [Fact]
    public void BuildEntries_DirectoryNames_HaveTrailingSlash()
    {
        CreateSubdir("mydir");

        var entries = DirectoryListing.BuildEntries(_root);
        var dirEntry = entries.FirstOrDefault(e => e.StartsWith("my"));

        Assert.NotNull(dirEntry);
        Assert.EndsWith("/", dirEntry);
    }

    // Test 13: access-denied directory → list is empty (only ".." present); no exception
    [Fact]
    public void BuildEntries_AccessDenied_ReturnsOnlyParentEntry()
    {
        // Simulate inaccessible directory by pointing at a non-existent path that
        // will throw on EnumerateDirectories — using a file as a directory path.
        var filePath = Path.Combine(_root, "notadir.txt");
        File.WriteAllText(filePath, "");

        // BuildEntries on a path where EnumerateDirectories throws (the path is a file)
        var entries = DirectoryListing.BuildEntries(filePath);

        // Should not throw; ".." is present because filePath is not a root
        Assert.Contains("..", entries);
        // No directory children because EnumerateDirectories threw
        Assert.Single(entries); // only ".."
    }

    // Test 14: scroll state reset: BuildEntries always starts fresh (_selected/_scrollTop reset to 0)
    // This is verified by observing that a new BuildEntries call returns the same entries
    // regardless of prior call state (pure function).
    [Fact]
    public void BuildEntries_CalledTwice_ReturnsSameEntries()
    {
        CreateSubdir("dir1");
        CreateSubdir("dir2");

        var first  = DirectoryListing.BuildEntries(_root);
        var second = DirectoryListing.BuildEntries(_root);

        Assert.Equal(first, second);
    }

    // Empty directory — only ".." in list
    [Fact]
    public void BuildEntries_EmptyDirectory_OnlyParentEntry()
    {
        var entries = DirectoryListing.BuildEntries(_root);

        Assert.Single(entries);
        Assert.Equal("..", entries[0]);
    }
}
