using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Theme;

// Color indices map to Terminal.Gui Color enum values; conversion happens in ColorPairMapper (Presentation).
public sealed class DefaultDarkTheme : IColorTheme
{
    public ColorPair ForToken(TokenType type) => type switch
    {
        TokenType.Keyword       => new(12, 0),  // BrightBlue
        TokenType.StringLiteral => new(9,  0),  // BrightRed (salmon)
        TokenType.CharLiteral   => new(9,  0),  // BrightRed
        TokenType.Comment       => new(2,  0),  // DarkGreen
        TokenType.Number        => new(10, 0),  // BrightGreen
        TokenType.Operator      => new(15, 0),  // BrightWhite
        TokenType.Punctuation   => new(8,  0),  // DarkGray
        TokenType.Identifier    => new(15, 0),  // BrightWhite
        _                       => Normal,
    };
    public ColorPair Normal     => new(15, 0);
    public ColorPair Selection  => new(0, 6);
    public ColorPair LineNumber => new(8, 0);
    public ColorPair StatusBar  => new(0, 7);
    public ColorPair MenuBar    => new(0, 7);
    public ColorPair Dialog      => new(0, 7);
    public ColorPair SearchMatch => new(0, 3);   // black on dark-yellow
}
