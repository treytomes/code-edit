using System.IO.Enumeration;
using System.Text.RegularExpressions;
using CodeEdit.Application;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Search;

public sealed class FindInFilesService(ILogger<FindInFilesService> logger)
{
    private const int MaxMatches = 1000;

    private static readonly HashSet<string> SkippedDirs =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git" };

    public IReadOnlyList<FileMatches> Search(
        string rootDir, string query, FindInFilesOptions options)
    {
        if (string.IsNullOrEmpty(query)) return [];

        Regex? regex = null;
        if (options.UseRegex)
        {
            var regexOptions = options.CaseSensitive
                ? RegexOptions.None
                : RegexOptions.IgnoreCase;
            regex = new Regex(query, regexOptions);  // throws ArgumentException if invalid
        }

        var results   = new List<FileMatches>();
        var total     = 0;
        var truncated = false;

        WalkDirectory(rootDir, options.FileGlob, (filePath) =>
        {
            if (truncated) return;
            if (IsBinary(filePath)) return;

            List<LineMatch>? fileMatches = null;
            try
            {
                fileMatches = MatchFile(filePath, query, options, regex, ref total, ref truncated);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Skipping unreadable file {File}", filePath);
            }

            if (fileMatches is { Count: > 0 })
                results.Add(new FileMatches(filePath, fileMatches));
        });

        if (truncated)
            results.Add(new FileMatches("",
                [new LineMatch(0, "Results truncated at 1000 matches — refine your query.", 0, 0)]));

        return results;
    }

    // ── File matching ─────────────────────────────────────────────────────

    private static List<LineMatch> MatchFile(
        string filePath, string query, FindInFilesOptions options,
        Regex? regex, ref int total, ref bool truncated)
    {
        var matches    = new List<LineMatch>();
        var comparison = options.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var lines = File.ReadAllLines(filePath);
        for (var lineIdx = 0; lineIdx < lines.Length && !truncated; lineIdx++)
        {
            var line = lines[lineIdx];
            if (regex is not null)
            {
                foreach (Match m in regex.Matches(line))
                {
                    matches.Add(new LineMatch(lineIdx, line, m.Index, m.Length));
                    if (++total >= MaxMatches) { truncated = true; break; }
                }
            }
            else
            {
                var col = 0;
                while (col <= line.Length - query.Length && !truncated)
                {
                    var idx = line.IndexOf(query, col, comparison);
                    if (idx < 0) break;

                    if (!options.WholeWord || IsWholeWord(line, idx, query.Length))
                    {
                        matches.Add(new LineMatch(lineIdx, line, idx, query.Length));
                        if (++total >= MaxMatches) { truncated = true; }
                    }
                    col = idx + 1;
                }
            }
        }
        return matches;
    }

    // ── Directory walk ────────────────────────────────────────────────────

    private static void WalkDirectory(string dir, string fileGlob, Action<string> onFile)
    {
        var entries = new List<string>();
        try { entries.AddRange(Directory.GetFileSystemEntries(dir)); }
        catch { return; }

        var dirs  = entries.Where(Directory.Exists)
                           .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                           .ToList();
        var files = entries.Where(File.Exists)
                           .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                           .ToList();

        foreach (var subDir in dirs)
        {
            if (SkippedDirs.Contains(Path.GetFileName(subDir)))
                continue;
            WalkDirectory(subDir, fileGlob, onFile);
        }

        foreach (var file in files)
        {
            if (!string.IsNullOrEmpty(fileGlob)
                && !FileSystemName.MatchesSimpleExpression(fileGlob, Path.GetFileName(file), ignoreCase: true))
                continue;
            onFile(file);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsBinary(string filePath)
    {
        try
        {
            using var fs  = File.OpenRead(filePath);
            var       buf = new byte[Math.Min(8192, (int)fs.Length)];
            var       read = fs.Read(buf, 0, buf.Length);
            return Array.IndexOf(buf, (byte)0, 0, read) >= 0;
        }
        catch { return true; }
    }

    private static bool IsWholeWord(string line, int start, int length)
    {
        if (start > 0 && IsWordChar(line[start - 1])) return false;
        var end = start + length;
        if (end < line.Length && IsWordChar(line[end])) return false;
        return true;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
