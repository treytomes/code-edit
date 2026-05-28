using CodeEdit.Domain;
using CodeEdit.Infrastructure.Syntax;

namespace CodeEdit.Tests.Infrastructure;

public sealed class GrammarSyntaxProviderTests
{
    private static GrammarSyntaxProvider Build(string json)
    {
        var model = System.Text.Json.JsonSerializer.Deserialize<GrammarModel>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new GrammarSyntaxProvider(model);
    }

    // ── Line comments ──────────────────────────────────────────────────────

    [Fact]
    public void LineComment_ColorsEntireLine()
    {
        var p  = Build(@"{""languageId"":""t"",""displayName"":""T"",""extensions"":[],""shebangs"":[],
            ""rules"":[{""type"":""line"",""pattern"":""//.*$"",""token"":""Comment""}]}");
        var lt = p.TokenizeLine("// hello", 0, 0);
        Assert.Single(lt.Tokens);
        Assert.Equal(TokenType.Comment, lt.Tokens[0].Type);
        Assert.Equal(0, lt.EndState);
    }

    // ── Block comments ─────────────────────────────────────────────────────

    [Fact]
    public void BlockComment_SingleLine_ClosedOnSameLine()
    {
        var p  = Build(@"{""languageId"":""t"",""displayName"":""T"",""extensions"":[],""shebangs"":[],
            ""rules"":[{""type"":""block"",""open"":""/\\*"",""close"":""\\*/"",""token"":""Comment""}]}");
        var lt = p.TokenizeLine("/* hi */", 0, 0);
        Assert.Single(lt.Tokens);
        Assert.Equal(TokenType.Comment, lt.Tokens[0].Type);
        Assert.Equal(0, lt.EndState);
    }

    [Fact]
    public void BlockComment_Unclosed_CarriesState()
    {
        var p  = Build(@"{""languageId"":""t"",""displayName"":""T"",""extensions"":[],""shebangs"":[],
            ""rules"":[{""type"":""block"",""open"":""/\\*"",""close"":""\\*/"",""token"":""Comment""}]}");
        var lt = p.TokenizeLine("/* unclosed", 0, 0);
        Assert.NotEqual(0, lt.EndState);

        // Continuation: whole next line is comment until close
        var lt2 = p.TokenizeLine("still comment */", 0, lt.EndState);
        Assert.Single(lt2.Tokens);
        Assert.Equal(TokenType.Comment, lt2.Tokens[0].Type);
        Assert.Equal(0, lt2.EndState);
    }

    // ── Span / string literals ─────────────────────────────────────────────

    [Fact]
    public void Span_StringLiteral_Closed()
    {
        var p  = Build(@"{""languageId"":""t"",""displayName"":""T"",""extensions"":[],""shebangs"":[],
            ""rules"":[{""type"":""span"",""open"":""\"""",""close"":""\"""",""escape"":""\\\\"",""multiLine"":false,""token"":""StringLiteral""}]}");
        var lt = p.TokenizeLine(@"""hello""", 0, 0);
        Assert.Single(lt.Tokens);
        Assert.Equal(TokenType.StringLiteral, lt.Tokens[0].Type);
        Assert.Equal(0, lt.EndState);
    }

    [Fact]
    public void Span_Unclosed_NonMultiLine_NoTokenEmitted()
    {
        var p  = Build(@"{""languageId"":""t"",""displayName"":""T"",""extensions"":[],""shebangs"":[],
            ""rules"":[{""type"":""span"",""open"":""\"""",""close"":""\"""",""escape"":""\\\\"",""multiLine"":false,""token"":""StringLiteral""}]}");
        // Unclosed non-multiLine span should not emit a string token and should not carry state
        var lt = p.TokenizeLine(@"""unclosed", 0, 0);
        Assert.Equal(0, lt.EndState);
        Assert.DoesNotContain(lt.Tokens, t => t.Type == TokenType.StringLiteral);
    }

    [Fact]
    public void Span_MultiLine_CarriesState()
    {
        var json = """
            {"languageId":"t","displayName":"T","extensions":[],"shebangs":[],
             "rules":[{"type":"span","open":"\"\"\"","close":"\"\"\"","escape":null,"multiLine":true,"token":"StringLiteral"}]}
            """;
        var p  = Build(json);
        var lt = p.TokenizeLine("x = \"\"\"start", 0, 0);
        Assert.NotEqual(0, lt.EndState);

        var lt2 = p.TokenizeLine("still in string\"\"\"", 0, lt.EndState);
        Assert.Equal(0, lt2.EndState);
    }

    // ── Keywords ───────────────────────────────────────────────────────────

    [Fact]
    public void Keywords_MatchedAtWordBoundary()
    {
        var p  = Build(@"{""languageId"":""t"",""displayName"":""T"",""extensions"":[],""shebangs"":[],
            ""rules"":[{""type"":""keywords"",""words"":[""if"",""else""],""token"":""Keyword""}]}");
        var lt = p.TokenizeLine("if (x) else", 0, 0);
        var kws = lt.Tokens.Where(t => t.Type == TokenType.Keyword).ToList();
        Assert.Equal(2, kws.Count); // "if" and "else"
        Assert.Equal(0, kws[0].StartColumn);
        Assert.Equal(7, kws[1].StartColumn);
    }

    [Fact]
    public void Keywords_NotMatchedInsideWord()
    {
        var p  = Build(@"{""languageId"":""t"",""displayName"":""T"",""extensions"":[],""shebangs"":[],
            ""rules"":[{""type"":""keywords"",""words"":[""if""],""token"":""Keyword""}]}");
        var lt = p.TokenizeLine("ifdef", 0, 0);
        Assert.DoesNotContain(lt.Tokens, t => t.Type == TokenType.Keyword);
    }

    // ── Pattern ────────────────────────────────────────────────────────────

    [Fact]
    public void Pattern_MatchesNumbers()
    {
        var p  = Build(@"{""languageId"":""t"",""displayName"":""T"",""extensions"":[],""shebangs"":[],
            ""rules"":[{""type"":""pattern"",""pattern"":""\\b[0-9]+\\b"",""token"":""Number""}]}");
        var lt = p.TokenizeLine("x = 42;", 0, 0);
        Assert.Single(lt.Tokens.Where(t => t.Type == TokenType.Number));
    }

    // ── PlainText ──────────────────────────────────────────────────────────

    [Fact]
    public void PlainTextProvider_ReturnsEmpty()
    {
        var p  = new PlainTextSyntaxProvider();
        var lt = p.TokenizeLine("anything", 0, 0);
        Assert.Empty(lt.Tokens);
        Assert.Equal(0, lt.EndState);
    }

    // ── GrammarRegistry ────────────────────────────────────────────────────

    [Fact]
    public void GrammarRegistry_SelectsByExtension()
    {
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { });
        var plainText     = new PlainTextSyntaxProvider();
        var logger        = Microsoft.Extensions.Logging.LoggerFactoryExtensions
                                .CreateLogger<GrammarRegistry>(loggerFactory);
        var registry      = new GrammarRegistry(plainText, logger);

        var cs = registry.Detect("Program.cs", null);
        Assert.Equal("csharp", cs.LanguageId);
    }

    [Fact]
    public void GrammarRegistry_FallsBackToPlainText()
    {
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { });
        var plainText     = new PlainTextSyntaxProvider();
        var logger        = Microsoft.Extensions.Logging.LoggerFactoryExtensions
                                .CreateLogger<GrammarRegistry>(loggerFactory);
        var registry      = new GrammarRegistry(plainText, logger);

        var p = registry.Detect("file.xyz", null);
        Assert.Equal("plaintext", p.LanguageId);
    }

    [Fact]
    public void GrammarRegistry_SelectsByShebang()
    {
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { });
        var plainText     = new PlainTextSyntaxProvider();
        var logger        = Microsoft.Extensions.Logging.LoggerFactoryExtensions
                                .CreateLogger<GrammarRegistry>(loggerFactory);
        var registry      = new GrammarRegistry(plainText, logger);

        var p = registry.Detect(null, "#!/usr/bin/env python3");
        Assert.Equal("python", p.LanguageId);
    }
}
