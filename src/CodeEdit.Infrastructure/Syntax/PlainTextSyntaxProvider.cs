using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Syntax;

public sealed class PlainTextSyntaxProvider : ISyntaxProvider
{
    public IReadOnlyList<SyntaxToken> Tokenize(IReadOnlyList<string> lines, int startLineIndex)
        => Array.Empty<SyntaxToken>();
}
