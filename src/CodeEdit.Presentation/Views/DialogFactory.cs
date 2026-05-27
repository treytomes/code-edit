using CodeEdit.Application;

namespace CodeEdit.Presentation.Views;

// Implemented per the search/replace and file-open/save feature specs.
public sealed class DialogFactory(ThemeRegistry themeRegistry)
{
    private readonly ThemeRegistry _themeRegistry = themeRegistry;
}
