using System.Text.Json;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Settings;

public sealed class SettingsService(ILogger<SettingsService> logger, string? settingsDir = null)
{
    private string SettingsPath => Path.Combine(
        settingsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".code-edit"),
        "settings.json");

    public EditorSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            logger.LogDebug("Settings file not found at {Path}, using defaults", SettingsPath);
            return EditorSettings.Default;
        }

        try
        {
            using var stream = File.OpenRead(SettingsPath);
            var raw = JsonSerializer.Deserialize<SettingsJson>(stream, _readOpts);

            if (raw is null) return EditorSettings.Default;

            var tabWidth       = raw.TabWidth       is > 0 ? raw.TabWidth.Value       : EditorSettings.Default.TabWidth;
            var insertSpaces   = raw.InsertSpaces   ?? EditorSettings.Default.InsertSpaces;
            var recentFilesMax = raw.RecentFilesMax is > 0 ? raw.RecentFilesMax.Value : EditorSettings.Default.RecentFilesMax;
            var activeTheme    = raw.ActiveTheme;

            logger.LogInformation("Loaded settings: tabWidth={TabWidth} insertSpaces={InsertSpaces} recentFilesMax={RecentFilesMax} activeTheme={ActiveTheme}",
                tabWidth, insertSpaces, recentFilesMax, activeTheme);
            return new EditorSettings(tabWidth, insertSpaces, recentFilesMax, activeTheme);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load settings from {Path}, using defaults", SettingsPath);
            return EditorSettings.Default;
        }
    }

    public void SaveActiveTheme(string? themeName)
    {
        try
        {
            var existing = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    File.ReadAllText(SettingsPath), _readOpts) ?? []
                : new Dictionary<string, JsonElement>();

            existing["activeTheme"] = themeName is null
                ? JsonSerializer.SerializeToElement((string?)null)
                : JsonSerializer.SerializeToElement(themeName);

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(existing, _writeOpts));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save activeTheme to {Path}", SettingsPath);
        }
    }

    private static readonly JsonSerializerOptions _readOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions _writeOpts = new()
    {
        WriteIndented = true,
    };

    private sealed class SettingsJson
    {
        public int?    TabWidth       { get; set; }
        public bool?   InsertSpaces   { get; set; }
        public int?    RecentFilesMax { get; set; }
        public string? ActiveTheme    { get; set; }
    }
}
