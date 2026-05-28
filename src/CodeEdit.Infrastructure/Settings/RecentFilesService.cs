using System.Text.Json;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Settings;

public sealed class RecentFilesService(ILogger<RecentFilesService> logger, EditorSettings settings, string? settingsDir = null)
{
    private string RecentPath => Path.Combine(
        settingsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".code-edit"),
        "recent.json");

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(RecentPath))
            return [];

        try
        {
            using var stream = File.OpenRead(RecentPath);
            var list = JsonSerializer.Deserialize<List<string>>(stream);
            return list ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load recent files from {Path}", RecentPath);
            return [];
        }
    }

    public void Add(string path)
    {
        path = Path.GetFullPath(path);
        var list = Load().ToList();
        list.RemoveAll(p => string.Equals(p, path, PathComparison));
        list.Insert(0, path);
        if (list.Count > settings.RecentFilesMax)
            list.RemoveRange(settings.RecentFilesMax, list.Count - settings.RecentFilesMax);
        Save(list);
    }

    public void Clear() => Save([]);

    private void Save(List<string> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentPath)!);
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RecentPath, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save recent files to {Path}", RecentPath);
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
