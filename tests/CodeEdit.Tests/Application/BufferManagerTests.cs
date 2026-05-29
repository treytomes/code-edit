using CodeEdit.Application;
using CodeEdit.Domain;

namespace CodeEdit.Tests.Application;

public sealed class BufferManagerTests
{
    private static FakeBuffer MakeBuf(string? filePath = null) => new(filePath);

    // ── Add ────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_FirstBuffer_BecomesActiveAtIndex0()
    {
        var bm  = new BufferManager();
        var buf = MakeBuf();
        bm.Add(buf);
        Assert.Equal(0, bm.ActiveIndex);
        Assert.Same(buf, bm.ActiveBuffer);
    }

    [Fact]
    public void Add_SecondBuffer_ActivatesNewTab()
    {
        var bm = new BufferManager();
        bm.Add(MakeBuf());
        bm.Add(MakeBuf());
        Assert.Equal(1, bm.ActiveIndex);
        Assert.Equal(2, bm.Tabs.Count);
    }

    [Fact]
    public void Add_DuplicateFilePath_ActivatesExistingTab()
    {
        var bm   = new BufferManager();
        var buf1 = MakeBuf("/a/b.cs");
        var buf2 = MakeBuf("/a/b.cs");
        bm.Add(buf1);
        bm.Add(MakeBuf());       // tab 1
        bm.Add(buf2);            // should activate tab 0, not add a third
        Assert.Equal(2, bm.Tabs.Count);
        Assert.Equal(0, bm.ActiveIndex);
    }

    [Fact]
    public void Add_MultipleNullFilePaths_AllowsMultipleTabs()
    {
        var bm = new BufferManager();
        bm.Add(MakeBuf(null));
        bm.Add(MakeBuf(null));
        bm.Add(MakeBuf(null));
        Assert.Equal(3, bm.Tabs.Count);
    }

    [Fact]
    public void Add_RaisesTabsChangedAndActiveTabChanged()
    {
        var bm            = new BufferManager();
        var tabsChanged   = 0;
        var activeChanged = 0;
        bm.TabsChanged      += (_, _) => tabsChanged++;
        bm.ActiveTabChanged += (_, _) => activeChanged++;

        bm.Add(MakeBuf());

        Assert.Equal(1, tabsChanged);
        Assert.Equal(1, activeChanged);
    }

    // ── Activate ───────────────────────────────────────────────────────────

    [Fact]
    public void Activate_ChangesActiveIndex()
    {
        var bm = new BufferManager();
        bm.Add(MakeBuf());
        bm.Add(MakeBuf());
        bm.Activate(0);
        Assert.Equal(0, bm.ActiveIndex);
    }

    [Fact]
    public void Activate_SameIndex_DoesNotFireEvent()
    {
        var bm  = new BufferManager();
        bm.Add(MakeBuf());
        var fired = 0;
        bm.ActiveTabChanged += (_, _) => fired++;

        bm.Activate(0);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Activate_OutOfRange_Throws()
    {
        var bm = new BufferManager();
        bm.Add(MakeBuf());
        Assert.Throws<ArgumentOutOfRangeException>(() => bm.Activate(5));
    }

    // ── Close ──────────────────────────────────────────────────────────────

    [Fact]
    public void Close_NonActiveTab_RemovesItAndKeepsActiveIndex()
    {
        var bm = new BufferManager();
        bm.Add(MakeBuf()); // 0
        bm.Add(MakeBuf()); // 1 — active
        bm.Close(0);
        Assert.Equal(1, bm.Tabs.Count);
        Assert.Equal(0, bm.ActiveIndex);
    }

    [Fact]
    public void Close_ActiveTab_ActivatesNearestRemaining()
    {
        var bm = new BufferManager();
        bm.Add(MakeBuf()); // 0
        bm.Add(MakeBuf()); // 1
        bm.Add(MakeBuf()); // 2 — active
        bm.Close(2);
        Assert.Equal(1, bm.ActiveIndex);
    }

    [Fact]
    public void Close_LastTab_Throws()
    {
        var bm = new BufferManager();
        bm.Add(MakeBuf());
        Assert.Throws<InvalidOperationException>(() => bm.Close(0));
    }

    [Fact]
    public void Close_RaisesTabClosedAndTabsChanged()
    {
        var bm          = new BufferManager();
        bm.Add(MakeBuf());
        bm.Add(MakeBuf());
        var closedIndex = -1;
        var tabsChanged = 0;
        bm.TabClosed    += (_, i) => closedIndex = i;
        bm.TabsChanged  += (_, _) => tabsChanged++;

        bm.Close(0);

        Assert.Equal(0, closedIndex);
        Assert.Equal(1, tabsChanged);
    }

    // ── Per-tab history ────────────────────────────────────────────────────

    [Fact]
    public void EachTab_HasItsOwnHistory()
    {
        var bm = new BufferManager();
        bm.Add(MakeBuf());
        bm.Add(MakeBuf());

        Assert.NotSame(bm.Tabs[0].History, bm.Tabs[1].History);
    }

    // ── FakeBuffer ─────────────────────────────────────────────────────────

    private sealed class FakeBuffer(string? filePath) : IMutableTextBuffer
    {
        public string?        FilePath         => filePath;
        public int            LineCount        => 0;
        public CursorPosition Cursor           { get; private set; }
        public Selection?     Selection        { get; private set; }
        public bool           IsDirty          => false;
        public string?        DetectedLanguage => null;
        public string         GetLine(int i)   => "";
        public void InsertText(CursorPosition at, string text) { }
        public void DeleteRange(TextRange range)               { }
        public void SetCursor(CursorPosition pos)              { Cursor = pos; }
        public void SetSelection(Selection? s)                 { Selection = s; }
        public void ResizeCache(int h)                         { }
    }
}
