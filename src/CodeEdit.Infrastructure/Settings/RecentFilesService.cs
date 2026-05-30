using System.Text.Json;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Settings;

public sealed class RecentFilesService(ILogger<RecentFilesService> logger, EditorSettings settings, string? settingsDir = null)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters    = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private string RecentPath => Path.Combine(
        settingsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".code-edit"),
        "recent.json");

    public IReadOnlyList<RecentEntry> Load()
    {
        if (!File.Exists(RecentPath))
            return [];

        try
        {
            using var stream = File.OpenRead(RecentPath);
            var list = JsonSerializer.Deserialize<List<RecentEntry>>(stream, JsonOpts);
            return list ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load recent files from {Path}", RecentPath);
            return [];
        }
    }

    public void Add(string path, RecentKind kind = RecentKind.File)
    {
        path = Path.GetFullPath(path);
        var list = Load().ToList();
        list.RemoveAll(e => string.Equals(e.Path, path, PathComparison));
        list.Insert(0, new RecentEntry(path, kind));
        if (list.Count > settings.RecentFilesMax)
            list.RemoveRange(settings.RecentFilesMax, list.Count - settings.RecentFilesMax);
        Save(list);
    }

    public void Clear() => Save([]);

    private void Save(List<RecentEntry> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentPath)!);
            var json = JsonSerializer.Serialize(list, JsonOpts);
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
