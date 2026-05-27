using CodeEdit.Domain;

namespace CodeEdit.Application.Ports;

public interface ISyntaxProvider
{
    IReadOnlyList<SyntaxToken> Tokenize(IReadOnlyList<string> lines, int startLineIndex);
}
