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
        services.AddSingleton<ISyntaxDetector, ExtensionShebangSyntaxDetector>();
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
            var editorView = provider.GetRequiredService<EditorView>();
            var statusBar  = provider.GetRequiredService<StatusBarView>();
            var eventBus   = provider.GetRequiredService<IEventBus>();

            // Open file from command line or start with an empty buffer
            IMutableTextBuffer buffer;
            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Length > 1 && !string.IsNullOrWhiteSpace(cmdArgs[1]))
            {
                var fileService = provider.GetRequiredService<IFileService>();
                buffer = (IMutableTextBuffer)fileService.Open(cmdArgs[1]);
                logger.LogInformation("Opened file: {Path}", cmdArgs[1]);
            }
            else
            {
                buffer = new EmptyBuffer();
            }

            eventBus.SetBuffer(buffer);
            editorView.SetBuffer(buffer);

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

            var wrapItem = new MenuItem("  _Word Wrap", "Alt+Z", () => editorView.ToggleWordWrap());
            editorView.WordWrapChanged += (_, _) =>
                wrapItem.Title = (editorView.WordWrap ? "✓ " : "  ") + "_Word Wrap";

            // Build menu bar
            var menuBar = new MenuBar(
            [
                new MenuBarItem("_File",
                [
                    new MenuItem("_Open", "", null),
                    new MenuItem("_Save", "", null),
                    new MenuItem("_Quit", "", () => app.RequestStop()),
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

            // Layout
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
