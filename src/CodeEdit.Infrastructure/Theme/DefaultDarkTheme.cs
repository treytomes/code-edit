using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Theme;

// Color indices map to Terminal.Gui Color enum values; conversion happens in ColorPairMapper (Presentation).
public sealed class DefaultDarkTheme : IColorTheme
{
    // Implemented per the color-theme feature spec.
    public ColorPair ForToken(TokenType type) => Normal;
    public ColorPair Normal     => new(15, 0);
    public ColorPair Selection  => new(0, 6);
    public ColorPair LineNumber => new(8, 0);
    public ColorPair StatusBar  => new(0, 7);
    public ColorPair MenuBar    => new(0, 7);
    public ColorPair Dialog     => new(0, 7);
}
