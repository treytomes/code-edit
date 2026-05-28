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
            var raw = JsonSerializer.Deserialize<SettingsJson>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas         = true,
                ReadCommentHandling         = JsonCommentHandling.Skip,
            });

            if (raw is null) return EditorSettings.Default;

            var tabWidth       = raw.TabWidth       is > 0 ? raw.TabWidth.Value       : EditorSettings.Default.TabWidth;
            var insertSpaces   = raw.InsertSpaces   ?? EditorSettings.Default.InsertSpaces;
            var recentFilesMax = raw.RecentFilesMax is > 0 ? raw.RecentFilesMax.Value : EditorSettings.Default.RecentFilesMax;

            logger.LogInformation("Loaded settings: tabWidth={TabWidth} insertSpaces={InsertSpaces} recentFilesMax={RecentFilesMax}",
                tabWidth, insertSpaces, recentFilesMax);
            return new EditorSettings(tabWidth, insertSpaces, recentFilesMax);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load settings from {Path}, using defaults", SettingsPath);
            return EditorSettings.Default;
        }
    }

    private sealed class SettingsJson
    {
        public int?  TabWidth       { get; set; }
        public bool? InsertSpaces   { get; set; }
        public int?  RecentFilesMax { get; set; }
    }
}
