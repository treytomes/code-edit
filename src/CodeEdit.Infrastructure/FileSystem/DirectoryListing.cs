namespace CodeEdit.Infrastructure.FileSystem;

public static class DirectoryListing
{
    public static List<string> BuildEntries(string directoryPath)
    {
        var entries = new List<string>();
        if (Path.GetPathRoot(directoryPath) != directoryPath)
            entries.Add("..");
        try
        {
            entries.AddRange(
                Directory.EnumerateDirectories(directoryPath)
                    .Select(d => Path.GetFileName(d)!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Select(n => n + "/"));
        }
        catch { }
        return entries;
    }
}
