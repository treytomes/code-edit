using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodeEdit.Domain;
using CodeEdit.Infrastructure.Theme;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Settings;

public sealed class ThemeService(ILogger<ThemeService> logger, string? themesDir = null)
{
    private string ThemesDir => themesDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".code-edit", "themes");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas    = true,
        ReadCommentHandling    = JsonCommentHandling.Skip,
    };

    public IReadOnlyList<UserTheme> LoadAll()
    {
        Directory.CreateDirectory(ThemesDir);

        var files = Directory.GetFiles(ThemesDir, "*.json");
        if (files.Length == 0)
        {
            var builtin = UserTheme.FromDefaults();
            Save(builtin);
            return [builtin];
        }

        var themes = new List<UserTheme>();
        foreach (var file in files)
        {
            try
            {
                var theme = ReadFile(file);
                if (theme is not null) themes.Add(theme);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping unreadable theme file {File}", file);
            }
        }
        return themes;
    }

    public UserTheme? LoadByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = Path.Combine(ThemesDir, Slugify(name) + ".json");
        if (!File.Exists(path)) return null;
        try { return ReadFile(path); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load theme {Name}", name);
            return null;
        }
    }

    public void Save(UserTheme theme)
    {
        Directory.CreateDirectory(ThemesDir);
        var path = Path.Combine(ThemesDir, Slugify(theme.Name) + ".json");
        var dto  = ToDto(theme);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOpts));
        logger.LogDebug("Saved theme {Name} to {Path}", theme.Name, path);
    }

    public void Delete(string name)
    {
        var path = Path.Combine(ThemesDir, Slugify(name) + ".json");
        if (!File.Exists(path)) return;
        File.Delete(path);
        logger.LogDebug("Deleted theme {Name}", name);
    }

    public void Export(UserTheme theme, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var dto = ToDto(theme);
        File.WriteAllText(destinationPath, JsonSerializer.Serialize(dto, JsonOpts));
        logger.LogDebug("Exported theme {Name} to {Path}", theme.Name, destinationPath);
    }

    public UserTheme Import(string sourcePath)
    {
        ThemeDto dto;
        try
        {
            var json = File.ReadAllText(sourcePath);
            dto = JsonSerializer.Deserialize<ThemeDto>(json, JsonOpts)
                  ?? throw new ThemeImportException("File is empty or null.");
        }
        catch (JsonException ex)
        {
            throw new ThemeImportException($"Invalid theme JSON: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ThemeImportException("Theme file is missing a name.");

        var destPath = Path.Combine(ThemesDir, Slugify(dto.Name) + ".json");
        if (File.Exists(destPath))
            throw new ThemeImportException($"A theme named \"{dto.Name}\" already exists.");

        Directory.CreateDirectory(ThemesDir);
        File.Copy(sourcePath, destPath);
        logger.LogDebug("Imported theme {Name} from {Source}", dto.Name, sourcePath);
        return FromDto(dto);
    }

    // ── Slug ──────────────────────────────────────────────────────────────

    public static string Slugify(string name)
        => Regex.Replace(name.ToLowerInvariant().Replace(' ', '-'), @"[^a-z0-9\-]", "");

    // ── DTO ───────────────────────────────────────────────────────────────

    private static UserTheme? ReadFile(string path)
    {
        var json = File.ReadAllText(path);
        var dto  = JsonSerializer.Deserialize<ThemeDto>(json, JsonOpts);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Name)) return null;
        return FromDto(dto);
    }

    private static UserTheme FromDto(ThemeDto d)
    {
        static Rgb ParseRgb(string? hex, Rgb fallback)
        {
            if (hex is null || hex.Length != 7 || hex[0] != '#') return fallback;
            try
            {
                return new Rgb(
                    Convert.ToByte(hex[1..3], 16),
                    Convert.ToByte(hex[3..5], 16),
                    Convert.ToByte(hex[5..7], 16));
            }
            catch { return fallback; }
        }

        static ColorPair ParsePair(ColorPairDto? p, ColorPair fallback)
            => p is null ? fallback
             : new ColorPair(ParseRgb(p.Fg, fallback.Foreground), ParseRgb(p.Bg, fallback.Background));

        var def     = UserTheme.FromDefaults();
        var tokens  = new Dictionary<TokenType, ColorPair>();

        if (d.Tokens is not null)
        {
            foreach (var (key, val) in d.Tokens)
            {
                if (Enum.TryParse<TokenType>(key, ignoreCase: true, out var tt) && tt != TokenType.Default)
                    tokens[tt] = ParsePair(val, def.ForToken(tt));
            }
        }

        foreach (TokenType tt in Enum.GetValues<TokenType>())
            if (tt != TokenType.Default && !tokens.ContainsKey(tt))
                tokens[tt] = def.ForToken(tt);

        return new UserTheme(
            d.Name!,
            ParsePair(d.Normal,      def.Normal),
            ParsePair(d.Selection,   def.Selection),
            ParsePair(d.LineNumber,  def.LineNumber),
            ParsePair(d.StatusBar,   def.StatusBar),
            ParsePair(d.MenuBar,     def.MenuBar),
            ParsePair(d.TabBar,      def.TabBar),
            ParsePair(d.FileTree,    def.FileTree),
            ParsePair(d.Dialog,      def.Dialog),
            ParsePair(d.SearchMatch, def.SearchMatch),
            tokens);
    }

    private static ThemeDto ToDto(UserTheme t)
    {
        static string Hex(Rgb c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        static ColorPairDto P(ColorPair p) => new(Hex(p.Foreground), Hex(p.Background));

        return new ThemeDto
        {
            Name        = t.Name,
            Normal      = P(t.Normal),
            Selection   = P(t.Selection),
            LineNumber  = P(t.LineNumber),
            StatusBar   = P(t.StatusBar),
            MenuBar     = P(t.MenuBar),
            TabBar      = P(t.TabBar),
            FileTree    = P(t.FileTree),
            Dialog      = P(t.Dialog),
            SearchMatch = P(t.SearchMatch),
            Tokens      = t.Tokens.ToDictionary(
                kv => kv.Key.ToString(),
                kv => P(kv.Value)),
        };
    }

    // ── DTO types ─────────────────────────────────────────────────────────

    private sealed class ThemeDto
    {
        public string?                           Name        { get; set; }
        public ColorPairDto?                     Normal      { get; set; }
        public ColorPairDto?                     Selection   { get; set; }
        public ColorPairDto?                     LineNumber  { get; set; }
        public ColorPairDto?                     StatusBar   { get; set; }
        public ColorPairDto?                     MenuBar     { get; set; }
        public ColorPairDto?                     TabBar      { get; set; }
        public ColorPairDto?                     FileTree    { get; set; }
        public ColorPairDto?                     Dialog      { get; set; }
        public ColorPairDto?                     SearchMatch { get; set; }
        public Dictionary<string, ColorPairDto>? Tokens      { get; set; }
    }

    private sealed class ColorPairDto(string fg, string bg)
    {
        public string Fg { get; set; } = fg;
        public string Bg { get; set; } = bg;
    }
}
