using System.Reflection;
using CodeEdit.Application;
using CodeEdit.Application.Events;
using CodeEdit.Application.Ports;
using CodeEdit.Domain;
using CodeEdit.Infrastructure.Buffer;
using CodeEdit.Infrastructure.Logging;
using CodeEdit.Infrastructure.Settings;
using CodeEdit.Infrastructure.Syntax;
using CodeEdit.Infrastructure.Theme;
using CodeEdit.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TGuiApp = Terminal.Gui.App.Application;

namespace CodeEdit.Presentation;

public static class AppBootstrap
{
    private const int TreeWidth      = 30;
    private const int MinEditorWidth = 30;

    public static void Run()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".code-edit", "logs");
        try { Directory.CreateDirectory(logDir); }
        catch (Exception) { /* Best-effort; file logging simply won't work */ }

        var services = new ServiceCollection();

        services.AddLogging(b => b
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(new RollingFileLoggerProvider(logDir)));

        services.AddSingleton<IColorTheme, DefaultDarkTheme>();
        services.AddSingleton<ThemeRegistry>(sp =>
            new ThemeRegistry(sp.GetRequiredService<IColorTheme>()));

        services.AddSingleton<PlainTextSyntaxProvider>();
        services.AddSingleton<ISyntaxDetector, GrammarRegistry>();
        services.AddSingleton<IFileService, FileService>();

        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<SearchService>();

        services.AddSingleton<SettingsService>();
        services.AddSingleton<StatusBarView>();
        services.AddSingleton<DialogFactory>();

        var provider = services.BuildServiceProvider();

        var logger = provider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CodeEdit.Presentation.AppBootstrap");
        logger.LogInformation("code-edit starting");

        using var app = TGuiApp.Create();
        app.Init();

        // EditorView requires IApplication (available after Init) — register after Init
        services.AddSingleton<IClipboardService>(new NativeClipboardService());
        services.AddSingleton<Terminal.Gui.App.IApplication>(app);
        var editorSettings = provider.GetRequiredService<SettingsService>().Load();
        services.AddSingleton(editorSettings);
        services.AddSingleton<RecentFilesService>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<EditorView>();
        services.AddSingleton<SearchBarView>();
        services.AddSingleton<TabBarView>();
        services.AddSingleton<FileTreeView>();
        provider = services.BuildServiceProvider();

        try
        {
            var editorView    = provider.GetRequiredService<EditorView>();
            var statusBar     = provider.GetRequiredService<StatusBarView>();
            var searchBar     = provider.GetRequiredService<SearchBarView>();
            var tabBar        = provider.GetRequiredService<TabBarView>();
            var fileTree      = provider.GetRequiredService<FileTreeView>();
            var eventBus      = provider.GetRequiredService<IEventBus>();
            var fileService   = provider.GetRequiredService<IFileService>();
            var recentFiles   = provider.GetRequiredService<RecentFilesService>();
            var sessionSvc    = provider.GetRequiredService<SessionService>();
            var themeSvc      = provider.GetRequiredService<ThemeService>();
            var themeRegistry = provider.GetRequiredService<ThemeRegistry>();
            var settingsSvc   = provider.GetRequiredService<SettingsService>();

            // ── Theme seeding and active theme restore ─────────────────────
            themeSvc.LoadAll();  // seeds ~/.code-edit/themes/ if empty
            if (editorSettings.ActiveTheme is not null)
            {
                var saved = themeSvc.LoadByName(editorSettings.ActiveTheme);
                if (saved is not null) themeRegistry.SetTheme(saved);
            }

            // ── Working directory root ─────────────────────────────────────

            string rootDir;
            string? cmdOpenError = null;
            var cmdArgs = Environment.GetCommandLineArgs();

            if (cmdArgs.Length > 1 && !string.IsNullOrWhiteSpace(cmdArgs[1]))
            {
                var cmdPath = cmdArgs[1];
                rootDir = Directory.Exists(cmdPath)
                    ? cmdPath
                    : Path.GetDirectoryName(Path.GetFullPath(cmdPath)) ?? Environment.CurrentDirectory;

                try
                {
                    var buf = (IMutableTextBuffer)fileService.Open(cmdPath);
                    recentFiles.Add(cmdPath, RecentKind.File);
                    logger.LogInformation("Opened file: {Path}", cmdPath);
                    eventBus.Buffers.Add(buf);
                }
                catch (FileServiceException ex)
                {
                    logger.LogWarning(ex, "Could not open command-line file: {Path}", cmdPath);
                    eventBus.Buffers.Add(new EmptyBuffer());
                    cmdOpenError = ex.Message;
                }
            }
            else
            {
                // Start with placeholder; session restore may replace it below
                eventBus.Buffers.Add(new EmptyBuffer());

                var session       = sessionSvc.Load();
                rootDir           = session.RootDir ?? Environment.CurrentDirectory;
                var restoredCount = 0;

                foreach (var path in session.OpenFiles)
                {
                    if (!File.Exists(path)) continue;
                    try
                    {
                        var restored = (IMutableTextBuffer)fileService.Open(path);
                        eventBus.Buffers.Add(restored);
                        restoredCount++;
                    }
                    catch (FileServiceException ex)
                    {
                        logger.LogWarning(ex, "Session restore: failed to open {Path}", path);
                    }
                }

                if (restoredCount > 0)
                {
                    eventBus.Buffers.Close(0);
                    var clampedIndex = Math.Clamp(session.ActiveIndex, 0, restoredCount - 1);
                    eventBus.Buffers.Activate(clampedIndex);
                }
            }

            fileTree.Populate(rootDir);
            editorView.SetBuffer(eventBus.Buffers.ActiveBuffer);
            tabBar.Refresh(eventBus.Buffers.Tabs, eventBus.Buffers.ActiveIndex);

            // ── Layout helpers ─────────────────────────────────────────────

            var currentBarH    = 0;
            var treeUserVisible = true;

            void UpdateEditorLayout()
            {
                var tabH  = tabBar.Visible ? 1 : 0;
                var treeW = fileTree.Visible ? TreeWidth : 0;
                fileTree.Height   = Dim.Fill() - Dim.Absolute(1 + currentBarH);
                editorView.X      = Pos.Absolute(treeW);
                editorView.Width  = Dim.Fill();   // Fill from X, which already accounts for tree width
                editorView.Height = Dim.Fill() - Dim.Absolute(1 + currentBarH);
                searchBar.Y       = Pos.AnchorEnd(1 + currentBarH);
                searchBar.Height  = Dim.Absolute(currentBarH);
            }

            void SetTreeVisible(bool visible)
            {
                fileTree.Visible = visible;
                fileTree.Width   = Dim.Absolute(visible ? TreeWidth : 0);
                UpdateEditorLayout();
            }

            void RefreshTabBar()
                => tabBar.Refresh(eventBus.Buffers.Tabs, eventBus.Buffers.ActiveIndex);

            SessionData BuildSessionData() => new(
                eventBus.Buffers.Tabs
                    .Select(t => t.Buffer.FilePath)
                    .Where(p => p is not null)
                    .ToList()!,
                eventBus.Buffers.ActiveIndex,
                rootDir);

            void SaveSession() => sessionSvc.Save(BuildSessionData());

            eventBus.BufferChanged += (_, _) =>
            {
                editorView.SetBuffer(eventBus.Buffers.ActiveBuffer);
                RefreshTabBar();
                searchBar.ClearSearch();
            };
            eventBus.EventExecuted += (_, _) => RefreshTabBar();

            eventBus.Buffers.TabsChanged      += (_, _) => SaveSession();
            eventBus.Buffers.ActiveTabChanged += (_, _) => SaveSession();

            // ── File helpers ───────────────────────────────────────────────

            void SetActiveBuffer(IMutableTextBuffer newBuf)
            {
                eventBus.Buffers.Add(newBuf);
                editorView.SetBuffer(newBuf);
            }

            void DoNew()
            {
                if (eventBus.Buffer.IsDirty)
                {
                    var choice = MessageBox.Query(app, "Unsaved Changes", "You have unsaved changes.\nCreate a new file anyway?", "Yes", "No");
                    if (choice != 0) return;
                }
                SetActiveBuffer(new EmptyBuffer());
            }

            void DoQuit()
            {
                if (eventBus.Buffer.IsDirty)
                {
                    var choice = MessageBox.Query(app, "Unsaved Changes", "You have unsaved changes.\nQuit anyway?", "Yes", "No");
                    if (choice != 0) return;
                }
                app.RequestStop();
            }

            void DoOpenFile()
            {
                var dlg = new OpenDialog { MustExist = true, OpenMode = Terminal.Gui.Views.OpenMode.File };
                app.Run(dlg);

                if (dlg.Canceled || dlg.FilePaths.Count == 0) return;

                var path = dlg.FilePaths[0].ToString()!;
                try
                {
                    SetActiveBuffer((IMutableTextBuffer)fileService.Open(path));
                    recentFiles.Add(path, RecentKind.File);
                }
                catch (FileServiceException ex)
                {
                    statusBar.SetMessage(ex.Message);
                }
            }

            void DoOpenFolder(string? overridePath = null)
            {
                string folderPath;
                if (overridePath is not null)
                {
                    folderPath = overridePath;
                }
                else
                {
                    var dlg = new OpenDialog { MustExist = true, OpenMode = Terminal.Gui.Views.OpenMode.Directory };
                    app.Run(dlg);
                    if (dlg.Canceled || dlg.FilePaths.Count == 0) return;
                    folderPath = dlg.FilePaths[0].ToString()!;
                }

                rootDir = folderPath;
                fileTree.Populate(folderPath);
                recentFiles.Add(folderPath, RecentKind.Folder);
                SaveSession();
            }

            void DoSave()
            {
                var buf = eventBus.Buffer;
                if (buf.FilePath is null)
                {
                    DoSaveAs();
                    return;
                }
                try
                {
                    fileService.Save(buf);
                    recentFiles.Add(buf.FilePath!, RecentKind.File);
                    statusBar.SetNeedsDraw();
                    editorView.SetNeedsDraw();
                }
                catch (FileServiceException ex)
                {
                    statusBar.SetMessage(ex.Message);
                }
            }

            void DoSaveAs()
            {
                var dlg = new SaveDialog();
                app.Run(dlg);

                if (dlg.FileName is null) return;

                var path = dlg.FileName.ToString()!;
                try
                {
                    var saved = (IMutableTextBuffer)fileService.SaveAs(eventBus.Buffer, path);
                    SetActiveBuffer(saved);
                    recentFiles.Add(path, RecentKind.File);
                }
                catch (FileServiceException ex)
                {
                    statusBar.SetMessage(ex.Message);
                }
            }

            // ── Undo / Redo ────────────────────────────────────────────────

            void DoUndo() { if (eventBus.CanUndo) eventBus.Undo(); }
            void DoRedo() { if (eventBus.CanRedo) eventBus.Redo(); }

            // ── Clipboard helpers ──────────────────────────────────────────

            var clipboardSvc = provider.GetRequiredService<IClipboardService>();

            void DoCopy()
            {
                if (!clipboardSvc.IsSupported) { statusBar.SetMessage("Clipboard not available"); return; }
                if (!CopyCommand.Execute(eventBus.Buffer, clipboardSvc)) statusBar.SetMessage("No text selected");
            }
            void DoCut()
            {
                if (!clipboardSvc.IsSupported) { statusBar.SetMessage("Clipboard not available"); return; }
                eventBus.Publish(new CutEvent(eventBus.Buffer, clipboardSvc));
            }
            void DoPaste()
            {
                if (!clipboardSvc.IsSupported) { statusBar.SetMessage("Clipboard not available"); return; }
                eventBus.Publish(new PasteEvent(eventBus.Buffer, clipboardSvc));
            }

            editorView.NewRequested  += (_, _) => DoNew();
            editorView.OpenRequested += (_, _) => DoOpenFile();
            editorView.SaveRequested += (_, _) => DoSave();

            // ── Search bar wiring ──────────────────────────────────────────

            searchBar.SearchResultsChanged += (_, e) =>
                editorView.UpdateSearchResults(e.Matches, e.CurrentIndex, e.QueryLength);

            searchBar.BarHeightChanged += (_, barH) =>
            {
                currentBarH = barH;
                UpdateEditorLayout();
            };

            editorView.FindNextRequested += (_, _) =>
            {
                if (searchBar.CurrentMode == SearchBarView.Mode.Closed)
                    searchBar.Open(SearchBarView.Mode.Find, resetQuery: false);
                else
                    searchBar.NavigateNext();
            };
            editorView.FindPrevRequested += (_, _) =>
            {
                if (searchBar.CurrentMode == SearchBarView.Mode.Closed)
                    searchBar.Open(SearchBarView.Mode.Find, resetQuery: false);
                else
                    searchBar.NavigatePrev();
            };
            editorView.ReplaceRequested += (_, _) => searchBar.Open(SearchBarView.Mode.Replace);

            // ── Tab wiring ─────────────────────────────────────────────────

            tabBar.TabActivated      += (_, i) => eventBus.Buffers.Activate(i);
            tabBar.TabCloseRequested += (_, i) => DoCloseTab(i);

            editorView.NextTabRequested  += (_, _) =>
            {
                var count = eventBus.Buffers.Tabs.Count;
                eventBus.Buffers.Activate((eventBus.Buffers.ActiveIndex + 1) % count);
            };
            editorView.PrevTabRequested  += (_, _) =>
            {
                var count = eventBus.Buffers.Tabs.Count;
                eventBus.Buffers.Activate((eventBus.Buffers.ActiveIndex - 1 + count) % count);
            };
            editorView.CloseTabRequested += (_, _) => DoCloseTab(eventBus.Buffers.ActiveIndex);

            void DoCloseTab(int index)
            {
                var buf = eventBus.Buffers.Tabs[index].Buffer;
                if (buf.IsDirty)
                {
                    var name   = buf.FilePath is null ? "Untitled" : Path.GetFileName(buf.FilePath);
                    var choice = MessageBox.Query(app, "Unsaved Changes",
                        $"{name} has unsaved changes. Close anyway?", "Yes", "No");
                    if (choice != 0) return;
                }
                if (eventBus.Buffers.Tabs.Count == 1)
                    eventBus.Buffers.Add(new EmptyBuffer());
                eventBus.Buffers.Close(index);
                RefreshTabBar();
            }

            // ── File tree wiring ───────────────────────────────────────────

            fileTree.FileOpenRequested += (_, path) =>
            {
                try
                {
                    SetActiveBuffer((IMutableTextBuffer)fileService.Open(path));
                    recentFiles.Add(path, RecentKind.File);
                    editorView.SetFocus();
                }
                catch (FileServiceException ex)
                {
                    statusBar.SetMessage(ex.Message);
                }
            };

            editorView.FileTreeToggleRequested += (_, _) =>
            {
                treeUserVisible = !treeUserVisible;
                // Only show if terminal is wide enough
                var shouldShow = treeUserVisible
                    && (fileTree.SuperView?.Viewport.Width ?? 0) >= TreeWidth + MinEditorWidth;
                SetTreeVisible(shouldShow);
                if (fileTree.Visible)
                    fileTree.SetFocus();
                else
                    editorView.SetFocus();
            };

            // Escape in file tree returns focus to editor
            fileTree.KeyDown += (_, key) =>
            {
                if (key.KeyCode == KeyCode.Esc)
                {
                    editorView.SetFocus();
                    key.Handled = true;
                }
            };

            // ── View menu ──────────────────────────────────────────────────

            var wrapItem = new MenuItem("  _Word Wrap", "Alt+Z", () => editorView.ToggleWordWrap());
            editorView.WordWrapChanged += (_, _) =>
                wrapItem.Title = (editorView.WordWrap ? "✓ " : "  ") + "_Word Wrap";

            var tabBarItem = new MenuItem("✓ _Tab Bar", "", () => tabBar.Toggle());
            tabBar.VisibilityChanged += (_, h) =>
            {
                tabBarItem.Title = (h > 0 ? "✓ " : "  ") + "_Tab Bar";
                UpdateEditorLayout();
            };

            var fileTreeItem = new MenuItem("✓ _File Tree", "Ctrl+B", () =>
            {
                treeUserVisible = !treeUserVisible;
                SetTreeVisible(treeUserVisible);
                if (fileTree.Visible) fileTree.SetFocus();
                else                  editorView.SetFocus();
            });
            // Keep checkmark in sync with actual visibility
            fileTree.VisibleChanged += (_, _) =>
                fileTreeItem.Title = (fileTree.Visible ? "✓ " : "  ") + "_File Tree";

            // ── Auto-hide on resize ────────────────────────────────────────

            // ── Recent files helpers ───────────────────────────────────────

            void DoOpenRecentFile(string path)
            {
                try
                {
                    SetActiveBuffer((IMutableTextBuffer)fileService.Open(path));
                    recentFiles.Add(path, RecentKind.File);
                }
                catch (FileServiceException ex)
                {
                    statusBar.SetMessage(ex.Message);
                }
            }

            Menu BuildRecentMenu()
            {
                var entries = recentFiles.Load();
                if (entries.Count == 0)
                {
                    var empty = new MenuItem("(empty)", "", null);
                    empty.Enabled = false;
                    return new Menu([empty]);
                }

                var items = new List<MenuItem>();
                var home  = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var entry in entries)
                {
                    var captured    = entry;
                    var displayPath = captured.Path.StartsWith(home)
                        ? "~" + captured.Path[home.Length..]
                        : captured.Path;
                    var prefix  = captured.Kind == RecentKind.Folder ? "📁 " : "📄 ";
                    var display = prefix + displayPath;
                    if (captured.Kind == RecentKind.Folder)
                        items.Add(new MenuItem(display, "", () => DoOpenFolder(captured.Path)));
                    else
                        items.Add(new MenuItem(display, "", () => DoOpenRecentFile(captured.Path)));
                }
                items.Add(null!);
                items.Add(new MenuItem("_Clear Recent Items", "", () => recentFiles.Clear()));
                return new Menu(items);
            }

            var recentItem = new MenuItem("Open _Recent", "", BuildRecentMenu());
            recentItem.Accepting += (_, _) => recentItem.SubMenu = BuildRecentMenu();

            var undoItem = new MenuItem("_Undo", "Ctrl+Z", DoUndo);
            var redoItem = new MenuItem("_Redo", "Ctrl+Y", DoRedo);

            void RefreshUndoRedo()
            {
                undoItem.Enabled = eventBus.CanUndo;
                redoItem.Enabled = eventBus.CanRedo;
            }

            eventBus.EventExecuted += (_, _) => RefreshUndoRedo();
            eventBus.EventUndone   += (_, _) => RefreshUndoRedo();
            eventBus.EventRedone   += (_, _) => RefreshUndoRedo();
            RefreshUndoRedo();

            // ── Help dialogs ───────────────────────────────────────────────

            const string KeyboardShortcutsText =
                "FILE\n" +
                "  Ctrl+N          New file\n" +
                "  Ctrl+O          Open file\n" +
                "  Ctrl+S          Save\n" +
                "\n" +
                "EDIT\n" +
                "  Ctrl+Z          Undo\n" +
                "  Ctrl+Y          Redo\n" +
                "  Ctrl+X          Cut\n" +
                "  Ctrl+C          Copy\n" +
                "  Ctrl+V          Paste\n" +
                "  Ctrl+A          Select all\n" +
                "  Tab             Indent selection\n" +
                "  Shift+Tab       Dedent selection\n" +
                "\n" +
                "SEARCH\n" +
                "  Ctrl+F          Find\n" +
                "  F3              Find next\n" +
                "  Shift+F3        Find previous\n" +
                "  Ctrl+H          Find and replace\n" +
                "\n" +
                "NAVIGATION\n" +
                "  Ctrl+Left/Right Word left / right\n" +
                "  Ctrl+Up/Down    Scroll up / down\n" +
                "  Home / End      Line start / end\n" +
                "  Ctrl+Home/End   Document start / end\n" +
                "\n" +
                "VIEW\n" +
                "  Ctrl+B          Toggle file tree\n" +
                "  Alt+Z           Toggle word wrap\n";

            void DoKeyboardShortcuts()
            {
#pragma warning disable CS0618 // Terminal.Gui's deprecation notice points to their own editor; TextView is correct for a read-only dialog
                var textView = new TextView
                {
                    X        = 0,
                    Y        = 0,
                    Width    = Dim.Fill(),
                    Height   = Dim.Fill() - Dim.Absolute(1),
                    ReadOnly = true,
                    Text     = KeyboardShortcutsText,
                };
#pragma warning restore CS0618
                var dlg = new Dialog
                {
                    Title  = "Keyboard Shortcuts",
                    Width  = 60,
                    Height = 24,
                };
                var ok = new Button { Text = "OK", IsDefault = true };
                ok.Accepting += (_, _) => app.RequestStop(dlg);
                dlg.Add(textView, ok);
                ok.X = Pos.Center();
                ok.Y = Pos.Bottom(textView);
                app.Run(dlg);
            }

            void DoAbout()
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                var verStr = ver is null ? "" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
                MessageBox.Query(app, "About", $"code-edit  v{verStr}\n\nA lightweight TUI code editor.", "OK");
            }

            editorView.KeyboardShortcutsRequested += (_, _) => DoKeyboardShortcuts();

            // ── Menu bar ───────────────────────────────────────────────────

            var menuBar = new MenuBar(
            [
                new MenuBarItem("_File",
                [
                    new MenuItem("_New",           "Ctrl+N", DoNew),
                    new MenuItem("Open _File…",    "Ctrl+O", DoOpenFile),
                    new MenuItem("Open F_older…",  "",       () => DoOpenFolder()),
                    recentItem,
                    null!,
                    new MenuItem("_Save",          "Ctrl+S", DoSave),
                    new MenuItem("Save _As…",      "",       DoSaveAs),
                    null!,
                    new MenuItem("E_xit",          "",       DoQuit),
                ]),
                new MenuBarItem("_Edit",
                [
                    undoItem,
                    redoItem,
                    null!,
                    new MenuItem("Cu_t",   "Ctrl+X", DoCut),
                    new MenuItem("_Copy",  "Ctrl+C", DoCopy),
                    new MenuItem("_Paste", "Ctrl+V", DoPaste),
                ]),
                new MenuBarItem("_Search",
                [
                    new MenuItem("_Find",          "Ctrl+F",   () => searchBar.Open(SearchBarView.Mode.Find)),
                    new MenuItem("Find _Next",      "F3",       () =>
                    {
                        if (searchBar.CurrentMode == SearchBarView.Mode.Closed)
                            searchBar.Open(SearchBarView.Mode.Find, resetQuery: false);
                        else
                            searchBar.NavigateNext();
                    }),
                    new MenuItem("Find _Previous",  "Shift+F3", () =>
                    {
                        if (searchBar.CurrentMode == SearchBarView.Mode.Closed)
                            searchBar.Open(SearchBarView.Mode.Find, resetQuery: false);
                        else
                            searchBar.NavigatePrev();
                    }),
                    new MenuItem("_Replace",        "Ctrl+H",   () => searchBar.Open(SearchBarView.Mode.Replace)),
                ]),
                new MenuBarItem("_View",
                [
                    fileTreeItem,
                    tabBarItem,
                    wrapItem,
                    null!,
                    new MenuItem("Edit _Theme…", "", () =>
                    {
                        var dlg = new ThemeEditorDialog(themeSvc, themeRegistry, settingsSvc);
                        app.Run(dlg);
                    }),
                ]),
                new MenuBarItem("_Help",
                [
                    new MenuItem("_Keyboard Shortcuts", "F1", DoKeyboardShortcuts),
                    new MenuItem("_About",              "",   DoAbout),
                ]),
            ]);

            // ── Layout ─────────────────────────────────────────────────────

            tabBar.X      = 0;
            tabBar.Y      = Pos.Bottom(menuBar);
            tabBar.Width  = Dim.Fill();
            tabBar.Height = Dim.Absolute(1);

            fileTree.X      = 0;
            fileTree.Y      = Pos.Bottom(tabBar);
            fileTree.Width  = Dim.Absolute(TreeWidth);
            fileTree.Height = Dim.Fill() - Dim.Absolute(1);

            editorView.X      = Pos.Absolute(TreeWidth);
            editorView.Y      = Pos.Bottom(tabBar);
            editorView.Width  = Dim.Fill();   // Fill from X, which already accounts for tree width
            editorView.Height = Dim.Fill() - Dim.Absolute(1);

            searchBar.X      = 0;
            searchBar.Y      = Pos.AnchorEnd(1);
            searchBar.Width  = Dim.Fill();
            searchBar.Height = Dim.Absolute(0);

            statusBar.X      = 0;
            statusBar.Y      = Pos.AnchorEnd(1);
            statusBar.Width  = Dim.Fill();
            statusBar.Height = Dim.Absolute(1);

            using var window = new Window { Title = "code-edit" };
            window.Add(menuBar, tabBar, fileTree, editorView, searchBar, statusBar);

            // Auto-hide file tree when terminal is too narrow
            window.FrameChanged += (_, _) =>
            {
                var shouldShow = treeUserVisible && window.Viewport.Width >= TreeWidth + MinEditorWidth;
                if (shouldShow != fileTree.Visible)
                    SetTreeVisible(shouldShow);
            };

            editorView.SetFocus();

            if (cmdOpenError is not null)
                app.Invoke(() => MessageBox.Query(app, "Error", cmdOpenError, "OK"));

            app.Run(window);
            logger.LogInformation("code-edit stopped");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unhandled exception — exiting");
            try { MessageBox.Query(app, "Fatal Error", $"An unexpected error occurred:\n{ex.Message}", "OK"); }
            catch { /* terminal may be unavailable */ }
            // Exit gracefully — error was logged and shown; don't crash with a stack trace
        }
    }
}
