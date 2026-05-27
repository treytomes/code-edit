using CodeEdit.Application;
using CodeEdit.Application.Events;
using CodeEdit.Application.Ports;
using CodeEdit.Domain;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace CodeEdit.Presentation.Views;

public sealed class EditorView : View
{
    private readonly IEventBus       _eventBus;
    private readonly ThemeRegistry   _themeRegistry;
    private readonly ISyntaxDetector _syntaxDetector;
    private ISyntaxProvider?         _syntaxProvider;
    private int                      _scrollRow;
    private int                      _wantColumn;

    public EditorView(IEventBus eventBus, ThemeRegistry themeRegistry, ISyntaxDetector syntaxDetector)
    {
        _eventBus       = eventBus;
        _themeRegistry  = themeRegistry;
        _syntaxDetector = syntaxDetector;

        CanFocus = true;

        _eventBus.EventExecuted += OnBufferChanged;
        _eventBus.EventUndone   += OnBufferChanged;
        _eventBus.EventRedone   += OnBufferChanged;
        _themeRegistry.ThemeChanged += OnThemeChanged;
    }

    public void SetBuffer(IMutableTextBuffer buffer)
    {
        var firstLine = buffer.LineCount > 0 ? buffer.GetLine(0) : null;
        _syntaxProvider = _syntaxDetector.Detect(buffer.FilePath, firstLine);
        _scrollRow  = 0;
        _wantColumn = 0;
        SetNeedsDraw();
    }

    // ── Drawing ────────────────────────────────────────────────────────────

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var theme = _themeRegistry.Active;
        var normalAttr    = ColorPairMapper.ToAttribute(theme.Normal);
        var lineNumAttr   = ColorPairMapper.ToAttribute(theme.LineNumber);
        var selectionAttr = ColorPairMapper.ToAttribute(theme.Selection);

        ITextBuffer? buffer = null;
        try { buffer = _eventBus.Buffer; }
        catch (InvalidOperationException) { }

        var height = Viewport.Height;
        var width  = Viewport.Width;

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
            var tokens   = _syntaxProvider!.Tokenize([lineText], lineIndex);

            // Gutter
            var gutterText = (lineIndex + 1).ToString().PadLeft(4) + " ";
            SetAttribute(lineNumAttr);
            AddStr(0, r, gutterText);

            // Text area
            const int gutterWidth = 5;
            for (var col = 0; col < lineText.Length; col++)
            {
                var screenCol = gutterWidth + col;
                if (screenCol >= width) break;

                var ch        = lineText[col];
                var tokenType = TokenTypeAt(tokens, lineIndex, col);
                var isSelected = buffer.Selection.HasValue
                                 && PositionInSelection(lineIndex, col, buffer.Selection.Value);
                var attr = isSelected
                    ? selectionAttr
                    : ColorPairMapper.ToAttribute(theme.ForToken(tokenType));

                SetAttribute(attr);
                AddStr(screenCol, r, ch.ToString());
            }

            // Fill rest of line
            var fillStart = gutterWidth + lineText.Length;
            if (fillStart < width)
            {
                SetAttribute(normalAttr);
                AddStr(fillStart, r, new string(' ', width - fillStart));
            }

            // Cursor overlay
            if (lineIndex == buffer.Cursor.Line)
            {
                var cursorScreenCol = gutterWidth + buffer.Cursor.Column;
                if (cursorScreenCol < width)
                {
                    var ch = buffer.Cursor.Column < lineText.Length
                        ? lineText[buffer.Cursor.Column].ToString()
                        : " ";
                    SetAttribute(selectionAttr);
                    AddStr(cursorScreenCol, r, ch);
                }
            }
        }

        return true;
    }

    // ── Keyboard ───────────────────────────────────────────────────────────

    protected override bool OnKeyDown(Key key)
    {
        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; }
        catch (InvalidOperationException) { return false; }

        var pos = buffer.Cursor;

        if (key.KeyCode == KeyCode.CursorUp)
        {
            var newPos = MoveUp(pos, _wantColumn, buffer);
            _eventBus.Publish(new SetCursorEvent(newPos, pos));
            return true;
        }

        if (key.KeyCode == KeyCode.CursorDown)
        {
            var newPos = MoveDown(pos, _wantColumn, buffer);
            _eventBus.Publish(new SetCursorEvent(newPos, pos));
            return true;
        }

        if (key.KeyCode == KeyCode.CursorLeft)
        {
            var newPos = MoveLeft(pos, buffer);
            _wantColumn = newPos.Column;
            _eventBus.Publish(new SetCursorEvent(newPos, pos));
            return true;
        }

        if (key.KeyCode == KeyCode.CursorRight)
        {
            var newPos = MoveRight(pos, buffer);
            _wantColumn = newPos.Column;
            _eventBus.Publish(new SetCursorEvent(newPos, pos));
            return true;
        }

        if (key.KeyCode == KeyCode.Enter)
        {
            _wantColumn = 0;
            _eventBus.Publish(new InsertTextEvent(pos, "\n"));
            return true;
        }

        if (key.KeyCode == KeyCode.Backspace)
        {
            if (pos.Line == 0 && pos.Column == 0) return true;

            var (range, text) = BackspaceRange(pos, buffer);
            _wantColumn = range.Start.Column;
            _eventBus.Publish(new DeleteEvent(range, text));
            return true;
        }

        if (key.KeyCode == KeyCode.Delete)
        {
            var del = DeleteRange(pos, buffer);
            if (del.HasValue)
            {
                _wantColumn = pos.Column;
                _eventBus.Publish(new DeleteEvent(del.Value.range, del.Value.text));
            }
            return true;
        }

        if (key.TryGetPrintableRune(out var rune))
        {
            var ch = rune.ToString();
            _wantColumn = pos.Column + ch.Length;
            _eventBus.Publish(new InsertTextEvent(pos, ch));
            return true;
        }

        return false;
    }

    // ── Scrolling ──────────────────────────────────────────────────────────

    private void ScrollToCursor()
    {
        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; }
        catch (InvalidOperationException) { return; }

        var cursorLine = buffer.Cursor.Line;
        var height     = Viewport.Height;

        if (cursorLine < _scrollRow)
            _scrollRow = cursorLine;
        else if (height > 0 && cursorLine >= _scrollRow + height)
            _scrollRow = cursorLine - height + 1;
    }

    // ── Event handlers ─────────────────────────────────────────────────────

    private void OnBufferChanged(object? sender, BufferEventArgs e)
    {
        ScrollToCursor();
        SetNeedsDraw();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => SetNeedsDraw();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _eventBus.EventExecuted -= OnBufferChanged;
            _eventBus.EventUndone   -= OnBufferChanged;
            _eventBus.EventRedone   -= OnBufferChanged;
            _themeRegistry.ThemeChanged -= OnThemeChanged;
        }
        base.Dispose(disposing);
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

    private static (TextRange range, string text) BackspaceRange(CursorPosition pos, ITextBuffer buffer)
    {
        if (pos.Column > 0)
        {
            var start = new CursorPosition(pos.Line, pos.Column - 1);
            var text  = buffer.GetLine(pos.Line)[pos.Column - 1].ToString();
            return (new TextRange(start, pos), text);
        }
        else
        {
            var prevLine  = buffer.GetLine(pos.Line - 1);
            var start     = new CursorPosition(pos.Line - 1, prevLine.Length);
            return (new TextRange(start, pos), "\n");
        }
    }

    private static (TextRange range, string text)? DeleteRange(CursorPosition pos, ITextBuffer buffer)
    {
        var lineLen = buffer.GetLine(pos.Line).Length;
        if (pos.Column < lineLen)
        {
            var end  = new CursorPosition(pos.Line, pos.Column + 1);
            var text = buffer.GetLine(pos.Line)[pos.Column].ToString();
            return (new TextRange(pos, end), text);
        }
        if (pos.Line < buffer.LineCount - 1)
        {
            var end = new CursorPosition(pos.Line + 1, 0);
            return (new TextRange(pos, end), "\n");
        }
        return null;
    }

    // ── Syntax / selection helpers ─────────────────────────────────────────

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
                           || (selection.Anchor.Line == selection.Active.Line && selection.Anchor.Column <= selection.Active.Column)
            ? (selection.Anchor, selection.Active)
            : (selection.Active, selection.Anchor);

        if (line < start.Line || line > end.Line) return false;
        if (line == start.Line && col < start.Column) return false;
        if (line == end.Line   && col >= end.Column)  return false;
        return true;
    }
}
