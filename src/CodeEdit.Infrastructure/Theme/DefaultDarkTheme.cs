using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Theme;

public sealed class DefaultDarkTheme : IColorTheme
{
    // ── Palette ────────────────────────────────────────────────────────────
    // Follows VS Code Dark+ (#1E1E1E family)

    private static Rgb Fg         => new(0xD4, 0xD4, 0xD4);  // #D4D4D4  default text
    private static Rgb EditorBg   => new(0x1E, 0x1E, 0x1E);  // #1E1E1E  editor background
    private static Rgb PanelBg    => new(0x25, 0x25, 0x26);  // #252526  sidebar / file tree
    private static Rgb TabBg      => new(0x2D, 0x2D, 0x2D);  // #2D2D2D  tab bar background
    private static Rgb BarBg      => new(0x00, 0x7A, 0xCC);  // #007ACC  status / menu bar
    private static Rgb SelBg      => new(0x26, 0x4F, 0x78);  // #264F78  selection
    private static Rgb LineNumFg  => new(0x85, 0x85, 0x85);  // #858585  line numbers
    private static Rgb Keyword    => new(0x56, 0x9C, 0xD6);  // #569CD6  keywords
    private static Rgb StrLit     => new(0xCE, 0x91, 0x78);  // #CE9178  string literals
    private static Rgb Comment    => new(0x6A, 0x99, 0x55);  // #6A9955  comments
    private static Rgb Number     => new(0xB5, 0xCE, 0xA8);  // #B5CEA8  numeric literals
    private static Rgb Identifier => new(0xDC, 0xDC, 0xAA);  // #DCDCAA  functions / identifiers
    private static Rgb Operator_  => new(0xD4, 0xD4, 0xD4);  // #D4D4D4  operators
    private static Rgb Punct      => new(0x80, 0x80, 0x80);  // #808080  punctuation (muted)
    private static Rgb SearchBg   => new(0x61, 0x33, 0x15);  // #613315  search match background
    private static Rgb White      => new(0xFF, 0xFF, 0xFF);

    private static ColorPair Pair(Rgb fg, Rgb bg) => new(fg, bg);

    // ── IColorTheme ────────────────────────────────────────────────────────

    public ColorPair ForToken(TokenType type) => type switch
    {
        TokenType.Keyword       => Pair(Keyword,    EditorBg),
        TokenType.StringLiteral => Pair(StrLit,     EditorBg),
        TokenType.CharLiteral   => Pair(StrLit,     EditorBg),
        TokenType.Comment       => Pair(Comment,    EditorBg),
        TokenType.Number        => Pair(Number,     EditorBg),
        TokenType.Operator      => Pair(Operator_,  EditorBg),
        TokenType.Punctuation   => Pair(Punct,      EditorBg),
        TokenType.Identifier    => Pair(Identifier, EditorBg),
        _                       => Normal,
    };

    public ColorPair Normal      => Pair(Fg,       EditorBg);
    public ColorPair Selection   => Pair(White,    SelBg);
    public ColorPair LineNumber  => Pair(LineNumFg, EditorBg);
    public ColorPair StatusBar   => Pair(White,    BarBg);
    public ColorPair MenuBar     => Pair(White,    BarBg);
    public ColorPair TabBar      => Pair(Fg,       TabBg);
    public ColorPair FileTree    => Pair(Fg,       PanelBg);
    public ColorPair Dialog      => Pair(Fg,       PanelBg);
    public ColorPair SearchMatch => Pair(Fg,       SearchBg);
}
