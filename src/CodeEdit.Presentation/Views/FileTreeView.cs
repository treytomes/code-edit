using CodeEdit.Application;
using CodeEdit.Infrastructure.Theme;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace CodeEdit.Presentation.Views;

public sealed class FileTreeView : View
{
    private readonly ThemeRegistry _themeRegistry;

    private string           _root      = "";
    private List<TreeEntry>  _entries   = [];
    private int              _selected;
    private int              _scrollTop;
    private HashSet<string>  _expanded  = [];

    private sealed record TreeEntry(string Path, string Name, bool IsDirectory, int Depth);

    public event EventHandler<string>? FileOpenRequested;

    public FileTreeView(ThemeRegistry themeRegistry)
    {
        _themeRegistry        = themeRegistry;
        CanFocus              = true;
        Width                 = 30;
    }

    public void Populate(string root)
    {
        _root      = root;
        _selected  = 0;
        _scrollTop = 0;
        _expanded.Clear();
        RebuildEntries();
        SetNeedsDraw();
    }

    public void Refresh()
    {
        RebuildEntries();
        _selected  = Math.Clamp(_selected, 0, Math.Max(0, _entries.Count - 1));
        SetNeedsDraw();
    }

    // ── Rendering ──────────────────────────────────────────────────────────

    protected override bool OnDrawingContent(DrawContext? context)
    {
        base.OnDrawingContent(context);

        var theme     = _themeRegistry.Active;
        var normal    = ColorPairMapper.ToAttribute(theme.FileTree);
        var selection = ColorPairMapper.ToAttribute(theme.Selection);
        var width     = Viewport.Width;
        var height    = Viewport.Height;

        for (var row = 0; row < height; row++)
        {
            var idx = _scrollTop + row;
            if (idx >= _entries.Count)
            {
                SetAttribute(normal);
                AddStr(0, row, new string(' ', width));
                continue;
            }

            var entry   = _entries[idx];
            var isActive = idx == _selected;
            SetAttribute(isActive ? selection : normal);

            var indent = new string(' ', entry.Depth * 2);
            var prefix = entry.IsDirectory
                ? (_expanded.Contains(entry.Path) ? "▼ " : "▶ ")
                : "  ";
            var label = indent + prefix + entry.Name;
            if (label.Length > width) label = label[..width];
            AddStr(0, row, label.PadRight(width));
        }

        return true;
    }

    // ── Keyboard ────────────────────────────────────────────────────────────

    protected override bool OnKeyDown(Key key)
    {
        var height = Viewport.Height;

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
        if (key.KeyCode == KeyCode.Home)
        {
            _selected = 0;
            EnsureVisible();
            SetNeedsDraw();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.End)
        {
            _selected = Math.Max(0, _entries.Count - 1);
            EnsureVisible();
            SetNeedsDraw();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.PageUp)
        {
            MoveSelection(-Math.Max(1, height));
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.PageDown)
        {
            MoveSelection(Math.Max(1, height));
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.Enter)
        {
            ActivateSelected();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.Space)
        {
            ToggleSelectedDir();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.F5)
        {
            Refresh();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.Esc)
        {
            // AppBootstrap moves focus back to EditorView
            key.Handled = false;
            return false;
        }

        return false;
    }

    // ── Mouse ────────────────────────────────────────────────────────────────

    protected override bool OnMouseEvent(Mouse mouseEvent)
    {
        if (!mouseEvent.Position.HasValue) return base.OnMouseEvent(mouseEvent);
        var row = mouseEvent.Position.Value.Y;
        var idx = _scrollTop + row;
        if (idx < 0 || idx >= _entries.Count) return base.OnMouseEvent(mouseEvent);

        if (mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            _selected = idx;
            SetNeedsDraw();
            SetFocus();
            mouseEvent.Handled = true;
            return true;
        }

        if (mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            _selected = idx;
            ActivateSelected();
            mouseEvent.Handled = true;
            return true;
        }

        return base.OnMouseEvent(mouseEvent);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void RebuildEntries()
    {
        _entries = [];
        if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root))
            return;
        Walk(_root, depth: 0);
    }

    private void Walk(string dir, int depth)
    {
        IEnumerable<string> children;
        try { children = Directory.EnumerateFileSystemEntries(dir); }
        catch { return; }

        var dirs  = new List<string>();
        var files = new List<string>();
        foreach (var path in children)
        {
            if (Directory.Exists(path)) dirs.Add(path);
            else                        files.Add(path);
        }

        dirs.Sort(StringComparer.OrdinalIgnoreCase);
        files.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var d in dirs)
        {
            _entries.Add(new TreeEntry(d, Path.GetFileName(d)!, IsDirectory: true, depth));
            if (_expanded.Contains(d))
                Walk(d, depth + 1);
        }
        foreach (var f in files)
        {
            _entries.Add(new TreeEntry(f, Path.GetFileName(f)!, IsDirectory: false, depth));
        }
    }

    private void MoveSelection(int delta)
    {
        _selected = Math.Clamp(_selected + delta, 0, Math.Max(0, _entries.Count - 1));
        EnsureVisible();
        SetNeedsDraw();
    }

    private void EnsureVisible()
    {
        var height = Viewport.Height;
        if (_selected < _scrollTop)
            _scrollTop = _selected;
        else if (height > 0 && _selected >= _scrollTop + height)
            _scrollTop = _selected - height + 1;
    }

    private void ActivateSelected()
    {
        if (_entries.Count == 0) return;
        var entry = _entries[_selected];
        if (entry.IsDirectory)
            ToggleDir(entry.Path);
        else
            FileOpenRequested?.Invoke(this, entry.Path);
    }

    private void ToggleSelectedDir()
    {
        if (_entries.Count == 0) return;
        var entry = _entries[_selected];
        if (entry.IsDirectory)
            ToggleDir(entry.Path);
    }

    private void ToggleDir(string path)
    {
        if (_expanded.Contains(path))
            _expanded.Remove(path);
        else
            _expanded.Add(path);
        RebuildEntries();
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _entries.Count - 1));
        EnsureVisible();
        SetNeedsDraw();
    }
}
