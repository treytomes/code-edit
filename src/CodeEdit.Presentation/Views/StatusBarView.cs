using CodeEdit.Application;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace CodeEdit.Presentation.Views;

public sealed class StatusBarView : View
{
    private readonly IEventBus     _eventBus;
    private readonly ThemeRegistry _themeRegistry;

    public StatusBarView(IEventBus eventBus, ThemeRegistry themeRegistry)
    {
        _eventBus      = eventBus;
        _themeRegistry = themeRegistry;

        CanFocus = false;
        Height   = 1;

        _eventBus.EventExecuted += OnBufferChanged;
        _eventBus.EventUndone   += OnBufferChanged;
        _eventBus.EventRedone   += OnBufferChanged;
        _themeRegistry.ThemeChanged += OnThemeChanged;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var theme = _themeRegistry.Active;
        var attr  = ColorPairMapper.ToAttribute(theme.StatusBar);
        SetAttribute(attr);

        Domain.ITextBuffer? buffer = null;
        try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { }

        var width = Viewport.Width;

        string filePart;
        string cursorPart;

        if (buffer is null)
        {
            filePart   = " [No File]";
            cursorPart = "Ln 1, Col 1 ";
        }
        else
        {
            var path  = buffer.FilePath ?? "[No File]";
            var dirty = buffer.IsDirty ? "*" : " ";
            filePart   = $" {path}{dirty}";
            cursorPart = $"Ln {buffer.Cursor.Line + 1}, Col {buffer.Cursor.Column + 1} ";
        }

        var available = width - cursorPart.Length;
        if (available < 0) available = 0;

        var fileDisplay = filePart.Length <= available
            ? filePart.PadRight(available)
            : filePart[..available];

        var line = fileDisplay + cursorPart;
        if (line.Length > width) line = line[..width];

        AddStr(0, 0, line);
        return true;
    }

    private void OnBufferChanged(object? sender, BufferEventArgs e) => SetNeedsDraw();
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
}
