using System.Reflection;
using System.Text.Json;
using CodeEdit.Application.Ports;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Syntax;

public sealed class GrammarRegistry : ISyntaxDetector
{
    private readonly PlainTextSyntaxProvider                         _plainText;
    private readonly ILogger<GrammarRegistry>                       _logger;
    private readonly Dictionary<string, GrammarSyntaxProvider>      _byExtension
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GrammarSyntaxProvider>      _byShebang
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GrammarSyntaxProvider>      _byLanguageId
        = new(StringComparer.OrdinalIgnoreCase);

    public GrammarRegistry(PlainTextSyntaxProvider plainText, ILogger<GrammarRegistry> logger)
    {
        _plainText = plainText;
        _logger    = logger;
        Load();
    }

    public ISyntaxProvider Detect(string? filePath, string? firstLine)
    {
        _logger.LogDebug("Detecting language for {FilePath}", filePath);

        if (filePath is not null)
        {
            var ext = Path.GetExtension(filePath);
            if (!string.IsNullOrEmpty(ext) && _byExtension.TryGetValue(ext, out var byExt))
                return byExt;
        }

        if (firstLine is not null && firstLine.StartsWith("#!"))
        {
            foreach (var (shebang, provider) in _byShebang)
            {
                if (firstLine.Contains(shebang, StringComparison.OrdinalIgnoreCase))
                    return provider;
            }
        }

        return _plainText;
    }

    internal ISyntaxProvider? GetById(string languageId) =>
        _byLanguageId.TryGetValue(languageId, out var p) ? p : null;

    private void Load()
    {
        // 1. Embedded built-in grammars
        var asm   = typeof(GrammarRegistry).Assembly;
        var names = asm.GetManifestResourceNames();
        foreach (var name in names)
        {
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = asm.GetManifestResourceStream(name)!;
            TryLoadGrammar(stream, name);
        }

        // 2. User overrides in ~/.code-edit/syntaxes/
        var userDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".code-edit", "syntaxes");
        if (Directory.Exists(userDir))
        {
            foreach (var file in Directory.EnumerateFiles(userDir, "*.json"))
            {
                using var stream = File.OpenRead(file);
                TryLoadGrammar(stream, file);
            }
        }

        // Wire up registry reference for fenced-code delegation
        foreach (var provider in _byLanguageId.Values)
            provider.SetRegistry(this);
    }

    private void TryLoadGrammar(Stream stream, string source)
    {
        try
        {
            var model = JsonSerializer.Deserialize<GrammarModel>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas         = true,
                ReadCommentHandling         = JsonCommentHandling.Skip,
            });

            if (model is null || string.IsNullOrEmpty(model.LanguageId))
            {
                _logger.LogWarning("Skipping invalid grammar from {Source}", source);
                return;
            }

            var provider = new GrammarSyntaxProvider(model);
            _byLanguageId[model.LanguageId] = provider;

            foreach (var ext in model.Extensions)
                _byExtension[ext] = provider;

            foreach (var shebang in model.Shebangs)
                _byShebang[shebang] = provider;

            _logger.LogDebug("Loaded grammar: {LanguageId} from {Source}", model.LanguageId, source);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load grammar from {Source}", source);
        }
    }
}
