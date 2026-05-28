using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Syntax;

public sealed class PlainTextSyntaxProvider : ISyntaxProvider
{
    public string LanguageId   => "plaintext";
    public string DisplayName  => "Plain Text";

    public LineTokens TokenizeLine(string line, int lineIndex, int startState)
        => new(Array.Empty<SyntaxToken>(), 0);
}
