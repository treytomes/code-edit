using CodeEdit.Application;
using CodeEdit.Infrastructure.Theme;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using MouseFlags = Terminal.Gui.Input.MouseFlags;
using TAttr = Terminal.Gui.Drawing.Attribute;

namespace CodeEdit.Presentation.Views;

public sealed class FindResultsPanel : View
{
    public event EventHandler<OpenResultArgs>? ResultOpenRequested;
    public event EventHandler?                 CloseRequested;

    private readonly ThemeRegistry _registry;

    private sealed record ResultRow(
        RowKind Kind,
        int     FileIndex,
        int     MatchIndex,
        string  DisplayText,
        string? FilePath,
        int     LineNumber);

    private enum RowKind { FileHeader, Match }

    private List<ResultRow> _rows      = [];
    private int             _selected  = 0;
    private int             _scrollTop = 0;
    private bool            _hasResults = false;
    private bool            _searching  = false;
    private string          _summary    = "";

    private const int HeaderRows = 1;

    public FindResultsPanel(ThemeRegistry registry)
    {
        _registry = registry;
        CanFocus  = true;
    }

    public void Clear()
    {
        _rows       = [];
        _selected   = 0;
        _scrollTop  = 0;
        _hasResults = false;
        _searching  = false;
        _summary    = "";
        SetNeedsDraw();
    }

    public void SetSearching()
    {
        _rows       = [];
        _selected   = 0;
        _scrollTop  = 0;
        _hasResults = false;
        _searching  = true;
        _summary    = "Searching…";
        SetNeedsDraw();
    }

    public void SetResults(string queryDisplay, IReadOnlyList<FileMatches> results)
    {
        _searching = false;
        _summary   = queryDisplay;
        _rows      = [];
        var navigable = results.Where(f => !string.IsNullOrEmpty(f.FilePath)).ToList();
        var sentinel  = results.FirstOrDefault(f => string.IsNullOrEmpty(f.FilePath));

        for (var fi = 0; fi < navigable.Count; fi++)
        {
            var fm   = navigable[fi];
            var name = Path.GetFileName(fm.FilePath);
            _rows.Add(new ResultRow(RowKind.FileHeader, fi, -1,
                $"{name}  ({fm.Matches.Count} match{(fm.Matches.Count == 1 ? "" : "es")})",
                fm.FilePath, -1));

            for (var mi = 0; mi < fm.Matches.Count; mi++)
            {
                var lm   = fm.Matches[mi];
                var snip = lm.LineText.TrimStart();
                _rows.Add(new ResultRow(RowKind.Match, fi, mi,
                    $"  {lm.LineNumber + 1}: {snip}",
                    fm.FilePath, lm.LineNumber));
            }
        }

        if (sentinel is not null)
            _rows.Add(new ResultRow(RowKind.FileHeader, -1, -1, sentinel.Matches[0].LineText, null, -1));

        // Start selection on first match row if present
        _selected   = _rows.FindIndex(r => r.Kind == RowKind.Match);
        if (_selected < 0) _selected = 0;
        _scrollTop  = 0;
        _hasResults = navigable.Count > 0;
        EnsureVisible();
        SetNeedsDraw();
    }

    public void HighlightResult(int fileIndex, int matchIndex)
    {
        var idx = _rows.FindIndex(r => r.Kind == RowKind.Match
            && r.FileIndex == fileIndex && r.MatchIndex == matchIndex);
        if (idx < 0) return;
        _selected = idx;
        EnsureVisible();
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? _)
    {
        var theme      = _registry.Active;
        var normal     = ColorPairMapper.ToAttribute(theme.FileTree);
        var sel        = ColorPairMapper.ToAttribute(theme.Selection);
        var statusAttr = ColorPairMapper.ToAttribute(theme.StatusBar);
        var fileHeader = new TAttr(
            new Color(theme.StatusBar.Foreground.R, theme.StatusBar.Foreground.G, theme.StatusBar.Foreground.B, 255),
            new Color(theme.FileTree.Background.R,  theme.FileTree.Background.G,  theme.FileTree.Background.B,  255));

        // ── Header row ────────────────────────────────────────────────────
        SetAttribute(statusAttr);
        AddStr(0, 0, new string(' ', Viewport.Width));
        if (_summary.Length > 0)
        {
            var summaryText = _summary.Length > Viewport.Width - 4
                ? _summary[..(Viewport.Width - 4)]
                : _summary;
            AddStr(0, 0, summaryText);
        }
        if (Viewport.Width >= 3)
            AddStr(Viewport.Width - 3, 0, "[x]");

        // ── Content rows ──────────────────────────────────────────────────
        var contentHeight = Viewport.Height - HeaderRows;

        if (!_hasResults && _rows.Count == 0)
        {
            SetAttribute(normal);
            string msg1, msg2;
            if (_searching)
            {
                msg1 = "";
                msg2 = "";
            }
            else
            {
                msg1 = "Press Ctrl+Alt+F";
                msg2 = "to search across files.";
            }
            for (var r = 0; r < contentHeight; r++)
            {
                var absRow = r + HeaderRows;
                if (r == 1 && msg1.Length > 0)
                {
                    AddStr(0, absRow, msg1.PadRight(Viewport.Width));
                }
                else if (r == 2 && msg2.Length > 0)
                {
                    AddStr(0, absRow, msg2.PadRight(Viewport.Width));
                }
                else
                {
                    AddStr(0, absRow, new string(' ', Viewport.Width));
                }
            }
            return true;
        }

        for (var row = 0; row < contentHeight; row++)
        {
            var i      = _scrollTop + row;
            var absRow = row + HeaderRows;
            if (i >= _rows.Count)
            {
                SetAttribute(normal);
                AddStr(0, absRow, new string(' ', Viewport.Width));
                continue;
            }

            var entry = _rows[i];
            var isSel = i == _selected;
            var attr  = entry.Kind == RowKind.FileHeader ? fileHeader
                      : isSel                            ? sel
                                                        : normal;
            SetAttribute(attr);

            var text = entry.DisplayText;
            if (text.Length > Viewport.Width) text = text[..Viewport.Width];
            AddStr(0, absRow, text.PadRight(Viewport.Width));
        }
        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key.KeyCode == KeyCode.Esc)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.CursorUp)
        {
            MoveSelection(-1);
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.CursorDown)
        {
            MoveSelection(1);
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.Enter)
        {
            OpenSelected();
            key.Handled = true;
            return true;
        }
        // Consume all printable keys so they don't bubble to the window-level handler
        if (key.TryGetPrintableRune(out _))
        {
            key.Handled = true;
            return true;
        }
        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.Position.HasValue) return base.OnMouseEvent(mouse);

        var pos = mouse.Position.Value;

        // Close button hit test: row 0, columns >= Viewport.Width - 3
        if (pos.Y == 0 && pos.X >= Viewport.Width - 3)
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                mouse.Handled = true;
                return true;
            }
        }

        // Content rows start at row HeaderRows
        var contentRow = pos.Y - HeaderRows;
        if (contentRow < 0) return base.OnMouseEvent(mouse);

        var clickRow = _scrollTop + contentRow;
        if (clickRow < 0 || clickRow >= _rows.Count) return base.OnMouseEvent(mouse);

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            _selected = clickRow;
            SetFocus();
            SetNeedsDraw();
            mouse.Handled = true;
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            _selected = clickRow;
            OpenSelected();
            mouse.Handled = true;
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    private void MoveSelection(int delta)
    {
        var next = _selected + delta;
        if (next < 0 || next >= _rows.Count) return;
        _selected = next;
        EnsureVisible();
        SetNeedsDraw();
    }

    private void OpenSelected()
    {
        if (_selected < 0 || _selected >= _rows.Count) return;
        var row = _rows[_selected];
        if (row.FilePath is null) return;
        var line = row.Kind == RowKind.FileHeader
            ? _rows.FirstOrDefault(r => r.Kind == RowKind.Match && r.FileIndex == row.FileIndex)?.LineNumber ?? 0
            : row.LineNumber;
        ResultOpenRequested?.Invoke(this, new OpenResultArgs(row.FilePath, line));
    }

    private void EnsureVisible()
    {
        var h = (Viewport.Height > 0 ? Viewport.Height : 10) - HeaderRows;
        if (h < 1) h = 1;
        if (_selected < _scrollTop) _scrollTop = _selected;
        if (_selected >= _scrollTop + h) _scrollTop = _selected - h + 1;
        if (_scrollTop < 0) _scrollTop = 0;
    }
}

public sealed record OpenResultArgs(string FilePath, int LineNumber);
