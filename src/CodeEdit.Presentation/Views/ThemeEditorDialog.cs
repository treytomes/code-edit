using CodeEdit.Application;
using CodeEdit.Application.Ports;
using Terminal.Gui.Input;
using TAttr = Terminal.Gui.Drawing.Attribute;
using CodeEdit.Domain;
using CodeEdit.Infrastructure.Settings;
using CodeEdit.Infrastructure.Theme;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TGuiApp = Terminal.Gui.App.Application;

namespace CodeEdit.Presentation.Views;

public sealed class ThemeEditorDialog : Dialog
{
    private readonly ThemeService    _themeSvc;
    private readonly ThemeRegistry   _registry;
    private readonly SettingsService _settingsSvc;

    private readonly List<UserTheme> _themes;
    private readonly string          _originalActiveName;
    private int                      _selectedTheme;
    private UserTheme                _working => _themes[_selectedTheme];

    private int  _selectedRole  = 0;
    private bool _editingFg     = true;

    // UI
    private readonly ThemeListPanel _themePanel;
    private readonly RoleListPanel  _rolePanel;
    private readonly TextField      _rField;
    private readonly TextField      _gField;
    private readonly TextField      _bField;
    private readonly Label          _swatchLabel;
    private readonly Label          _fgBgLabel;

    private record RoleDef(
        string Label,
        Func<UserTheme, ColorPair>?   Get,
        Action<UserTheme, ColorPair>? Set);

    private readonly List<RoleDef> _roles;
    private bool _suppressRgbChange;

    public ThemeEditorDialog(ThemeService themeSvc, ThemeRegistry registry, SettingsService settingsSvc)
    {
        _themeSvc    = themeSvc;
        _registry    = registry;
        _settingsSvc = settingsSvc;

        Title  = "Theme Editor";
        Width  = 80;
        Height = 24;

        _originalActiveName = registry.Active is UserTheme ut ? ut.Name : "VS Code Dark+";
        _themes = _themeSvc.LoadAll().Select(t => t.Clone()).ToList();
        if (_themes.Count == 0) _themes.Add(UserTheme.FromDefaults());
        _selectedTheme = 0;

        _roles =
        [
            new("Normal",       t => t.Normal,      (t,p) => t.Normal      = p),
            new("Selection",    t => t.Selection,   (t,p) => t.Selection   = p),
            new("Line Number",  t => t.LineNumber,  (t,p) => t.LineNumber  = p),
            new("Status Bar",   t => t.StatusBar,   (t,p) => t.StatusBar   = p),
            new("Menu Bar",     t => t.MenuBar,     (t,p) => t.MenuBar     = p),
            new("Tab Bar",      t => t.TabBar,      (t,p) => t.TabBar      = p),
            new("File Tree",    t => t.FileTree,    (t,p) => t.FileTree    = p),
            new("Dialog",       t => t.Dialog,      (t,p) => t.Dialog      = p),
            new("Search Match", t => t.SearchMatch, (t,p) => t.SearchMatch = p),
            new("── Tokens ──", null, null),
            new("Keyword",      t => t.ForToken(TokenType.Keyword),       (t,p) => t.Tokens[TokenType.Keyword]       = p),
            new("String",       t => t.ForToken(TokenType.StringLiteral), (t,p) => t.Tokens[TokenType.StringLiteral] = p),
            new("Char",         t => t.ForToken(TokenType.CharLiteral),   (t,p) => t.Tokens[TokenType.CharLiteral]   = p),
            new("Comment",      t => t.ForToken(TokenType.Comment),       (t,p) => t.Tokens[TokenType.Comment]       = p),
            new("Number",       t => t.ForToken(TokenType.Number),        (t,p) => t.Tokens[TokenType.Number]        = p),
            new("Operator",     t => t.ForToken(TokenType.Operator),      (t,p) => t.Tokens[TokenType.Operator]      = p),
            new("Punctuation",  t => t.ForToken(TokenType.Punctuation),   (t,p) => t.Tokens[TokenType.Punctuation]   = p),
            new("Identifier",   t => t.ForToken(TokenType.Identifier),    (t,p) => t.Tokens[TokenType.Identifier]    = p),
        ];

        // ── Left panel ─────────────────────────────────────────────────────
        _themePanel = new ThemeListPanel(this)
        {
            X = 0, Y = 0,
            Width = Dim.Absolute(27),
            Height = Dim.Fill() - Dim.Absolute(1),
        };

        // ── Right panel ────────────────────────────────────────────────────
        _rolePanel = new RoleListPanel(this)
        {
            X = Pos.Right(_themePanel),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - Dim.Absolute(1),
        };

        // ── RGB editor row ─────────────────────────────────────────────────
        _fgBgLabel  = new Label { Text = "Fg", X = 1,                  Y = Pos.AnchorEnd(1) };
        var lblR    = new Label { Text = " R:", X = Pos.Right(_fgBgLabel), Y = Pos.AnchorEnd(1) };
        _rField     = new TextField { X = Pos.Right(lblR),  Y = Pos.AnchorEnd(1), Width = 4 };
        var lblG    = new Label { Text = " G:", X = Pos.Right(_rField), Y = Pos.AnchorEnd(1) };
        _gField     = new TextField { X = Pos.Right(lblG),  Y = Pos.AnchorEnd(1), Width = 4 };
        var lblB    = new Label { Text = " B:", X = Pos.Right(_gField), Y = Pos.AnchorEnd(1) };
        _bField     = new TextField { X = Pos.Right(lblB),  Y = Pos.AnchorEnd(1), Width = 4 };
        var toggleBtn = new Button { Text = "Fg/Bg", X = Pos.Right(_bField) + 1, Y = Pos.AnchorEnd(1) };
        _swatchLabel  = new Label  { Text = "     ", X = Pos.Right(toggleBtn) + 1, Y = Pos.AnchorEnd(1) };

        _rField.TextChanged += (_, _) => OnRgbChanged();
        _gField.TextChanged += (_, _) => OnRgbChanged();
        _bField.TextChanged += (_, _) => OnRgbChanged();
        toggleBtn.Accepting += (_, _) => { _editingFg = !_editingFg; _fgBgLabel.Text = _editingFg ? "Fg" : "Bg"; LoadRgbEditor(); };

        // ── Bottom buttons ─────────────────────────────────────────────────
        var btnRevert = new Button { Text = "Revert", X = Pos.AnchorEnd(36), Y = Pos.AnchorEnd(1) };
        var btnCancel = new Button { Text = "Cancel", X = Pos.AnchorEnd(24), Y = Pos.AnchorEnd(1) };
        var btnSave   = new Button { Text = "Save",   X = Pos.AnchorEnd(12), Y = Pos.AnchorEnd(1), IsDefault = true };

        btnRevert.Accepting += (_, _) => DoRevert();
        btnCancel.Accepting += (_, _) => DoCancel();
        btnSave.Accepting   += (_, _) => DoSave();

        Add(_themePanel, _rolePanel,
            _fgBgLabel, lblR, _rField, lblG, _gField, lblB, _bField, toggleBtn, _swatchLabel,
            btnRevert, btnCancel, btnSave);

        _rolePanel.Refresh();
        _themePanel.Refresh();
    }

    // ── Internal panels ───────────────────────────────────────────────────

    private sealed class ThemeListPanel : View
    {
        private readonly ThemeEditorDialog _dlg;
        private int _scrollTop;

        // Buttons
        private readonly Button _btnNew, _btnDupe, _btnRen, _btnDel, _btnImport, _btnExport;

        public ThemeListPanel(ThemeEditorDialog dlg)
        {
            _dlg     = dlg;
            CanFocus = true;

            _btnNew    = new Button { Text = "New",    X = 0,                       Y = Pos.AnchorEnd(2) };
            _btnDupe   = new Button { Text = "Dupe",   X = Pos.Right(_btnNew) + 1,  Y = Pos.AnchorEnd(2) };
            _btnRen    = new Button { Text = "Ren",    X = Pos.Right(_btnDupe) + 1, Y = Pos.AnchorEnd(2) };
            _btnDel    = new Button { Text = "Del",    X = Pos.Right(_btnRen) + 1,  Y = Pos.AnchorEnd(2) };
            _btnImport = new Button { Text = "Import", X = 0,                       Y = Pos.AnchorEnd(1) };
            _btnExport = new Button { Text = "Export", X = Pos.Right(_btnImport)+1, Y = Pos.AnchorEnd(1) };

            _btnNew.Accepting    += (_, _) => _dlg.DoNew();
            _btnDupe.Accepting   += (_, _) => _dlg.DoDupe();
            _btnRen.Accepting    += (_, _) => _dlg.DoRename();
            _btnDel.Accepting    += (_, _) => _dlg.DoDelete();
            _btnImport.Accepting += (_, _) => _dlg.DoImport();
            _btnExport.Accepting += (_, _) => _dlg.DoExport();

            Add(_btnNew, _btnDupe, _btnRen, _btnDel, _btnImport, _btnExport);
        }

        public void Refresh()
        {
            _btnDel.Enabled = !_dlg.IsBuiltIn(_dlg._working);
            _btnRen.Enabled = !_dlg.IsBuiltIn(_dlg._working);
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? _)
        {
            var h = Viewport.Height - 2;  // leave rows for buttons
            for (var row = 0; row < h; row++)
            {
                var i = _scrollTop + row;
                var isCurrent = i == _dlg._selectedTheme;
                var attr = isCurrent
                    ? new TAttr(new Color(0, 0, 0, 255), new Color(0x26, 0x4F, 0x78, 255))
                    : new TAttr(new Color(0xD4, 0xD4, 0xD4, 255), new Color(0x1E, 0x1E, 0x1E, 255));
                SetAttribute(attr);

                if (i < _dlg._themes.Count)
                {
                    var activeMarker = string.Equals(_dlg._themes[i].Name, _dlg._originalActiveName,
                        StringComparison.OrdinalIgnoreCase) ? "●" : " ";
                    var label = $"{activeMarker} {_dlg._themes[i].Name}";
                    if (label.Length > Viewport.Width) label = label[..Viewport.Width];
                    AddStr(0, row, label.PadRight(Viewport.Width));
                }
                else
                {
                    AddStr(0, row, new string(' ', Viewport.Width));
                }
            }
            return true;
        }

        protected override bool OnKeyDown(Key key)
        {
            var changed = false;
            if (key.KeyCode == KeyCode.CursorUp && _dlg._selectedTheme > 0)
            { _dlg._selectedTheme--; changed = true; }
            else if (key.KeyCode == KeyCode.CursorDown && _dlg._selectedTheme < _dlg._themes.Count - 1)
            { _dlg._selectedTheme++; changed = true; }

            if (changed)
            {
                var listH = Viewport.Height - 2;
                if (_dlg._selectedTheme < _scrollTop) _scrollTop = _dlg._selectedTheme;
                if (_dlg._selectedTheme >= _scrollTop + listH) _scrollTop = _dlg._selectedTheme - listH + 1;
                _dlg.OnThemeSelectionChanged();
                Refresh();
                key.Handled = true;
                return true;
            }
            return base.OnKeyDown(key);
        }
    }

    private sealed class RoleListPanel : View
    {
        private readonly ThemeEditorDialog _dlg;
        private int _scrollTop;

        public RoleListPanel(ThemeEditorDialog dlg) { _dlg = dlg; CanFocus = true; }

        public void Refresh() => SetNeedsDraw();

        protected override bool OnDrawingContent(DrawContext? _)
        {
            var h = Viewport.Height;
            for (var row = 0; row < h; row++)
            {
                var i = _scrollTop + row;
                if (i >= _dlg._roles.Count)
                {
                    SetAttribute(new TAttr(new Color(0xD4, 0xD4, 0xD4, 255), new Color(0x1E, 0x1E, 0x1E, 255)));
                    AddStr(0, row, new string(' ', Viewport.Width));
                    continue;
                }

                var role      = _dlg._roles[i];
                var isSep     = role.Get is null;
                var isCurrent = !isSep && i == _dlg._selectedRole;

                var attr = isCurrent
                    ? new TAttr(new Color(0, 0, 0, 255), new Color(0x26, 0x4F, 0x78, 255))
                    : new TAttr(new Color(0xD4, 0xD4, 0xD4, 255), new Color(0x1E, 0x1E, 0x1E, 255));
                SetAttribute(attr);

                if (isSep)
                {
                    var sep = role.Label.PadRight(Viewport.Width);
                    AddStr(0, row, sep.Length > Viewport.Width ? sep[..Viewport.Width] : sep);
                }
                else
                {
                    var pair = role.Get!(_dlg._working);
                    var line = $"{role.Label,-14} #{pair.Foreground.R:X2}{pair.Foreground.G:X2}{pair.Foreground.B:X2}  #{pair.Background.R:X2}{pair.Background.G:X2}{pair.Background.B:X2}";
                    if (line.Length > Viewport.Width) line = line[..Viewport.Width];
                    AddStr(0, row, line.PadRight(Viewport.Width));
                }
            }
            return true;
        }

        protected override bool OnKeyDown(Key key)
        {
            var changed = false;
            var count   = _dlg._roles.Count;

            if (key.KeyCode == KeyCode.CursorUp)
            {
                var next = _dlg._selectedRole - 1;
                while (next >= 0 && _dlg._roles[next].Get is null) next--;
                if (next >= 0) { _dlg._selectedRole = next; changed = true; }
            }
            else if (key.KeyCode == KeyCode.CursorDown)
            {
                var next = _dlg._selectedRole + 1;
                while (next < count && _dlg._roles[next].Get is null) next++;
                if (next < count) { _dlg._selectedRole = next; changed = true; }
            }

            if (changed)
            {
                var listH = Viewport.Height;
                if (_dlg._selectedRole < _scrollTop) _scrollTop = _dlg._selectedRole;
                if (_dlg._selectedRole >= _scrollTop + listH) _scrollTop = _dlg._selectedRole - listH + 1;
                _dlg.LoadRgbEditor();
                SetNeedsDraw();
                key.Handled = true;
                return true;
            }
            return base.OnKeyDown(key);
        }
    }

    // ── Theme selection ───────────────────────────────────────────────────

    private void OnThemeSelectionChanged()
    {
        _rolePanel.Refresh();
        _selectedRole = 0;
        LoadRgbEditor();
        _registry.SetTheme(_working);
    }

    private bool IsBuiltIn(UserTheme t)
        => string.Equals(t.Name, "VS Code Dark+", StringComparison.OrdinalIgnoreCase);

    // ── RGB editor ────────────────────────────────────────────────────────

    internal void LoadRgbEditor()
    {
        var role = _roles[_selectedRole];
        if (role.Get is null) { ClearRgbEditor(); return; }

        var pair = role.Get(_working);
        var c    = _editingFg ? pair.Foreground : pair.Background;

        _suppressRgbChange = true;
        _rField.Text = c.R.ToString();
        _gField.Text = c.G.ToString();
        _bField.Text = c.B.ToString();
        _suppressRgbChange = false;

        UpdateSwatch(pair);
    }

    private void ClearRgbEditor()
    {
        _suppressRgbChange = true;
        _rField.Text = ""; _gField.Text = ""; _bField.Text = "";
        _suppressRgbChange = false;
        _swatchLabel.Text = "     ";
    }

    private void OnRgbChanged()
    {
        if (_suppressRgbChange) return;
        var role = _roles[_selectedRole];
        if (role.Get is null || role.Set is null) return;

        if (!TryParseByte(_rField.Text?.ToString(), out var r)) return;
        if (!TryParseByte(_gField.Text?.ToString(), out var g)) return;
        if (!TryParseByte(_bField.Text?.ToString(), out var b)) return;

        var old     = role.Get(_working);
        var updated = _editingFg
            ? new ColorPair(new Rgb(r, g, b), old.Background)
            : new ColorPair(old.Foreground,   new Rgb(r, g, b));

        role.Set(_working, updated);
        UpdateSwatch(updated);
        _rolePanel.Refresh();
        _registry.SetTheme(_working);
    }

    private void UpdateSwatch(ColorPair pair)
    {
        var c = _editingFg ? pair.Foreground : pair.Background;
        _swatchLabel.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static bool TryParseByte(string? s, out byte value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (!int.TryParse(s.Trim(), out var n) || n < 0 || n > 255) return false;
        value = (byte)n;
        return true;
    }

    // ── Button actions ────────────────────────────────────────────────────

    internal void DoNew()
    {
        var name = PromptName("New Theme", "");
        if (name is null) return;
        _themes.Add(_working.Clone(name));
        _selectedTheme = _themes.Count - 1;
        OnThemeSelectionChanged();
        _themePanel.Refresh();
    }

    internal void DoDupe()
    {
        var name = PromptName("Duplicate Theme", "");
        if (name is null) return;
        _themes.Add(_working.Clone(name));
        _selectedTheme = _themes.Count - 1;
        OnThemeSelectionChanged();
        _themePanel.Refresh();
    }

    internal void DoRename()
    {
        if (IsBuiltIn(_working)) return;
        var name = PromptName("Rename Theme", _working.Name, excludeTheme: _working);
        if (name is null) return;
        _working.Name = name;
        _themePanel.Refresh();
    }

    internal void DoDelete()
    {
        if (IsBuiltIn(_working)) return;
        var choice = MessageBox.Query(TGuiApp.Instance!, "Delete Theme",
            $"Delete '{_working.Name}'? This cannot be undone.", "Delete", "Cancel");
        if (choice != 0) return;

        try { _themeSvc.Delete(_working.Name); }
        catch (Exception ex) { ShowError(ex.Message); return; }

        _themes.RemoveAt(_selectedTheme);
        if (_themes.Count == 0) _themes.Add(UserTheme.FromDefaults());
        _selectedTheme = Math.Max(0, _selectedTheme - 1);
        OnThemeSelectionChanged();
        _themePanel.Refresh();
    }

    internal void DoImport()
    {
        var dlg = new OpenDialog { MustExist = true, OpenMode = OpenMode.File };
        TGuiApp.Instance!.Run(dlg);
        if (dlg.Canceled || dlg.FilePaths.Count == 0) return;

        var path = dlg.FilePaths[0].ToString()!;
        try
        {
            var imported = _themeSvc.Import(path);
            _themes.Add(imported);
            _selectedTheme = _themes.Count - 1;
            OnThemeSelectionChanged();
            _themePanel.Refresh();
        }
        catch (ThemeImportException ex) when (ex.Message.Contains("already exists"))
        {
            var newName = PromptName("Import — Name Conflict\n" + ex.Message, "");
            if (newName is null) return;
            ImportWithName(path, newName);
        }
        catch (ThemeImportException ex) { ShowError(ex.Message); }
    }

    private void ImportWithName(string sourcePath, string newName)
    {
        var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            var json    = File.ReadAllText(sourcePath);
            var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            var dict    = new Dictionary<string, object?>();
            foreach (var prop in element.EnumerateObject())
                dict[prop.Name] = (object)prop.Value;
            dict["name"] = (object)newName;
            File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(dict));

            var imported = _themeSvc.Import(tmp);
            _themes.Add(imported);
            _selectedTheme = _themes.Count - 1;
            OnThemeSelectionChanged();
            _themePanel.Refresh();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    internal void DoExport()
    {
        var dlg = new SaveDialog();
        TGuiApp.Instance!.Run(dlg);
        if (dlg.FileName is null) return;
        try { _themeSvc.Export(_working, dlg.FileName.ToString()!); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void DoRevert()
    {
        var fresh = _themeSvc.LoadByName(_working.Name) ?? UserTheme.FromDefaults(_working.Name);
        _themes[_selectedTheme] = fresh;
        OnThemeSelectionChanged();
        _themePanel.Refresh();
    }

    private void DoSave()
    {
        try
        {
            _themeSvc.Save(_working);
            // Live preview keeps the registry pointing at _working, so saving always makes
            // the current working theme the persisted active theme.
            _settingsSvc.SaveActiveTheme(_working.Name);
            TGuiApp.Instance!.RequestStop(this);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void DoCancel()
    {
        var original = _themes.FirstOrDefault(t =>
            string.Equals(t.Name, _originalActiveName, StringComparison.OrdinalIgnoreCase));
        _registry.SetTheme(original as IColorTheme ?? new DefaultDarkTheme());
        TGuiApp.Instance!.RequestStop(this);
    }

    // ── Name prompt ───────────────────────────────────────────────────────

    // excludeTheme: for Rename, exclude the theme being renamed from the uniqueness check.
    // For New/Dupe pass null — the new theme doesn't exist yet, check against all.
    private string? PromptName(string title, string prefill, UserTheme? excludeTheme = null)
    {
        string? result = null;

        var dlg       = new Dialog { Title = title, Width = 46, Height = 8 };
        var errLabel  = new Label  { X = 1, Y = 0, Width = Dim.Fill() - Dim.Absolute(2), Text = "" };
        var nameField = new TextField { X = 1, Y = 1, Width = Dim.Fill() - Dim.Absolute(2), Text = prefill };
        var btnOk     = new Button { Text = "OK",     IsDefault = true, X = Pos.AnchorEnd(14), Y = 3 };
        var btnCx     = new Button { Text = "Cancel",                   X = Pos.AnchorEnd(24), Y = 3 };

        btnOk.Accepting += (_, _) =>
        {
            var name = nameField.Text?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { errLabel.Text = "Name cannot be empty."; return; }
            if (_themes.Any(t => !ReferenceEquals(t, excludeTheme) &&
                    string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            { errLabel.Text = $"'{name}' already exists."; return; }
            result = name;
            TGuiApp.Instance!.RequestStop(dlg);
        };
        btnCx.Accepting += (_, _) => TGuiApp.Instance!.RequestStop(dlg);

        dlg.Add(errLabel, nameField, btnOk, btnCx);
        TGuiApp.Instance!.Run(dlg);
        return result;
    }

    private static void ShowError(string message)
        => MessageBox.Query(TGuiApp.Instance!, "Error", message, "OK");
}
