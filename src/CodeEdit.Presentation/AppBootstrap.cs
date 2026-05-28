using CodeEdit.Application;
using CodeEdit.Application.Events;
using CodeEdit.Application.Ports;
using CodeEdit.Domain;
using CodeEdit.Infrastructure.Buffer;
using CodeEdit.Infrastructure.Logging;
using CodeEdit.Infrastructure.Syntax;
using CodeEdit.Infrastructure.Theme;
using CodeEdit.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TGuiApp = Terminal.Gui.App.Application;

namespace CodeEdit.Presentation;

public static class AppBootstrap
{
    public static void Run()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".code-edit", "logs");
        Directory.CreateDirectory(logDir);

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
        services.AddSingleton<EditorView>();
        provider = services.BuildServiceProvider();

        try
        {
            var editorView  = provider.GetRequiredService<EditorView>();
            var statusBar   = provider.GetRequiredService<StatusBarView>();
            var eventBus    = provider.GetRequiredService<IEventBus>();
            var fileService = provider.GetRequiredService<IFileService>();

            // Open file from command line or start with an empty buffer
            IMutableTextBuffer buffer;
            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Length > 1 && !string.IsNullOrWhiteSpace(cmdArgs[1]))
            {
                buffer = (IMutableTextBuffer)fileService.Open(cmdArgs[1]);
                logger.LogInformation("Opened file: {Path}", cmdArgs[1]);
            }
            else
            {
                buffer = new EmptyBuffer();
            }

            eventBus.SetBuffer(buffer);
            editorView.SetBuffer(buffer);

            // ── File helpers ───────────────────────────────────────────────

            void SetActiveBuffer(IMutableTextBuffer newBuf)
            {
                eventBus.SetBuffer(newBuf);
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

            void DoOpen()
            {
                if (eventBus.Buffer.IsDirty)
                {
                    var choice = MessageBox.Query(app, "Unsaved Changes", "You have unsaved changes.\nOpen a new file anyway?", "Yes", "No");
                    if (choice != 0) return;
                }

                var dlg = new OpenDialog { MustExist = true };
                app.Run(dlg);

                if (dlg.Canceled || dlg.FilePaths.Count == 0) return;

                var path = dlg.FilePaths[0].ToString()!;
                try
                {
                    SetActiveBuffer((IMutableTextBuffer)fileService.Open(path));
                }
                catch (FileServiceException ex)
                {
                    statusBar.SetMessage(ex.Message);
                }
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
                    SetActiveBuffer((IMutableTextBuffer)fileService.SaveAs(eventBus.Buffer, path));
                }
                catch (FileServiceException ex)
                {
                    statusBar.SetMessage(ex.Message);
                }
            }

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
            editorView.OpenRequested += (_, _) => DoOpen();
            editorView.SaveRequested += (_, _) => DoSave();

            // ── View menu ──────────────────────────────────────────────────

            var wrapItem = new MenuItem("  _Word Wrap", "Alt+Z", () => editorView.ToggleWordWrap());
            editorView.WordWrapChanged += (_, _) =>
                wrapItem.Title = (editorView.WordWrap ? "✓ " : "  ") + "_Word Wrap";

            // ── Menu bar ───────────────────────────────────────────────────

            var menuBar = new MenuBar(
            [
                new MenuBarItem("_File",
                [
                    new MenuItem("_New",      "Ctrl+N", DoNew),
                    new MenuItem("_Open",     "Ctrl+O", DoOpen),
                    new MenuItem("_Save",     "Ctrl+S", DoSave),
                    new MenuItem("Save _As…", "",       DoSaveAs),
                    null!,
                    new MenuItem("_Quit", "", DoQuit),
                ]),
                new MenuBarItem("_Edit",
                [
                    new MenuItem("Cu_t",   "Ctrl+X", DoCut),
                    new MenuItem("_Copy",  "Ctrl+C", DoCopy),
                    new MenuItem("_Paste", "Ctrl+V", DoPaste),
                ]),
                new MenuBarItem("_View",
                [
                    wrapItem,
                ]),
            ]);

            // ── Layout ─────────────────────────────────────────────────────

            editorView.X      = 0;
            editorView.Y      = Pos.Bottom(menuBar);
            editorView.Width  = Dim.Fill();
            editorView.Height = Dim.Fill() - Dim.Absolute(1);

            statusBar.X      = 0;
            statusBar.Y      = Pos.AnchorEnd(1);
            statusBar.Width  = Dim.Fill();
            statusBar.Height = Dim.Absolute(1);

            using var window = new Window { Title = "code-edit" };
            window.Add(menuBar, editorView, statusBar);
            editorView.SetFocus();

            app.Run(window);
            logger.LogInformation("code-edit stopped");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unhandled exception — exiting");
            throw;
        }
    }
}
