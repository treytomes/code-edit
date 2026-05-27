namespace CodeEdit.Application.Ports;

public interface ISyntaxDetector
{
    ISyntaxProvider Detect(string? filePath, string? firstLine);
}
