using CodeEdit.Application;
using CodeEdit.Domain;
using CodeEdit.Infrastructure.Theme;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace CodeEdit.Presentation.Views;

public sealed class TabBarView : View
{
    private readonly ThemeRegistry _themeRegistry;

    private IReadOnlyList<TabEntry> _tabs        = [];
    private int                     _activeIndex = -1;
    private int                     _scrollOffset = 0;  // tabs scrolled off the left

    public event EventHandler<int>? TabActivated;
    public event EventHandler<int>? TabCloseRequested;
    public event EventHandler<int>? VisibilityChanged;   // arg = new height (0 or 1)

    public TabBarView(ThemeRegistry themeRegistry)
    {
        _themeRegistry = themeRegistry;
        CanFocus = false;
        Height   = 1;
        Visible  = true;
    }

    public void Refresh(IReadOnlyList<TabEntry> tabs, int activeIndex)
    {
        _tabs        = tabs;
        _activeIndex = activeIndex;
        EnsureActiveVisible();
        SetNeedsDraw();
    }

    public void Toggle()
    {
        Visible = !Visible;
        Height  = Visible ? 1 : 0;
        VisibilityChanged?.Invoke(this, Visible ? 1 : 0);
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    protected override bool OnDrawingContent(DrawContext? context)
    {
        base.OnDrawingContent(context);
        if (_tabs.Count == 0) return false;

        var theme    = _themeRegistry.Active;
        var normal   = ColorPairMapper.ToAttribute(theme.TabBar);
        var active   = ColorPairMapper.ToAttribute(theme.Selection);
        var width    = Viewport.Width;
        var col      = 0;

        var hasLeft  = _scrollOffset > 0;
        var rightMax = width - (hasLeft ? 1 : 0);

        // Left scroll indicator
        if (hasLeft)
        {
            SetAttribute(normal);
            AddStr(0, 0, "◀");
            col = 1;
        }

        // Tabs
        for (var i = _scrollOffset; i < _tabs.Count; i++)
        {
            var label    = TabLabel(_tabs[i]);
            var isActive = i == _activeIndex;

            if (col + label.Length > rightMax) break;

            SetAttribute(isActive ? active : normal);
            AddStr(col, 0, label);
            col += label.Length;

            // Space between tabs
            if (col < rightMax)
            {
                SetAttribute(normal);
                AddStr(col, 0, " ");
                col++;
            }
        }

        // Right scroll indicator
        var hasRight = HasTabsBeyond(rightMax, col);
        if (hasRight)
        {
            SetAttribute(normal);
            AddStr(width - 1, 0, "▶");
        }

        // Fill remainder
        SetAttribute(normal);
        while (col < (hasRight ? width - 1 : width))
        {
            AddStr(col, 0, " ");
            col++;
        }
        return true;
    }

    // ── Mouse ──────────────────────────────────────────────────────────────

    protected override bool OnMouseEvent(Mouse mouseEvent)
    {
        if (!mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonClicked)) return base.OnMouseEvent(mouseEvent);

        if (!mouseEvent.Position.HasValue) return base.OnMouseEvent(mouseEvent);
        var col      = mouseEvent.Position.Value.X;
        var width    = Viewport.Width;
        var hasLeft  = _scrollOffset > 0;

        // Left scroll arrow
        if (hasLeft && col == 0)
        {
            _scrollOffset = Math.Max(0, _scrollOffset - 1);
            SetNeedsDraw();
            return true;
        }

        // Right scroll arrow
        if (col == width - 1 && HasTabsBeyond(width - (hasLeft ? 1 : 0), ComputeTabsWidth()))
        {
            _scrollOffset = Math.Min(_tabs.Count - 1, _scrollOffset + 1);
            SetNeedsDraw();
            return true;
        }

        // Hit-test tabs
        var cursor = hasLeft ? 1 : 0;
        for (var i = _scrollOffset; i < _tabs.Count; i++)
        {
            var label = TabLabel(_tabs[i]);
            if (col >= cursor && col < cursor + label.Length)
            {
                // The × is the third-to-last character: "[ title × ]"
                //                                                ^  col = cursor + label.Length - 3
                var closeCol = cursor + label.Length - 3;
                if (col == closeCol)
                    TabCloseRequested?.Invoke(this, i);
                else
                    TabActivated?.Invoke(this, i);
                return true;
            }
            cursor += label.Length + 1;
            if (cursor >= width - 1) break;
        }

        return base.OnMouseEvent(mouseEvent);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string TabLabel(TabEntry tab)
    {
        var name = tab.Buffer.FilePath is null
            ? "Untitled"
            : Path.GetFileName(tab.Buffer.FilePath);
        var title = tab.Buffer.IsDirty ? $"*{name}" : name!;
        return $"[ {title} × ]";
    }

    private void EnsureActiveVisible()
    {
        if (_activeIndex < _scrollOffset)
        {
            _scrollOffset = _activeIndex;
            return;
        }

        var width    = Viewport.Width;
        var hasLeft  = _scrollOffset > 0;
        var col      = hasLeft ? 1 : 0;

        for (var i = _scrollOffset; i < _tabs.Count; i++)
        {
            var len = TabLabel(_tabs[i]).Length + 1;
            if (i == _activeIndex && col + len > width - 1)
            {
                _scrollOffset++;
                EnsureActiveVisible();
                return;
            }
            col += len;
        }
    }

    private bool HasTabsBeyond(int rightMax, int usedCols)
    {
        // Check if any tab beyond the current render fits past rightMax
        var col = usedCols;
        for (var i = _scrollOffset; i < _tabs.Count; i++)
        {
            var len = TabLabel(_tabs[i]).Length + 1;
            if (col + len > rightMax) return true;
            col += len;
        }
        return false;
    }

    private int ComputeTabsWidth()
    {
        var hasLeft = _scrollOffset > 0;
        var col     = hasLeft ? 1 : 0;
        var width   = Viewport.Width;
        for (var i = _scrollOffset; i < _tabs.Count; i++)
        {
            var len = TabLabel(_tabs[i]).Length + 1;
            if (col + len > width - 1) break;
            col += len;
        }
        return col;
    }
}
