using CodeEdit.Application.Ports;
using Terminal.Gui.App;

namespace CodeEdit.Presentation.Views;

public sealed class TGuiClipboardService(IApplication app) : IClipboardService
{
    private Terminal.Gui.App.IClipboard? Board => app.Clipboard;

    public bool IsSupported => Board?.IsSupported ?? false;

    public bool TryGet(out string text)
    {
        if (Board is null) { text = string.Empty; return false; }
        return Board.TryGetClipboardData(out text);
    }

    public bool TrySet(string text) => Board?.TrySetClipboardData(text) ?? false;
}
