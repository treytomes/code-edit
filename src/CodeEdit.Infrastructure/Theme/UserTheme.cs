using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Theme;

public sealed class UserTheme : IColorTheme
{
    public string Name { get; set; }

    public ColorPair Normal      { get; set; }
    public ColorPair Selection   { get; set; }
    public ColorPair LineNumber  { get; set; }
    public ColorPair StatusBar   { get; set; }
    public ColorPair MenuBar     { get; set; }
    public ColorPair TabBar      { get; set; }
    public ColorPair FileTree    { get; set; }
    public ColorPair Dialog      { get; set; }
    public ColorPair SearchMatch { get; set; }

    private Dictionary<TokenType, ColorPair> _tokens;

    public ColorPair ForToken(TokenType type)
        => _tokens.TryGetValue(type, out var p) ? p : Normal;

    public UserTheme(string name, ColorPair normal, ColorPair selection, ColorPair lineNumber,
        ColorPair statusBar, ColorPair menuBar, ColorPair tabBar, ColorPair fileTree,
        ColorPair dialog, ColorPair searchMatch, Dictionary<TokenType, ColorPair> tokens)
    {
        Name        = name;
        Normal      = normal;
        Selection   = selection;
        LineNumber  = lineNumber;
        StatusBar   = statusBar;
        MenuBar     = menuBar;
        TabBar      = tabBar;
        FileTree    = fileTree;
        Dialog      = dialog;
        SearchMatch = searchMatch;
        _tokens     = tokens;
    }

    public UserTheme(IColorTheme source, string name)
    {
        Name        = name;
        Normal      = source.Normal;
        Selection   = source.Selection;
        LineNumber  = source.LineNumber;
        StatusBar   = source.StatusBar;
        MenuBar     = source.MenuBar;
        TabBar      = source.TabBar;
        FileTree    = source.FileTree;
        Dialog      = source.Dialog;
        SearchMatch = source.SearchMatch;
        _tokens = Enum.GetValues<TokenType>()
            .Where(t => t != TokenType.Default)
            .ToDictionary(t => t, t => source.ForToken(t));
    }

    public UserTheme Clone(string? newName = null)
        => new(newName ?? Name, Normal, Selection, LineNumber, StatusBar, MenuBar,
               TabBar, FileTree, Dialog, SearchMatch,
               new Dictionary<TokenType, ColorPair>(_tokens));

    public Dictionary<TokenType, ColorPair> Tokens => _tokens;

    public static UserTheme FromDefaults(string name = "VS Code Dark+")
        => new(new DefaultDarkTheme(), name);
}
