using CodeEdit.Application;
using CodeEdit.Application.Events;
using CodeEdit.Domain;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace CodeEdit.Presentation.Views;

public sealed class SearchBarView : View
{
    public enum Mode { Closed, Find, Replace }

    private readonly IEventBus     _eventBus;
    private readonly SearchService _searchService;
    private readonly IApplication  _app;

    // Child views
    private readonly Label     _findLabel;
    private readonly TextField _findInput;
    private readonly CheckBox  _caseToggle;
    private readonly CheckBox  _wordToggle;
    private readonly Button    _prevButton;
    private readonly Button    _nextButton;
    private readonly Label     _counterLabel;
    private readonly Label     _replLabel;
    private readonly TextField _replInput;
    private readonly Button    _replaceButton;
    private readonly Button    _replaceAllButton;

    // Search state
    private IReadOnlyList<CursorPosition> _matches = [];
    private int    _currentIndex = -1;
    private Mode   _mode         = Mode.Closed;
    private System.Timers.Timer? _wrapTimer;

    public event EventHandler<int>? BarHeightChanged;
    public event EventHandler<SearchResultsChangedEventArgs>? SearchResultsChanged;

    public Mode CurrentMode => _mode;
    public IReadOnlyList<CursorPosition> Matches => _matches;
    public int CurrentMatchIndex => _currentIndex;
    public string Query => _findInput.Text ?? "";
    public int QueryLength => Query.Length;

    public SearchBarView(IEventBus eventBus, SearchService searchService, IApplication app)
    {
        _eventBus      = eventBus;
        _searchService = searchService;
        _app           = app;

        CanFocus = true;
        Visible  = false;
        Height   = 0;

        _findLabel  = new Label     { Text = "Find: ", X = 0,                          Y = 0, Width = 6,                 Height = 1 };
        _findInput  = new TextField {                  X = 6,                          Y = 0, Width = Dim.Fill() - 30,   Height = 1 };
        _caseToggle = new CheckBox  { Text = "Aa",     X = Pos.Right(_findInput) + 1,  Y = 0,                            Height = 1 };
        _wordToggle = new CheckBox  { Text = "W",      X = Pos.Right(_caseToggle) + 1, Y = 0,                            Height = 1 };
        _prevButton = new Button    { Text = "◀",      X = Pos.Right(_wordToggle) + 1, Y = 0,                            Height = 1, NoDecorations = true, NoPadding = true };
        _nextButton = new Button    { Text = "▶",      X = Pos.Right(_prevButton) + 1, Y = 0,                            Height = 1, NoDecorations = true, NoPadding = true };
        _counterLabel = new Label   {                  X = Pos.Right(_nextButton) + 1, Y = 0, Width = Dim.Fill(),        Height = 1 };

        _replLabel        = new Label     { Text = "Repl: ",     X = 0,                              Y = 1, Width = 6,              Height = 1 };
        _replInput        = new TextField {                       X = 6,                              Y = 1, Width = Dim.Fill() - 30, Height = 1 };
        _replaceButton    = new Button    { Text = "Replace",     X = Pos.Right(_replInput) + 1,      Y = 1,                         Height = 1 };
        _replaceAllButton = new Button    { Text = "Replace All", X = Pos.Right(_replaceButton) + 1,  Y = 1,                         Height = 1 };

        Add(_findLabel, _findInput, _caseToggle, _wordToggle, _prevButton, _nextButton, _counterLabel);
        Add(_replLabel, _replInput, _replaceButton, _replaceAllButton);

        _findInput.ValueChanged     += (_, _) => RunSearch();
        _caseToggle.ValueChanged    += (_, _) => RunSearch();
        _wordToggle.ValueChanged    += (_, _) => RunSearch();
        _prevButton.Accepting       += (_, _) => NavigatePrev();
        _nextButton.Accepting       += (_, _) => NavigateNext();
        _replaceButton.Accepting    += (_, _) => DoReplace();
        _replaceAllButton.Accepting += (_, _) => DoReplaceAll();

        SetReplaceRowVisible(false);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void Open(Mode mode)
    {
        var wasOpen = _mode != Mode.Closed;
        _mode = mode;

        SetReplaceRowVisible(mode == Mode.Replace);
        var newHeight = mode == Mode.Closed ? 0 : mode == Mode.Find ? 1 : 2;
        Visible = mode != Mode.Closed;
        Height  = newHeight;
        BarHeightChanged?.Invoke(this, newHeight);

        if (!wasOpen)
        {
            _findInput.Text = "";
            _matches        = [];
            _currentIndex   = -1;
            RaiseSearchResults();
        }

        _findInput.SetFocus();
    }

    public void Close()
    {
        _mode   = Mode.Closed;
        Visible = false;
        Height  = 0;
        BarHeightChanged?.Invoke(this, 0);
        _matches      = [];
        _currentIndex = -1;
        RaiseSearchResults();
    }

    public void NavigateNext()
    {
        if (string.IsNullOrEmpty(Query)) return;

        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { return; }

        var from = _currentIndex >= 0 && _currentIndex < _matches.Count
            ? new CursorPosition(_matches[_currentIndex].Line, _matches[_currentIndex].Column + Query.Length)
            : buffer.Cursor;

        var match = _searchService.FindNext(buffer, Query, MatchCase, WholeWord, from, out var wrapped);
        if (match is null) { UpdateCounter(); return; }

        SelectMatch(buffer, match.Value);
        if (wrapped) ShowWrapped();
    }

    public void NavigatePrev()
    {
        if (string.IsNullOrEmpty(Query)) return;

        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { return; }

        var from = _currentIndex >= 0 && _currentIndex < _matches.Count
            ? _matches[_currentIndex]
            : buffer.Cursor;

        var match = _searchService.FindPrev(buffer, Query, MatchCase, WholeWord, from, out var wrapped);
        if (match is null) { UpdateCounter(); return; }

        SelectMatch(buffer, match.Value);
        if (wrapped) ShowWrapped();
    }

    // ── Key handling ───────────────────────────────────────────────────────

    protected override bool OnKeyDown(Key key)
    {
        if (key.KeyCode == KeyCode.Esc)
        {
            Close();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.F3
            || key.KeyCode == (KeyCode.CtrlMask | KeyCode.F))
        {
            NavigateNext();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.F3))
        {
            NavigatePrev();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.Enter && _mode == Mode.Replace && _replInput.HasFocus)
        {
            DoReplace();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == KeyCode.Enter)
        {
            NavigateNext();
            key.Handled = true;
            return true;
        }
        return base.OnKeyDown(key);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private bool MatchCase => _caseToggle.Value == CheckState.Checked;
    private bool WholeWord => _wordToggle.Value == CheckState.Checked;

    private void RunSearch()
    {
        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { return; }

        _matches      = _searchService.FindAll(buffer, Query, MatchCase, WholeWord);
        _currentIndex = -1;

        if (_matches.Count > 0)
        {
            var cursor = buffer.Cursor;
            for (var i = 0; i < _matches.Count; i++)
            {
                var m = _matches[i];
                if (m.Line > cursor.Line || (m.Line == cursor.Line && m.Column >= cursor.Column))
                {
                    _currentIndex = i;
                    SelectMatch(buffer, m);
                    return;
                }
            }
            _currentIndex = 0;
            SelectMatch(buffer, _matches[0]);
            return;
        }

        UpdateCounter();
        RaiseSearchResults();
    }

    private void SelectMatch(ITextBuffer buffer, CursorPosition match)
    {
        for (var i = 0; i < _matches.Count; i++)
            if (_matches[i] == match) { _currentIndex = i; break; }

        var end       = new CursorPosition(match.Line, match.Column + Query.Length);
        var selection = new Selection(match, end);
        _eventBus.Publish(new SetSelectionEvent(selection, end, buffer.Selection, buffer.Cursor));
        UpdateCounter();
        RaiseSearchResults();
    }

    private void UpdateCounter()
    {
        if (_matches.Count == 0)
            _counterLabel.Text = string.IsNullOrEmpty(Query) ? "" : "No matches";
        else
        {
            var n = _currentIndex >= 0 ? _currentIndex + 1 : 1;
            _counterLabel.Text = _matches.Count == 1 ? "1 match" : $"{n} of {_matches.Count} matches";
        }
        _counterLabel.SetNeedsDraw();
    }

    private void ShowWrapped()
    {
        _counterLabel.Text += " (Wrapped)";
        _counterLabel.SetNeedsDraw();

        _wrapTimer?.Stop();
        _wrapTimer?.Dispose();
        _wrapTimer = new System.Timers.Timer(2000) { AutoReset = false };
        _wrapTimer.Elapsed += (_, _) =>
            _app.Invoke(() => { UpdateCounter(); _counterLabel.SetNeedsDraw(); });
        _wrapTimer.Start();
    }

    private void DoReplace()
    {
        if (string.IsNullOrEmpty(Query)) return;

        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { return; }

        if (_currentIndex < 0 || _currentIndex >= _matches.Count)
        {
            NavigateNext();
            return;
        }

        try
        {
            var (_, ev) = _searchService.BuildReplaceEvent(
                buffer, _matches[_currentIndex], Query, _replInput.Text ?? "", MatchCase, WholeWord);
            _eventBus.Publish(ev);
            RunSearch();
            NavigateNext();
        }
        catch (InvalidOperationException) { RunSearch(); }
    }

    private void DoReplaceAll()
    {
        if (string.IsNullOrEmpty(Query)) return;

        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { return; }

        var (count, events) = _searchService.BuildReplaceAllEvents(
            buffer, Query, _replInput.Text ?? "", MatchCase, WholeWord);

        foreach (var ev in events)
            _eventBus.Publish(ev);

        RunSearch();
        _counterLabel.Text = count == 0 ? "No matches" : $"{count} replacement{(count == 1 ? "" : "s")} made";
        _counterLabel.SetNeedsDraw();
    }

    private void SetReplaceRowVisible(bool visible)
    {
        _replLabel.Visible        = visible;
        _replInput.Visible        = visible;
        _replaceButton.Visible    = visible;
        _replaceAllButton.Visible = visible;
    }

    private void RaiseSearchResults() =>
        SearchResultsChanged?.Invoke(this,
            new SearchResultsChangedEventArgs(_matches, _currentIndex, Query.Length));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _wrapTimer?.Stop();
            _wrapTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public sealed class SearchResultsChangedEventArgs(
    IReadOnlyList<CursorPosition> matches,
    int currentIndex,
    int queryLength) : EventArgs
{
    public IReadOnlyList<CursorPosition> Matches      { get; } = matches;
    public int                           CurrentIndex { get; } = currentIndex;
    public int                           QueryLength  { get; } = queryLength;
}
