using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public static class CopyCommand
{
    public static bool Execute(ITextBuffer buffer, IClipboardService clipboard)
    {
        if (!buffer.Selection.HasValue) return false;
        return clipboard.TrySet(SelectedText(buffer));
    }

    public static string SelectedText(ITextBuffer buffer)
    {
        var sel   = buffer.Selection!.Value;
        var (start, end) = Normalise(sel);

        if (start.Line == end.Line)
            return buffer.GetLine(start.Line)[start.Column..end.Column];

        var sb = new System.Text.StringBuilder();
        sb.Append(buffer.GetLine(start.Line)[start.Column..]);
        for (var l = start.Line + 1; l < end.Line; l++)
        {
            sb.Append('\n');
            sb.Append(buffer.GetLine(l));
        }
        sb.Append('\n');
        sb.Append(buffer.GetLine(end.Line)[..end.Column]);
        return sb.ToString();
    }

    internal static string CurrentLine(ITextBuffer buffer) =>
        buffer.GetLine(buffer.Cursor.Line) + "\n";

    public static (CursorPosition start, CursorPosition end) Normalise(Selection sel)
    {
        var a = sel.Anchor;
        var b = sel.Active;
        return a.Line < b.Line || (a.Line == b.Line && a.Column <= b.Column)
            ? (a, b) : (b, a);
    }
}
