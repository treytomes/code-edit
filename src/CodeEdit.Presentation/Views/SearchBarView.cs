using System.Text.RegularExpressions;
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
    private readonly CheckBox  _regexToggle;
    private readonly Button    _prevButton;
    private readonly Button    _nextButton;
    private readonly Label     _counterLabel;
    private readonly Label     _replLabel;
    private readonly TextField _replInput;
    private readonly Button    _replaceButton;
    private readonly Button    _replaceAllButton;

    // Search state
    private IReadOnlyList<CursorPosition> _matches      = [];
    private IReadOnlyList<int>            _matchLengths = [];  // parallel to _matches; length of each match
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

        _findLabel   = new Label    { Text = "Find: ", X = 0,                           Y = 0, Width = 6,               Height = 1 };
        _findInput   = new TextField{                  X = 6,                           Y = 0, Width = Dim.Fill() - 34,  Height = 1 };
        _caseToggle  = new CheckBox { Text = "Aa",     X = Pos.Right(_findInput) + 1,  Y = 0,                            Height = 1 };
        _wordToggle  = new CheckBox { Text = "W",      X = Pos.Right(_caseToggle) + 1, Y = 0,                            Height = 1 };
        _regexToggle = new CheckBox { Text = ".*",     X = Pos.Right(_wordToggle) + 1, Y = 0,                            Height = 1 };
        _prevButton  = new Button   { Text = "◀",      X = Pos.Right(_regexToggle) + 1,Y = 0,                            Height = 1, NoDecorations = true, NoPadding = true };
        _nextButton  = new Button   { Text = "▶",      X = Pos.Right(_prevButton) + 1, Y = 0,                            Height = 1, NoDecorations = true, NoPadding = true };
        _counterLabel= new Label    {                  X = Pos.Right(_nextButton) + 1, Y = 0, Width = Dim.Fill(),        Height = 1 };

        _replLabel        = new Label     { Text = "Repl: ",     X = 0,                              Y = 1, Width = 6,              Height = 1 };
        _replInput        = new TextField {                       X = 6,                              Y = 1, Width = Dim.Fill() - 30, Height = 1 };
        _replaceButton    = new Button    { Text = "Replace",     X = Pos.Right(_replInput) + 1,      Y = 1,                         Height = 1 };
        _replaceAllButton = new Button    { Text = "Replace All", X = Pos.Right(_replaceButton) + 1,  Y = 1,                         Height = 1 };

        Add(_findLabel, _findInput, _caseToggle, _wordToggle, _regexToggle, _prevButton, _nextButton, _counterLabel);
        Add(_replLabel, _replInput, _replaceButton, _replaceAllButton);

        _findInput.ValueChanged     += (_, _) => RunSearch();
        _caseToggle.ValueChanged    += (_, _) => RunSearch();
        _wordToggle.ValueChanged    += (_, _) => RunSearch();
        _regexToggle.ValueChanged   += (_, _) => RunSearch();
        _prevButton.Accepting       += (_, _) => NavigatePrev();
        _nextButton.Accepting       += (_, _) => NavigateNext();
        _replaceButton.Accepting    += (_, _) => DoReplace();
        _replaceAllButton.Accepting += (_, _) => DoReplaceAll();

        SetReplaceRowVisible(false);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void Open(Mode mode, bool resetQuery = true)
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
            if (resetQuery || string.IsNullOrEmpty(Query))
            {
                _findInput.Text = "";
                _matches        = [];
                _currentIndex   = -1;
                RaiseSearchResults();
            }
            else
            {
                // Re-opening with a prior query — refresh matches against current buffer
                ITextBuffer? buffer = null;
                try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { }
                if (buffer is not null)
                {
                    _matches      = _searchService.FindAll(buffer, Query, MatchCase, WholeWord);
                    _currentIndex = -1;
                    RaiseSearchResults();
                }
            }
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

    // Called when the active buffer changes so stale highlights are cleared.
    public void ClearSearch()
    {
        _matches      = [];
        _matchLengths = [];
        _currentIndex = -1;
        RaiseSearchResults();
        if (_mode != Mode.Closed)
            RunSearch();
    }

    public void NavigateNext()
    {
        if (string.IsNullOrEmpty(Query) && !UseRegex) return;

        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { return; }

        Regex? regex = null;
        if (UseRegex) { try { regex = BuildRegex(); } catch { return; } }

        var currentLen = _currentIndex >= 0 && _currentIndex < _matchLengths.Count
            ? _matchLengths[_currentIndex] : Query.Length;
        var from = _currentIndex >= 0 && _currentIndex < _matches.Count
            ? new CursorPosition(_matches[_currentIndex].Line, _matches[_currentIndex].Column + currentLen)
            : buffer.Cursor;

        var match = _searchService.FindNext(buffer, Query, MatchCase, WholeWord, from, out var wrapped, regex);
        if (match is null) { UpdateCounter(); return; }

        SelectMatch(buffer, match.Value);
        if (wrapped) ShowWrapped();
    }

    public void NavigatePrev()
    {
        if (string.IsNullOrEmpty(Query) && !UseRegex) return;

        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { return; }

        Regex? regex = null;
        if (UseRegex) { try { regex = BuildRegex(); } catch { return; } }

        var from = _currentIndex >= 0 && _currentIndex < _matches.Count
            ? _matches[_currentIndex]
            : buffer.Cursor;

        var match = _searchService.FindPrev(buffer, Query, MatchCase, WholeWord, from, out var wrapped, regex);
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
            || key.KeyCode == (KeyCode.CtrlMask | KeyCode.F3)
            || key.KeyCode == (KeyCode.CtrlMask | KeyCode.F))
        {
            NavigateNext();
            key.Handled = true;
            return true;
        }
        if (key.KeyCode == (KeyCode.ShiftMask | KeyCode.F3)
            || key.KeyCode == (KeyCode.CtrlMask | KeyCode.ShiftMask | KeyCode.F3))
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

    private bool MatchCase  => _caseToggle.Value  == CheckState.Checked;
    private bool WholeWord  => _wordToggle.Value  == CheckState.Checked;
    private bool UseRegex   => _regexToggle.Value == CheckState.Checked;

    private Regex? BuildRegex()
    {
        if (!UseRegex || string.IsNullOrEmpty(Query)) return null;
        var opts = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        return new Regex(Query, opts);
    }

    private void RunSearch()
    {
        ITextBuffer buffer;
        try { buffer = _eventBus.Buffer; } catch (InvalidOperationException) { return; }

        Regex? regex = null;
        if (UseRegex)
        {
            try { regex = BuildRegex(); }
            catch (ArgumentException)
            {
                _matches      = [];
                _matchLengths = [];
                _currentIndex = -1;
                _counterLabel.Text = "Invalid regex";
                _counterLabel.SetNeedsDraw();
                RaiseSearchResults();
                return;
            }
        }

        _matches      = _searchService.FindAll(buffer, Query, MatchCase, WholeWord, regex);
        _matchLengths = ComputeMatchLengths(buffer, _matches, regex);
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
        var matchLen = Query.Length;
        for (var i = 0; i < _matches.Count; i++)
        {
            if (_matches[i] == match)
            {
                _currentIndex = i;
                matchLen = i < _matchLengths.Count ? _matchLengths[i] : Query.Length;
                break;
            }
        }

        var end       = new CursorPosition(match.Line, match.Column + matchLen);
        var selection = new Selection(match, end);
        _eventBus.Publish(new SetSelectionEvent(selection, end, buffer.Selection, buffer.Cursor));
        UpdateCounter();
        RaiseSearchResults();
    }

    private IReadOnlyList<int> ComputeMatchLengths(
        ITextBuffer buffer, IReadOnlyList<CursorPosition> matches, Regex? regex)
    {
        if (regex is null)
            return matches.Select(_ => Query.Length).ToList();

        var lengths = new List<int>(matches.Count);
        foreach (var pos in matches)
        {
            var line = buffer.GetLine(pos.Line);
            var m    = regex.Match(line, pos.Column);
            lengths.Add(m.Success && m.Index == pos.Column ? m.Length : Query.Length);
        }
        return lengths;
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
