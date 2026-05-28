namespace CodeEdit.Infrastructure.Syntax;

internal sealed class GrammarModel
{
    public string   LanguageId   { get; set; } = "";
    public string   DisplayName  { get; set; } = "";
    public string[] Extensions   { get; set; } = [];
    public string[] Shebangs     { get; set; } = [];
    public RuleModel[] Rules     { get; set; } = [];
}

internal sealed class RuleModel
{
    public string   Type      { get; set; } = "";
    public string?  Pattern   { get; set; }
    public string?  Open      { get; set; }
    public string?  Close     { get; set; }
    public string?  Escape    { get; set; }
    public bool     MultiLine { get; set; }
    public string[] Words     { get; set; } = [];
    public string   Token     { get; set; } = "Default";
}
