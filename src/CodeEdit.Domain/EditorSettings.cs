namespace CodeEdit.Domain;

public sealed record EditorSettings(int TabWidth, bool InsertSpaces, int RecentFilesMax)
{
    public static readonly EditorSettings Default = new(TabWidth: 4, InsertSpaces: true, RecentFilesMax: 10);

    public string IndentString => InsertSpaces ? new string(' ', TabWidth) : "\t";
}
