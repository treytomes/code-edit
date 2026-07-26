namespace CodeEdit.Application;

public sealed record FindInFilesOptions(
    bool   CaseSensitive = false,
    bool   WholeWord     = false,
    bool   UseRegex      = false,
    string FileGlob      = "");
