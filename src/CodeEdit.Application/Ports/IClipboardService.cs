namespace CodeEdit.Application.Ports;

public interface IClipboardService
{
    bool IsSupported { get; }
    bool TryGet(out string text);
    bool TrySet(string text);
}
