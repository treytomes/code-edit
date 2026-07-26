using CodeEdit.Application.Ports;
using CodeEdit.Infrastructure.FileSystem;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using MouseFlags = Terminal.Gui.Input.MouseFlags;

namespace CodeEdit.Presentation.Views;

public sealed class FolderPickerDialog : Dialog
{
    private const int DialogWidth  = 62;
    private const int DialogHeight = 18;
    private const int ListHeight   = 10;

    private readonly IApplication      _app;
    private readonly IColorTheme       _theme;
    private string                     _currentPath = "";
    private readonly TextField         _pathField;
    private readonly DirectoryListView _listView;
    private readonly ErrorLabel        _errorView;

    public new bool Canceled   { get; private set; } = true;
    public string SelectedPath { get; private set; } = "";

    public FolderPickerDialog(IApplication app, IColorTheme theme, string initialPath)
    {
        _app   = app;
        _theme = theme;

        var resolved = Directory.Exists(initialPath)
            ? initialPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Title  = "Open Folder";
        Width  = DialogWidth;
        Height = DialogHeight;

        var lblPath = new Label { Text = "Path:", X = 2, Y = 1 };

        _pathField = new TextField
        {
            X     = 9,
            Y     = 1,
            Width = Dim.Fill() - Dim.Absolute(4),
            Text  = resolved,
        };

        _errorView = new ErrorLabel(theme)
        {
            X      = 2,
            Y      = 2,
            Width  = Dim.Fill() - Dim.Absolute(4),
            Height = Dim.Absolute(1),
        };

        _listView = new DirectoryListView(theme)
        {
            X      = 2,
            Y      = 3,
            Width  = Dim.Fill() - Dim.Absolute(4),
            Height = Dim.Absolute(ListHeight),
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            X    = Pos.AnchorEnd(29),
            Y    = Pos.AnchorEnd(1),
        };
        var btnAccept = new Button
        {
            Text      = "Select Folder",
            X         = Pos.AnchorEnd(18),
            Y         = Pos.AnchorEnd(1),
            IsDefault = true,
        };

        _pathField.Accepting     += (_, _) => NavigateTo(_pathField.Text?.Trim() ?? "");
        _listView.EntryActivated += OnEntryActivated;
        btnCancel.Accepting      += (_, _) => _app.RequestStop(this);
        btnAccept.Accepting      += (_, _) => DoAccept();

        Add(lblPath, _pathField, _errorView, _listView, btnCancel, btnAccept);

        NavigateTo(resolved);
        _pathField.SetFocus();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key.KeyCode == KeyCode.Esc)
        {
            Canceled = true;
            _app.RequestStop(this);
            key.Handled = true;
            return true;
        }
        return base.OnKeyDown(key);
    }

    private void NavigateTo(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch { _errorView.SetText("Invalid path"); return; }

        if (!Directory.Exists(full))
        {
            _errorView.SetText("Path not found");
            return;
        }

        _currentPath    = full;
        _pathField.Text = full;
        _listView.Populate(full);
        _errorView.SetText("");
        SetNeedsDraw();
    }

    private void OnEntryActivated(object? sender, string entry)
    {
        string next;
        if (entry == "..")
        {
            var parent = Path.GetDirectoryName(_currentPath);
            if (parent is null) return;
            next = parent;
        }
        else
        {
            next = Path.Combine(_currentPath, entry);
        }
        NavigateTo(next);
        _listView.SetFocus();
    }

    private void DoAccept()
    {
        NavigateTo(_pathField.Text?.Trim() ?? "");
        if (_errorView.HasError) return;
        SelectedPath = _currentPath;
        Canceled     = false;
        _app.RequestStop(this);
    }

    // ── Inner types ────────────────────────────────────────────────────────────

    private sealed class ErrorLabel : View
    {
        private readonly IColorTheme _theme;
        private string               _text = "";

        public ErrorLabel(IColorTheme theme)
        {
            _theme   = theme;
            CanFocus = false;
            Visible  = false;
        }

        public void SetText(string text)
        {
            _text   = text;
            Visible = text.Length > 0;
            SetNeedsDraw();
        }

        public bool HasError => _text.Length > 0;

        protected override bool OnDrawingContent(DrawContext? _)
        {
            SetAttribute(ColorPairMapper.ToAttribute(_theme.StatusBar));
            var w       = Viewport.Width;
            var display = _text.Length > w ? _text[..w] : _text.PadRight(w);
            AddStr(0, 0, display);
            return true;
        }
    }

    private sealed class DirectoryListView : View
    {
        private List<string>     _entries   = [];
        private int              _selected;
        private int              _scrollTop;
        private readonly IColorTheme _theme;

        public event EventHandler<string>? EntryActivated;

        public DirectoryListView(IColorTheme theme)
        {
            _theme   = theme;
            CanFocus = true;
        }

        public void Populate(string directoryPath)
        {
            _entries   = DirectoryListing.BuildEntries(directoryPath);
            _selected  = 0;
            _scrollTop = 0;
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? _)
        {
            var width  = Viewport.Width;
            var height = Viewport.Height;
            var normal = ColorPairMapper.ToAttribute(_theme.FileTree);
            var sel    = ColorPairMapper.ToAttribute(_theme.Selection);

            for (var row = 0; row < height; row++)
            {
                var idx = _scrollTop + row;
                if (idx >= _entries.Count)
                {
                    SetAttribute(normal);
                    AddStr(0, row, new string(' ', width));
                    continue;
                }
                var isSelected = idx == _selected;
                SetAttribute(isSelected ? sel : normal);
                var text = _entries[idx];
                if (text.Length > width) text = text[..width];
                AddStr(0, row, text.PadRight(width));
            }
            return true;
        }

        protected override bool OnKeyDown(Key key)
        {
            var height = Viewport.Height;
            switch (key.KeyCode)
            {
                case KeyCode.CursorUp:
                    MoveSelection(-1);
                    key.Handled = true;
                    return true;
                case KeyCode.CursorDown:
                    MoveSelection(1);
                    key.Handled = true;
                    return true;
                case KeyCode.Home:
                    _selected = 0;
                    EnsureVisible();
                    SetNeedsDraw();
                    key.Handled = true;
                    return true;
                case KeyCode.End:
                    _selected = Math.Max(0, _entries.Count - 1);
                    EnsureVisible();
                    SetNeedsDraw();
                    key.Handled = true;
                    return true;
                case KeyCode.PageUp:
                    MoveSelection(-Math.Max(1, height));
                    key.Handled = true;
                    return true;
                case KeyCode.PageDown:
                    MoveSelection(Math.Max(1, height));
                    key.Handled = true;
                    return true;
                case KeyCode.Enter:
                    if (_entries.Count > 0)
                        EntryActivated?.Invoke(this, _entries[_selected]);
                    key.Handled = true;
                    return true;
            }
            return base.OnKeyDown(key);
        }

        protected override bool OnMouseEvent(Mouse mouseEvent)
        {
            if (mouseEvent.Flags.HasFlag(MouseFlags.WheeledUp))
            {
                MoveSelection(-1);
                mouseEvent.Handled = true;
                return true;
            }
            if (mouseEvent.Flags.HasFlag(MouseFlags.WheeledDown))
            {
                MoveSelection(1);
                mouseEvent.Handled = true;
                return true;
            }

            if (!mouseEvent.Position.HasValue) return base.OnMouseEvent(mouseEvent);
            var row = mouseEvent.Position.Value.Y;
            var idx = _scrollTop + row;
            if (idx < 0 || idx >= _entries.Count) return base.OnMouseEvent(mouseEvent);

            if (mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                _selected = idx;
                SetFocus();
                SetNeedsDraw();
                mouseEvent.Handled = true;
                return true;
            }
            if (mouseEvent.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
            {
                _selected = idx;
                EntryActivated?.Invoke(this, _entries[_selected]);
                mouseEvent.Handled = true;
                return true;
            }
            return base.OnMouseEvent(mouseEvent);
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
    }
}

