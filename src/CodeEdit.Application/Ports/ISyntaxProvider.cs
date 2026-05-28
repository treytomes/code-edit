using CodeEdit.Domain;

namespace CodeEdit.Application.Ports;

public interface ISyntaxProvider
{
    string LanguageId { get; }
    string DisplayName { get; }

    /// <summary>
    /// Tokenize one line. startState 0 means normal; other values are opaque
    /// to callers — only the provider that produced them interprets them.
    /// </summary>
    LineTokens TokenizeLine(string line, int lineIndex, int startState);
}
