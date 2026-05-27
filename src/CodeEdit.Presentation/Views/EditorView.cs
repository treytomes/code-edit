using CodeEdit.Application;
using CodeEdit.Domain;
using Terminal.Gui.ViewBase;

namespace CodeEdit.Presentation.Views;

// Implemented per the text-buffer and rendering feature specs.
public sealed class EditorView(IEventBus eventBus, ThemeRegistry themeRegistry) : View
{
    private readonly IEventBus _eventBus = eventBus;
    private readonly ThemeRegistry _themeRegistry = themeRegistry;
}
