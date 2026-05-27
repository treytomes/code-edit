using CodeEdit.Application;
using Terminal.Gui.ViewBase;

namespace CodeEdit.Presentation.Views;

// Implemented per the status-bar feature spec.
public sealed class StatusBarView(ThemeRegistry themeRegistry) : View
{
    private readonly ThemeRegistry _themeRegistry = themeRegistry;
}
