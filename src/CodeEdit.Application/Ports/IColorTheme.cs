using CodeEdit.Domain;

namespace CodeEdit.Application.Ports;

public interface IColorTheme
{
    ColorPair ForToken(TokenType type);
    ColorPair Normal { get; }
    ColorPair Selection { get; }
    ColorPair LineNumber { get; }
    ColorPair StatusBar { get; }
    ColorPair MenuBar { get; }
    ColorPair Dialog { get; }
}
