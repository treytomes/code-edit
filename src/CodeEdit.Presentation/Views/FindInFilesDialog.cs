using CodeEdit.Application;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace CodeEdit.Presentation.Views;

public sealed class FindInFilesDialog : Dialog
{
    public new bool           Canceled { get; private set; } = true;
    public string             Query    { get; private set; } = "";
    public FindInFilesOptions Options  { get; private set; } = new();

    private readonly IApplication _app;
    private readonly TextField    _queryField;
    private readonly TextField    _globField;
    private readonly CheckBox     _caseCheck;
    private readonly CheckBox     _wordCheck;
    private readonly CheckBox     _regexCheck;

    public FindInFilesDialog(IApplication app, string prefill = "")
    {
        _app = app;
        Title  = "Find in Files";
        Width  = 62;
        Height = 12;

        var lblQuery = new Label { Text = "Query:",  X = 2, Y = 1, Width = 7 };
        var lblGlob  = new Label { Text = "Filter:", X = 2, Y = 3, Width = 7 };
        var lblGlobHint = new Label
        {
            Text = "(file glob, e.g. *.cs)",
            X = 2, Y = 4,
            Width = Dim.Fill() - Dim.Absolute(4),
        };

        _queryField = new TextField
        {
            X = 10, Y = 1,
            Width = Dim.Fill() - Dim.Absolute(4),
            Text = prefill,
        };
        _globField = new TextField
        {
            X = 10, Y = 3,
            Width = Dim.Fill() - Dim.Absolute(4),
        };

        _caseCheck  = new CheckBox { Text = "Case sensitive", X = 2,                       Y = 6 };
        _wordCheck  = new CheckBox { Text = "Whole word",     X = Pos.Right(_caseCheck) + 2, Y = 6 };
        _regexCheck = new CheckBox { Text = "Regex",          X = Pos.Right(_wordCheck) + 2, Y = 6 };

        var btnCancel = new Button { Text = "Cancel", X = Pos.AnchorEnd(24), Y = Pos.AnchorEnd(1) };
        var btnSearch = new Button { Text = "Search", X = Pos.AnchorEnd(12), Y = Pos.AnchorEnd(1), IsDefault = true };

        btnCancel.Accepting += (_, _) => { Canceled = true;  _app.RequestStop(this); };
        btnSearch.Accepting += (_, _) => DoSearch();

        Add(lblQuery, _queryField, lblGlob, _globField, lblGlobHint,
            _caseCheck, _wordCheck, _regexCheck,
            btnCancel, btnSearch);

        _queryField.SetFocus();
    }

    protected override bool OnKeyDown(Terminal.Gui.Input.Key key)
    {
        if (key.KeyCode == Terminal.Gui.Drivers.KeyCode.Esc)
        {
            Canceled = true;
            _app.RequestStop(this);
            key.Handled = true;
            return true;
        }
        return base.OnKeyDown(key);
    }

    private void DoSearch()
    {
        var q = _queryField.Text?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(q)) return;

        Query    = q;
        Options  = new FindInFilesOptions(
            CaseSensitive: _caseCheck.Value  == Terminal.Gui.Views.CheckState.Checked,
            WholeWord:     _wordCheck.Value  == Terminal.Gui.Views.CheckState.Checked,
            UseRegex:      _regexCheck.Value == Terminal.Gui.Views.CheckState.Checked,
            FileGlob:      _globField.Text?.ToString()?.Trim() ?? "");
        Canceled = false;
        _app.RequestStop(this);
    }
}
