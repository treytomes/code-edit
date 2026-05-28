using CodeEdit.Application;
using CodeEdit.Application.Events;
using CodeEdit.Application.Ports;
using CodeEdit.Domain;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace CodeEdit.Presentation.Views;

public sealed class EditorView : View
{
    private readonly IEventBus         _eventBus;
    private readonly ThemeRegistry     _themeRegistry;
    private readonly ISyntaxDetector   _syntaxDetector;
    private readonly IClipboardService _clipboardService;
    private readonly StatusBarView     _statusBar;
    private readonly IApplication      _app;
    private readonly EditorSettings    _settings;
    private ISyntaxProvider?           _syntaxProvider;

    // Token cache: one entry per logical line; null = invalid / not yet computed
    private record struct CachedLine(int StartState, IReadOnlyList<SyntaxToken> Tokens, int EndState);
    private CachedLine?[] _tokenCache = [];
    private int           _invalidateFrom = int.MaxValue;

    // Scroll state — logical rows (wrap off) or visual rows (wrap on)
    private int _scrollRow;
    private int _scrollVisualRow;

    private int _wantColumn;

    // Word wrap
    private bool            _wordWrap;
    private List<VisualRow>? _wrapLayout;
    private int              _wrapLayoutWidth;

    // Mouse drag state
    private CursorPosition? _dragAnchor;

    // Search highlights
    private IReadOnlyList<CursorPosition> _searchMatches = [];
    private int _searchCurrentIndex = -1;
    private int _searchQueryLength  = 0;

    private const int ScrollLines = 3;
    private const int GutterWidth = 5;

    // Maps a logical line segment to a screen row in wrap mode
    private readonly record struct VisualRow(int LogicalLine, int StartCol, int EndCol);

    public bool WordWrap => _wordWrap;

    public event EventHandler? WordWrapChanged;
    public event EventHandler? NewRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? ReplaceRequested;
    public event EventHandler? FindNextRequested;
    public event EventHandler? FindPrevRequested;
    public event EventHandler? KeyboardShortcutsRequested;

    public EditorView(
        IEventBus         eventBus,
        ThemeRegistry     themeRegistry,
        ISyntaxDetector   syntaxDetector,
        IClipboardService clipboardService,
        StatusBarView     statusBar,
        IApplication      app,
        EditorSettings    settings)
    {
        _eventBus         = eventBus;
        _themeRegistry    = themeRegistry;
        _syntaxDetector   = syntaxDetector;
        _clipboardService = clipboardService;
        _statusBar        = statusBar;
        _app              = app;
        _settings         = settings;

        CanFocus              = true;
        MousePositionTracking = true;

        _eventBus.EventExecuted  += OnBufferChanged;
        _eventBus.EventUndone    += OnBufferChanged;
        _eventBus.EventRedone    += OnBufferChanged;
        _eventBus.BufferMutated  += OnBufferMutated;
        _themeRegistry.ThemeChanged += OnThemeChanged;
    }

    public void SetBuffer(IMutableTextBuffer buffer)
    {
        var firstLine   = buffer.LineCount > 0 ? buffer.GetLine(0) : null;
        _syntaxProvider = _syntaxDetector.Detect(buffer.FilePath, firstLine);
        _tokenCache     = new CachedLine?[buffer.LineCount];
        _invalidateFrom = 0;
        _scrollRow      = 0;
        _scrollVisualRow = 0;
        _wantColumn     = 0;
        _wrapLayout     = null;
        SetNeedsDraw();
    }

    public void UpdateSearchResults(IReadOnlyList<CursorPosition> matches, int currentIndex, int queryLength)
    {
        _searchMatches      = matches;
        _searchCurrentIndex = currentIndex;
        _searchQueryLength  = queryLength;
        SetNeedsDraw();
    }

    public void ToggleWordWrap()
    {
        _wordWrap   = !_wordWrap;
        _wrapLayout = null;
        ScrollToCursor();
        WordWrapChanged?.Invoke(this, EventArgs.Empty);
        SetNeedsDraw();
    }

    // ── Drawing ────────────────────────────────────────────────────────────

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var theme         = _themeRegistry.Active;
        var normalAttr    = ColorPairMapper.ToAttribute(theme.Normal);
        var lineNumAttr   = ColorPairMapper.ToAttribute(theme.LineNumber);
        var selectionAttr = ColorPairMapper.ToAttribute(theme.Selection);

        ITextBuffer? buffer = null;
        try { buffer = _eventBus.Buffer; }
        catch (InvalidOperationException) { }

        var height = Viewport.Height;
        var width  = Viewport.Width;

        if (_wordWrap && buffer is not null)
        {
            var layout = RebuildWrapLayout(buffer, width);

            for (var r = 0; r < height; r++)
            {
                var vrIdx = _scrollVisualRow + r;

                if (vrIdx >= layout.Count)
                {
                    SetAttribute(normalAttr);
                    AddStr(0, r, new string(' ', width));
                    continue;
                }

                var vr       = layout[vrIdx];
                var lineText = buffer.GetLine(vr.LogicalLine);
                var tokens   = GetTokens(buffer, vr.LogicalLine);

                // Gutter: line number on first segment, blank on continuations
                SetAttribute(lineNumAttr);
                var gutterText = vr.StartCol == 0
                    ? (vr.LogicalLine + 1).ToString().PadLeft(4) + " "
                    : "     ";
                AddStr(0, r, gutterText);

                // Text segment [StartCol..EndCol)
                DrawSegment(r, vr.LogicalLine, vr.StartCol, vr.EndCol,
                    lineText, tokens, buffer, width, normalAttr, selectionAttr, theme);

                // Cursor overlay
                if (vr.LogicalLine == buffer.Cursor.Line
                    && buffer.Cursor.Column >= vr.StartCol
                    && (buffer.Cursor.Column < vr.EndCol || vr.EndCol == lineText.Length))
                {
                    var cursorScreenCol = GutterWidth + VisualOffsetInSegment(lineText, vr.StartCol, buffer.Cursor.Column);
                    if (cursorScreenCol < width)
                    {
                        var ch = buffer.Cursor.Column < lineText.Length
                            ? lineText[buffer.Cursor.Column].ToString()
                            : " ";
                        if (ch == "\t") ch = " ";
                        SetAttribute(selectionAttr);
                        AddStr(cursorScreenCol, r, ch);
                    }
                }
            }
        }
        else
        {
            for (var r = 0; r < height; r++)
            {
                var lineIndex = _scrollRow + r;

                if (buffer is null || lineIndex >= buffer.LineCount)
                {
                    SetAttribute(normalAttr);
                    AddStr(0, r, new string(' ', width));
                    continue;
                }

                var lineText = buffer.GetLine(lineIndex);
                var tokens   = GetTokens(buffer, lineIndex);

                SetAttribute(lineNumAttr);
                AddStr(0, r, (lineIndex + 1).ToString().PadLeft(4) + " ");

                DrawSegment(r, lineIndex, 0, lineText.Length,
                    lineText, tokens, buffer, width, normalAttr, selectionAttr, theme);

                // Cursor overlay
                if (lineIndex == buffer.Cursor.Line)
                {
                    var cursorScreenCol = GutterWidth + VisualOffsetInSegment(lineText, 0, buffer.Cursor.Column);
                    if (cursorScreenCol < width)
                    {
                        var ch = buffer.Cursor.Column < lineText.Length
                            ? lineText[buffer.Cursor.Column].ToString()
                            : " ";
                        if (ch == "\t") ch = " ";
                        SetAttribute(selectionAttr);
                        AddStr(cursorScreenCol, r, ch);
                    }
                }
            }
        }

        return true;
    }

    // Renders the text segment [startCol..endCol) of logicalLine onto screen row r.
    private void DrawSegment(
        int r, int logicalLine, int startCol, int endCol,
        string lineText, IReadOnlyList<SyntaxToken> tokens,
        ITextBuffer buffer, int width,
        Terminal.Gui.Drawing.Attribute normalAttr,
        Terminal.Gui.Drawing.Attribute selectionAttr,
        IColorTheme theme)
    {
        var maxScreenCols  = width - GutterWidth;
        var tabWidth       = _settings.TabWidth;
        var screenCol      = 0;       // visual column within this segment (for tab stops)
        var runScreenCol   = GutterWidth;
        var runSb          = new System.Text.StringBuilder();
        var runAttr        = normalAttr;
        var searchMatchAttr = ColorPairMapper.ToAttribute(theme.SearchMatch);

        void FlushRun()
        {
            if (runSb.Length == 0) return;
            SetAttribute(runAttr);
            AddStr(runScreenCol, r, runSb.ToString());
            runSb.Clear();
        }

        for (var logicalCol = startCol; logicalCol < endCol && screenCol < maxScreenCols; logicalCol++)
        {
            var ch         = lineText[logicalCol];
            var tabExpand  = ch == '\t' ? tabWidth - (screenCol % tabWidth) : 1;
            var tokenType  = TokenTypeAt(tokens, logicalLine, logicalCol);
            var isSelected = buffer.Selection.HasValue
                             && PositionInSelection(logicalLine, logicalCol, buffer.Selection.Value);
            var matchIdx   = SearchMatchIndexAt(logicalLine, logicalCol);
            var attr = isSelected
                ? selectionAttr
                : matchIdx >= 0
                    ? (matchIdx == _searchCurrentIndex ? selectionAttr : searchMatchAttr)
                    : ColorPairMapper.ToAttribute(theme.ForToken(tokenType));

            if (runSb.Length > 0 && attr != runAttr)
                FlushRun();

            if (runSb.Length == 0)
            {
                runAttr      = attr;
                runScreenCol = GutterWidth + screenCol;
            }

            var spaces = tabExpand > 1 ? new string(' ', Math.Min(tabExpand, maxScreenCols - screenCol)) : null;
            runSb.Append(spaces ?? ch.ToString());
            screenCol += tabExpand;
        }

        FlushRun();

        var fillStart = GutterWidth + screenCol;
        if (fillStart < width)
        {
            SetAttribute(normalAttr);
            AddStr(fillStart, r, new string(' ', width - fillStart));
        }
    }

    // ── Mouse ──────────────────────────────────────────────────────────────

    protected override bool OnMouseEvent(Mouse mouse)
    {
        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; }
        catch (InvalidOperationException) { return false; }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            ScrollBy(-ScrollLines, buffer);
            mouse.Handled = true;
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            ScrollBy(ScrollLines, buffer);
            mouse.Handled = true;
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed)
            && !mouse.Flags.HasFlag(MouseFlags.PositionReport)
            && mouse.Position.HasValue)
        {
            var pos = ScreenToBuffer(mouse.Position.Value, buffer);
            _dragAnchor = pos;
            _wantColumn = VisualColOf(pos, buffer);
            _eventBus.Publish(new SetSelectionEvent(
                null, pos,
                buffer.Selection, buffer.Cursor));
            _app.Mouse.GrabMouse(this);
            mouse.Handled = true;
            return true;
        }

        if (_dragAnchor.HasValue
            && mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed | MouseFlags.PositionReport)
            && mouse.Position.HasValue)
        {
            var active = ScreenToBuffer(mouse.Position.Value, buffer);
            if (active != _dragAnchor.Value)
            {
                _eventBus.Publish(new SetSelectionEvent(
                    new Selection(_dragAnchor.Value, active), active,
                    buffer.Selection, buffer.Cursor));
            }
            mouse.Handled = true;
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased))
        {
            _app.Mouse.UngrabMouse();
            _dragAnchor = null;
            mouse.Handled = true;
            return true;
        }

        return false;
    }

    // ── Keyboard ───────────────────────────────────────────────────────────

    protected override bool OnKeyDown(Key key)
    {
        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; }
        catch (InvalidOperationException) { return false; }

        var pos = buffer.Cursor;

        // ── Word wrap toggle ───────────────────────────────────────────────

        if (key.KeyCode == (KeyCode.AltMask | KeyCode.Z))
        {
            ToggleWordWrap();
            key.Handled = true;
            return true;
        }

        // ── Help ───────────────────────────────────────────────────────────

        if (key.KeyCode == KeyCode.F1)
        {
            KeyboardShortcutsRequested?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
            return true;
        }

        // ── File shortcuts ─────────────────────────────────────────────────

        // Swallow Escape — Terminal.Gui binds it to Command.Quit at the app level
        if (key.KeyCode == KeyCode.Esc)
        {
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.N))
        {
            NewRequested?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.O))
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.S))
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.F)
            || key.KeyCode == KeyCode.F3
            || key.KeyCode == (KeyCode.CtrlMask | KeyCode.F3))
        {
            FindNextRequested?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.F3)
            || key.KeyCode == (KeyCode.CtrlMask | KeyCode.ShiftMask | KeyCode.F3))
        {
            FindPrevRequested?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.H))
        {
            ReplaceRequested?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
            return true;
        }

        // ── Shift selection moves ──────────────────────────────────────────

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.CursorLeft))
        {
            var newActive = MoveLeft(pos, buffer);
            _wantColumn = VisualColOf(newActive, buffer);
            PublishSelectionMove(buffer, newActive);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.CursorRight))
        {
            var newActive = MoveRight(pos, buffer);
            _wantColumn = VisualColOf(newActive, buffer);
            PublishSelectionMove(buffer, newActive);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.CursorUp))
        {
            var newActive = _wordWrap
                ? MoveUpWrap(pos, _wantColumn, GetWrapLayout(buffer))
                : MoveUp(pos, _wantColumn, buffer);
            PublishSelectionMove(buffer, newActive);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.CursorDown))
        {
            var newActive = _wordWrap
                ? MoveDownWrap(pos, _wantColumn, GetWrapLayout(buffer))
                : MoveDown(pos, _wantColumn, buffer);
            PublishSelectionMove(buffer, newActive);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.Home))
        {
            var newActive = new CursorPosition(pos.Line, 0);
            _wantColumn = 0;
            PublishSelectionMove(buffer, newActive);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.End))
        {
            var newActive = new CursorPosition(pos.Line, buffer.GetLine(pos.Line).Length);
            _wantColumn = VisualColOf(newActive, buffer);
            PublishSelectionMove(buffer, newActive);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.CtrlMask | KeyCode.CursorLeft))
        {
            var newActive = WordBoundaryLeft(pos, buffer);
            _wantColumn = VisualColOf(newActive, buffer);
            PublishSelectionMove(buffer, newActive);
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.CtrlMask | KeyCode.CursorRight))
        {
            var newActive = WordBoundaryRight(pos, buffer);
            _wantColumn = VisualColOf(newActive, buffer);
            PublishSelectionMove(buffer, newActive);
            key.Handled = true;
            return true;
        }

        // ── Ctrl+A (select all) ───────────────────────────────────────────

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.A))
        {
            var lastLine = buffer.LineCount - 1;
            var lastCol  = buffer.GetLine(lastLine).Length;
            var anchor   = new CursorPosition(0, 0);
            var active   = new CursorPosition(lastLine, lastCol);
            _eventBus.Publish(new SetSelectionEvent(
                new Selection(anchor, active), active,
                buffer.Selection, buffer.Cursor));
            key.Handled = true;
            return true;
        }

        // ── Tab / Shift+Tab ────────────────────────────────────────────────

        if (key.KeyCode == KeyCode.Tab)
        {
            if (!buffer.Selection.HasValue)
            {
                // No selection: insert indent at cursor (like typing characters)
                _eventBus.Publish(new InsertTextEvent(pos, _settings.IndentString));
            }
            else
            {
                var lines = TouchedLines(buffer);
                var ev    = new IndentEvent(lines, _settings.IndentString, dedent: false);
                _eventBus.Publish(ev);
                AdjustSelectionAfterIndent(buffer, ev);
            }
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.Tab))
        {
            var lines = TouchedLines(buffer, dedent: true);
            var ev    = new IndentEvent(lines, _settings.IndentString, dedent: true);
            _eventBus.Publish(ev);
            AdjustSelectionAfterIndent(buffer, ev);
            key.Handled = true;
            return true;
        }

        // ── Ctrl+Left / Ctrl+Right ─────────────────────────────────────────

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.CursorLeft))
        {
            var from = buffer.Selection.HasValue
                ? CopyCommand.Normalise(buffer.Selection.Value).Item1
                : pos;
            var newPos = WordBoundaryLeft(from, buffer);
            _wantColumn = VisualColOf(newPos, buffer);
            _eventBus.Publish(new SetSelectionEvent(null, newPos, buffer.Selection, buffer.Cursor));
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.CursorRight))
        {
            var from = buffer.Selection.HasValue
                ? CopyCommand.Normalise(buffer.Selection.Value).Item2
                : pos;
            var newPos = WordBoundaryRight(from, buffer);
            _wantColumn = VisualColOf(newPos, buffer);
            _eventBus.Publish(new SetSelectionEvent(null, newPos, buffer.Selection, buffer.Cursor));
            key.Handled = true;
            return true;
        }

        // ── Ctrl+Up / Ctrl+Down ────────────────────────────────────────────

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.CursorUp))
        {
            if (_wordWrap)
            {
                var layout = GetWrapLayout(buffer);
                _scrollVisualRow = Math.Clamp(_scrollVisualRow - 1, 0, Math.Max(0, layout.Count - 1));
            }
            else
            {
                _scrollRow = Math.Clamp(_scrollRow - 1, 0, Math.Max(0, buffer.LineCount - 1));
            }
            SetNeedsDraw();
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.CursorDown))
        {
            if (_wordWrap)
            {
                var layout = GetWrapLayout(buffer);
                _scrollVisualRow = Math.Clamp(_scrollVisualRow + 1, 0, Math.Max(0, layout.Count - 1));
            }
            else
            {
                _scrollRow = Math.Clamp(_scrollRow + 1, 0, Math.Max(0, buffer.LineCount - 1));
            }
            SetNeedsDraw();
            key.Handled = true;
            return true;
        }

        // ── Non-shift arrow keys with selection: collapse ──────────────────

        if (key.KeyCode == KeyCode.CursorLeft || key.KeyCode == KeyCode.CursorUp)
        {
            if (buffer.Selection.HasValue)
            {
                var (start, _) = CopyCommand.Normalise(buffer.Selection.Value);
                _wantColumn = VisualColOf(start, buffer);
                _eventBus.Publish(new SetSelectionEvent(null, start, buffer.Selection, buffer.Cursor));
                key.Handled = true;
                return true;
            }
        }

        if (key.KeyCode == KeyCode.CursorRight || key.KeyCode == KeyCode.CursorDown)
        {
            if (buffer.Selection.HasValue)
            {
                var (_, end) = CopyCommand.Normalise(buffer.Selection.Value);
                _wantColumn = VisualColOf(end, buffer);
                _eventBus.Publish(new SetSelectionEvent(null, end, buffer.Selection, buffer.Cursor));
                key.Handled = true;
                return true;
            }
        }

        // ── Plain cursor moves (no selection) ─────────────────────────────

        if (key.KeyCode == KeyCode.CursorUp)
        {
            var newPos = _wordWrap
                ? MoveUpWrap(pos, _wantColumn, GetWrapLayout(buffer))
                : MoveUp(pos, _wantColumn, buffer);
            _eventBus.Publish(new SetCursorEvent(newPos, pos));
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == KeyCode.CursorDown)
        {
            var newPos = _wordWrap
                ? MoveDownWrap(pos, _wantColumn, GetWrapLayout(buffer))
                : MoveDown(pos, _wantColumn, buffer);
            _eventBus.Publish(new SetCursorEvent(newPos, pos));
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == KeyCode.CursorLeft)
        {
            var newPos = MoveLeft(pos, buffer);
            _wantColumn = VisualColOf(newPos, buffer);
            _eventBus.Publish(new SetCursorEvent(newPos, pos));
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == KeyCode.CursorRight)
        {
            var newPos = MoveRight(pos, buffer);
            _wantColumn = VisualColOf(newPos, buffer);
            _eventBus.Publish(new SetCursorEvent(newPos, pos));
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == KeyCode.Enter)
        {
            _wantColumn = 0;
            if (buffer.Selection.HasValue)
            {
                var (start, end, selText) = SelectionInfo(buffer);
                _eventBus.Publish(new InsertTextEvent(start, "\n", new TextRange(start, end), selText));
            }
            else
            {
                _eventBus.Publish(new InsertTextEvent(pos, "\n"));
            }
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == KeyCode.Backspace)
        {
            if (buffer.Selection.HasValue)
            {
                var (start, end, selText) = SelectionInfo(buffer);
                _wantColumn = VisualColOf(start, buffer);
                _eventBus.Publish(new DeleteEvent(new TextRange(start, end), selText));
            }
            else
            {
                if (pos.Line == 0 && pos.Column == 0) { key.Handled = true; return true; }
                var (range, text) = BackspaceRange(pos, buffer);
                _wantColumn = VisualColOf(range.Start, buffer);
                _eventBus.Publish(new DeleteEvent(range, text));
            }
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == KeyCode.Delete)
        {
            if (buffer.Selection.HasValue)
            {
                var (start, end, selText) = SelectionInfo(buffer);
                _wantColumn = VisualColOf(start, buffer);
                _eventBus.Publish(new DeleteEvent(new TextRange(start, end), selText));
            }
            else
            {
                var del = DeleteRange(pos, buffer);
                if (del.HasValue)
                    _eventBus.Publish(new DeleteEvent(del.Value.range, del.Value.text));
            }
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.Z))
        {
            if (_eventBus.CanUndo) _eventBus.Undo();
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.Y)
            || key.KeyCode == (KeyCode.CtrlMask | KeyCode.ShiftMask | KeyCode.Z))
        {
            if (_eventBus.CanRedo) _eventBus.Redo();
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.C))
        {
            if (!_clipboardService.IsSupported)
                _statusBar.SetMessage("Clipboard not available");
            else if (!CopyCommand.Execute(_eventBus.Buffer, _clipboardService))
                _statusBar.SetMessage("No text selected");
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.X))
        {
            if (!_clipboardService.IsSupported)
                _statusBar.SetMessage("Clipboard not available");
            else
                _eventBus.Publish(new CutEvent(_eventBus.Buffer, _clipboardService));
            key.Handled = true;
            return true;
        }

        if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.V))
        {
            if (!_clipboardService.IsSupported)
                _statusBar.SetMessage("Clipboard not available");
            else
                _eventBus.Publish(new PasteEvent(_eventBus.Buffer, _clipboardService));
            key.Handled = true;
            return true;
        }

        if (key.TryGetPrintableRune(out var rune))
        {
            var ch = rune.ToString();
            if (buffer.Selection.HasValue)
            {
                var (start, end, selText) = SelectionInfo(buffer);
                _eventBus.Publish(new InsertTextEvent(start, ch, new TextRange(start, end), selText));
            }
            else
            {
                _eventBus.Publish(new InsertTextEvent(pos, ch));
            }
            key.Handled = true;
            return true;
        }

        return false;
    }

    // ── Scrolling ──────────────────────────────────────────────────────────

    private void ScrollBy(int delta, ITextBuffer buffer)
    {
        if (_wordWrap)
        {
            var layout = GetWrapLayout(buffer);
            _scrollVisualRow = Math.Clamp(_scrollVisualRow + delta, 0, Math.Max(0, layout.Count - 1));
        }
        else
        {
            _scrollRow = Math.Clamp(_scrollRow + delta, 0, Math.Max(0, buffer.LineCount - 1));
        }
        SetNeedsDraw();
    }

    private void ScrollToCursor()
    {
        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; }
        catch (InvalidOperationException) { return; }

        var height = Viewport.Height;

        if (_wordWrap)
        {
            var layout = GetWrapLayout(buffer);
            var vrIdx  = FindVisualRowIndex(buffer.Cursor, layout);
            if (vrIdx < _scrollVisualRow)
                _scrollVisualRow = vrIdx;
            else if (height > 0 && vrIdx >= _scrollVisualRow + height)
                _scrollVisualRow = vrIdx - height + 1;
        }
        else
        {
            var cursorLine = buffer.Cursor.Line;
            if (cursorLine < _scrollRow)
                _scrollRow = cursorLine;
            else if (height > 0 && cursorLine >= _scrollRow + height)
                _scrollRow = cursorLine - height + 1;
        }
    }

    // ── Event handlers ─────────────────────────────────────────────────────

    private void OnBufferChanged(object? sender, BufferEventArgs e)
    {
        // Resize token cache if line count changed (insertions/deletions change line count)
        try
        {
            var lineCount = _eventBus.Buffer.LineCount;
            if (_tokenCache.Length != lineCount)
            {
                var newCache = new CachedLine?[lineCount];
                Array.Copy(_tokenCache, newCache, Math.Min(_tokenCache.Length, lineCount));
                _tokenCache = newCache;
            }
        }
        catch (InvalidOperationException) { }

        _wrapLayout = null;
        ScrollToCursor();
        SetNeedsDraw();
    }

    private void OnBufferMutated(object? sender, BufferMutatedEventArgs e)
    {
        _invalidateFrom = Math.Min(_invalidateFrom, e.FirstAffectedLine);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _invalidateFrom = 0;
        SetNeedsDraw();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _eventBus.EventExecuted  -= OnBufferChanged;
            _eventBus.EventUndone    -= OnBufferChanged;
            _eventBus.EventRedone    -= OnBufferChanged;
            _eventBus.BufferMutated  -= OnBufferMutated;
            _themeRegistry.ThemeChanged -= OnThemeChanged;
        }
        base.Dispose(disposing);
    }

    // ── Wrap layout ────────────────────────────────────────────────────────

    private List<VisualRow> GetWrapLayout(ITextBuffer buffer)
    {
        var width = Viewport.Width;
        if (_wrapLayout is null || _wrapLayoutWidth != width)
            _wrapLayout = RebuildWrapLayout(buffer, width);
        return _wrapLayout;
    }

    private List<VisualRow> RebuildWrapLayout(ITextBuffer buffer, int viewportWidth)
    {
        var textWidth = Math.Max(1, viewportWidth - GutterWidth);
        var layout    = new List<VisualRow>(buffer.LineCount);

        for (var l = 0; l < buffer.LineCount; l++)
        {
            var lineLen = buffer.GetLine(l).Length;
            if (lineLen == 0)
            {
                layout.Add(new VisualRow(l, 0, 0));
                continue;
            }
            var start = 0;
            while (start < lineLen)
            {
                var end = Math.Min(start + textWidth, lineLen);
                layout.Add(new VisualRow(l, start, end));
                start = end;
            }
        }

        _wrapLayout      = layout;
        _wrapLayoutWidth = viewportWidth;
        return layout;
    }

    // Returns the index of the visual row that contains cursor.
    // That's the last VR of cursor.Line where StartCol <= cursor.Column.
    private static int FindVisualRowIndex(CursorPosition cursor, List<VisualRow> layout)
    {
        var best = 0;
        for (var i = 0; i < layout.Count; i++)
        {
            var vr = layout[i];
            if (vr.LogicalLine < cursor.Line) continue;
            if (vr.LogicalLine > cursor.Line) break;
            if (vr.StartCol <= cursor.Column) best = i;
        }
        return best;
    }

    // Visual columns from startCol up to (not including) targetCol, expanding \t.
    private int VisualOffsetInSegment(string line, int startCol, int targetCol)
    {
        var tabWidth  = _settings.TabWidth;
        var visualCol = 0;
        for (var i = startCol; i < targetCol && i < line.Length; i++)
        {
            visualCol += line[i] == '\t'
                ? tabWidth - (visualCol % tabWidth)
                : 1;
        }
        return visualCol;
    }

    // Returns the visual column of cursor within its visual row, expanding \t.
    private int VisualColOf(CursorPosition pos, ITextBuffer buffer)
    {
        var tabWidth  = _settings.TabWidth;
        var line      = buffer.GetLine(pos.Line);
        var startCol  = 0;
        if (_wordWrap)
        {
            var layout = GetWrapLayout(buffer);
            var vrIdx  = FindVisualRowIndex(pos, layout);
            startCol   = layout[vrIdx].StartCol;
        }

        var visualCol = 0;
        for (var i = startCol; i < pos.Column && i < line.Length; i++)
        {
            visualCol += line[i] == '\t'
                ? tabWidth - (visualCol % tabWidth)
                : 1;
        }
        return visualCol;
    }

    // ── Navigation helpers ─────────────────────────────────────────────────

    private static CursorPosition MoveUp(CursorPosition pos, int wantCol, ITextBuffer buffer)
    {
        var newLine = Math.Max(0, pos.Line - 1);
        var newCol  = Math.Min(wantCol, buffer.GetLine(newLine).Length);
        return new CursorPosition(newLine, newCol);
    }

    private static CursorPosition MoveDown(CursorPosition pos, int wantCol, ITextBuffer buffer)
    {
        var newLine = Math.Min(buffer.LineCount - 1, pos.Line + 1);
        var newCol  = Math.Min(wantCol, buffer.GetLine(newLine).Length);
        return new CursorPosition(newLine, newCol);
    }

    // wantVisualCol is offset within the current visual row (not logical col).
    private static CursorPosition MoveUpWrap(CursorPosition pos, int wantVisualCol, List<VisualRow> layout)
    {
        var vrIdx = FindVisualRowIndex(pos, layout);
        if (vrIdx == 0) return pos;
        var prevVr  = layout[vrIdx - 1];
        var textLen = prevVr.EndCol - prevVr.StartCol;
        var col     = prevVr.StartCol + Math.Min(wantVisualCol, textLen);
        return new CursorPosition(prevVr.LogicalLine, col);
    }

    private static CursorPosition MoveDownWrap(CursorPosition pos, int wantVisualCol, List<VisualRow> layout)
    {
        var vrIdx = FindVisualRowIndex(pos, layout);
        if (vrIdx >= layout.Count - 1) return pos;
        var nextVr  = layout[vrIdx + 1];
        var textLen = nextVr.EndCol - nextVr.StartCol;
        var col     = nextVr.StartCol + Math.Min(wantVisualCol, textLen);
        return new CursorPosition(nextVr.LogicalLine, col);
    }

    private static CursorPosition MoveLeft(CursorPosition pos, ITextBuffer buffer)
    {
        if (pos.Column > 0)
            return new CursorPosition(pos.Line, pos.Column - 1);
        if (pos.Line > 0)
            return new CursorPosition(pos.Line - 1, buffer.GetLine(pos.Line - 1).Length);
        return pos;
    }

    private static CursorPosition MoveRight(CursorPosition pos, ITextBuffer buffer)
    {
        var lineLen = buffer.GetLine(pos.Line).Length;
        if (pos.Column < lineLen)
            return new CursorPosition(pos.Line, pos.Column + 1);
        if (pos.Line < buffer.LineCount - 1)
            return new CursorPosition(pos.Line + 1, 0);
        return pos;
    }

    private static CursorPosition WordBoundaryLeft(CursorPosition pos, ITextBuffer buffer)
    {
        var line = pos.Line;
        var col  = pos.Column;

        if (col == 0)
        {
            if (line == 0) return pos;
            line--;
            col = buffer.GetLine(line).Length;
        }

        var text = buffer.GetLine(line);
        col--;
        while (col > 0 && IsWordChar(text[col - 1]) == IsWordChar(text[col]))
            col--;
        return new CursorPosition(line, col);
    }

    private static CursorPosition WordBoundaryRight(CursorPosition pos, ITextBuffer buffer)
    {
        var line    = pos.Line;
        var col     = pos.Column;
        var text    = buffer.GetLine(line);
        var lineLen = text.Length;

        if (col >= lineLen)
        {
            if (line >= buffer.LineCount - 1) return pos;
            return new CursorPosition(line + 1, 0);
        }

        col++;
        while (col < lineLen && IsWordChar(text[col - 1]) == IsWordChar(text[col]))
            col++;
        return new CursorPosition(line, col);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // Returns the set of lines to indent/dedent for the current cursor/selection.
    // Single line or no selection → just the cursor line.
    // Multi-line selection → all touched lines.  For indent only: a line whose
    // only contribution is the active endpoint sitting at column 0 is excluded
    // (VS Code behaviour — avoids indenting a line you barely selected the start of).
    // For dedent the exclusion is not applied so that lines already at col 0
    // after a prior dedent are still included on the next Shift+Tab.
    private IReadOnlyList<int> TouchedLines(ITextBuffer buffer, bool dedent = false)
    {
        if (!buffer.Selection.HasValue)
            return [buffer.Cursor.Line];

        var (start, end) = CopyCommand.Normalise(buffer.Selection.Value);
        if (start.Line == end.Line)
            return [start.Line];

        if (dedent)
        {
            var all = new List<int>(end.Line - start.Line + 1);
            for (var l = start.Line; l <= end.Line; l++) all.Add(l);
            return all;
        }

        // Indent: apply col-0 exclusion on the active endpoint's line.
        var activeIsEnd = buffer.Selection.Value.Active.Line > buffer.Selection.Value.Anchor.Line
                          || (buffer.Selection.Value.Active.Line == buffer.Selection.Value.Anchor.Line
                              && buffer.Selection.Value.Active.Column >= buffer.Selection.Value.Anchor.Column);

        var lines = new List<int>();
        for (var l = start.Line; l <= end.Line; l++)
        {
            if (l == end.Line   && activeIsEnd  && buffer.Selection.Value.Active.Column == 0) continue;
            if (l == start.Line && !activeIsEnd && buffer.Selection.Value.Active.Column == 0) continue;
            lines.Add(l);
        }
        return lines.Count > 0 ? lines : [buffer.Cursor.Line];
    }

    // After publishing IndentEvent, shift cursor and selection endpoints to
    // follow the inserted/removed characters at column 0.
    private void AdjustSelectionAfterIndent(ITextBuffer buffer, IndentEvent ev)
    {
        var indentLen = ev.IndentString.Length;

        int AdjustedCol(int lineIndex, int col)
        {
            var idx = -1;
            for (var i = 0; i < ev.Lines.Count; i++)
                if (ev.Lines[i] == lineIndex) { idx = i; break; }
            if (idx < 0) return col;
            if (ev.IsDedent)
                return Math.Max(0, col - ev.RemovedLengths[idx]);
            return col + indentLen;
        }

        var newCursor = buffer.Cursor with { Column = AdjustedCol(buffer.Cursor.Line, buffer.Cursor.Column) };

        Selection? newSel = null;
        if (buffer.Selection.HasValue)
        {
            var sel = buffer.Selection.Value;
            newSel = new Selection(
                sel.Anchor with { Column = AdjustedCol(sel.Anchor.Line, sel.Anchor.Column) },
                sel.Active with { Column = AdjustedCol(sel.Active.Line, sel.Active.Column) });
        }

        _eventBus.Publish(new SetSelectionEvent(newSel, newCursor, buffer.Selection, buffer.Cursor));
        _wantColumn = VisualColOf(buffer.Cursor, buffer);
    }

    // ── Screen ↔ buffer mapping ────────────────────────────────────────────

    private CursorPosition ScreenToBuffer(System.Drawing.Point screenPos, ITextBuffer buffer)
    {
        if (_wordWrap)
        {
            var layout = GetWrapLayout(buffer);
            var vrIdx  = Math.Clamp(_scrollVisualRow + screenPos.Y, 0, layout.Count - 1);
            var vr     = layout[vrIdx];
            var col    = Math.Clamp(vr.StartCol + Math.Max(0, screenPos.X - GutterWidth),
                                    0, buffer.GetLine(vr.LogicalLine).Length);
            return new CursorPosition(vr.LogicalLine, col);
        }
        else
        {
            var line = Math.Clamp(_scrollRow + screenPos.Y, 0, buffer.LineCount - 1);
            var col  = Math.Clamp(screenPos.X - GutterWidth, 0, buffer.GetLine(line).Length);
            return new CursorPosition(line, col);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void PublishSelectionMove(ITextBuffer buffer, CursorPosition newActive)
    {
        var anchor = buffer.Selection.HasValue ? buffer.Selection.Value.Anchor : buffer.Cursor;
        _eventBus.Publish(new SetSelectionEvent(
            new Selection(anchor, newActive), newActive,
            buffer.Selection, buffer.Cursor));
    }

    private static (CursorPosition start, CursorPosition end, string text) SelectionInfo(ITextBuffer buffer)
    {
        var (start, end) = CopyCommand.Normalise(buffer.Selection!.Value);
        return (start, end, CopyCommand.SelectedText(buffer));
    }

    private static (TextRange range, string text) BackspaceRange(CursorPosition pos, ITextBuffer buffer)
    {
        if (pos.Column > 0)
        {
            var start = new CursorPosition(pos.Line, pos.Column - 1);
            return (new TextRange(start, pos), buffer.GetLine(pos.Line)[pos.Column - 1].ToString());
        }
        var prevLine = buffer.GetLine(pos.Line - 1);
        var bsStart  = new CursorPosition(pos.Line - 1, prevLine.Length);
        return (new TextRange(bsStart, pos), "\n");
    }

    private static (TextRange range, string text)? DeleteRange(CursorPosition pos, ITextBuffer buffer)
    {
        var lineLen = buffer.GetLine(pos.Line).Length;
        if (pos.Column < lineLen)
        {
            var end = new CursorPosition(pos.Line, pos.Column + 1);
            return (new TextRange(pos, end), buffer.GetLine(pos.Line)[pos.Column].ToString());
        }
        if (pos.Line < buffer.LineCount - 1)
            return (new TextRange(pos, new CursorPosition(pos.Line + 1, 0)), "\n");
        return null;
    }

    // ── Syntax / selection helpers ─────────────────────────────────────────

    private IReadOnlyList<SyntaxToken> GetTokens(ITextBuffer buffer, int lineIndex)
    {
        if (_syntaxProvider is null) return Array.Empty<SyntaxToken>();

        var lineCount = buffer.LineCount;

        // Resize cache when line count changes. Entries at or after _invalidateFrom are
        // stale regardless — null them so they don't get treated as valid after a shift.
        if (_tokenCache.Length != lineCount)
        {
            var newCache = new CachedLine?[lineCount];
            var copyEnd  = Math.Min(_invalidateFrom, Math.Min(_tokenCache.Length, lineCount));
            if (copyEnd > 0)
                Array.Copy(_tokenCache, newCache, copyEnd);
            _tokenCache = newCache;
        }

        // Determine start state from previous line's cached result
        var startState = lineIndex == 0 ? 0
            : (_tokenCache.Length > lineIndex - 1 && _tokenCache[lineIndex - 1].HasValue
                ? _tokenCache[lineIndex - 1]!.Value.EndState
                : 0);

        // Lazily invalidate at and beyond the watermark
        if (lineIndex >= _invalidateFrom)
            _tokenCache[lineIndex] = null;

        var cached = lineIndex < _tokenCache.Length ? _tokenCache[lineIndex] : null;
        if (cached.HasValue && cached.Value.StartState == startState)
            return cached.Value.Tokens;

        var line   = buffer.GetLine(lineIndex);
        var result = _syntaxProvider.TokenizeLine(line, lineIndex, startState);
        if (lineIndex < _tokenCache.Length)
            _tokenCache[lineIndex] = new CachedLine(startState, result.Tokens, result.EndState);

        // Advance watermark once we've re-tokenized past the stale region
        if (lineIndex >= _invalidateFrom)
            _invalidateFrom = lineIndex + 1;

        return result.Tokens;
    }

    private static TokenType TokenTypeAt(IReadOnlyList<SyntaxToken> tokens, int line, int col)
    {
        foreach (var token in tokens)
        {
            if (token.Line == line && col >= token.StartColumn && col < token.StartColumn + token.Length)
                return token.Type;
        }
        return TokenType.Default;
    }

    private static bool PositionInSelection(int line, int col, Selection selection)
    {
        var (start, end) = selection.Anchor.Line < selection.Active.Line
                           || (selection.Anchor.Line == selection.Active.Line
                               && selection.Anchor.Column <= selection.Active.Column)
            ? (selection.Anchor, selection.Active)
            : (selection.Active, selection.Anchor);

        if (line < start.Line || line > end.Line) return false;
        if (line == start.Line && col < start.Column) return false;
        if (line == end.Line   && col >= end.Column)  return false;
        return true;
    }

    // Returns the index into _searchMatches if (line, col) falls within any match,
    // or -1 if not. Uses binary search on the sorted match list for efficiency.
    private int SearchMatchIndexAt(int line, int col)
    {
        if (_searchMatches.Count == 0 || _searchQueryLength == 0) return -1;

        // Binary search: find the last match whose start position is <= (line, col)
        var lo = 0;
        var hi = _searchMatches.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var m   = _searchMatches[mid];
            var cmp = m.Line != line ? m.Line.CompareTo(line) : m.Column.CompareTo(col);
            if (cmp <= 0) lo = mid + 1;
            else          hi = mid - 1;
        }

        // hi is now the last match whose start <= (line, col)
        if (hi < 0) return -1;
        var match = _searchMatches[hi];
        if (match.Line == line && col >= match.Column && col < match.Column + _searchQueryLength)
            return hi;
        return -1;
    }
}
