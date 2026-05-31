using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Settings;

public sealed record SessionData(
    IReadOnlyList<string> OpenFiles,
    int ActiveIndex,
    string? RootDir = null);

public sealed class SessionService(ILogger<SessionService> logger, string? workingDir = null)
{
    private string SessionPath => Path.Combine(
        workingDir ?? Environment.CurrentDirectory,
        ".code-edit", "session.json");

    public SessionData Load()
    {
        if (!File.Exists(SessionPath))
            return new SessionData([], 0);

        try
        {
            using var stream = File.OpenRead(SessionPath);
            var data = JsonSerializer.Deserialize<SessionData>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return data ?? new SessionData([], 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load session from {Path}", SessionPath);
            return new SessionData([], 0);
        }
    }

    public void Save(SessionData data)
    {
        try
        {
            var dir = Path.GetDirectoryName(SessionPath)!;
            Directory.CreateDirectory(dir);
            var tmp  = SessionPath + ".tmp";
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json);
            File.Move(tmp, SessionPath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save session to {Path}", SessionPath);
        }
    }
}
