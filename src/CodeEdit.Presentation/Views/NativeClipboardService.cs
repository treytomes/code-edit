using System.Diagnostics;
using System.Runtime.InteropServices;
using CodeEdit.Application.Ports;

namespace CodeEdit.Presentation.Views;

/// <summary>
/// OS clipboard via native tools. Terminal.Gui's built-in clipboard never wires up UnixClipboard
/// on plain Linux (only Windows/macOS/WSL), so we bypass it entirely.
/// </summary>
public sealed class NativeClipboardService : IClipboardService
{
    private enum Backend { Unsupported, Wayland, Xclip, Xsel, MacOs, Windows }

    private readonly Backend _backend;

    public NativeClipboardService() => _backend = Detect();

    public bool IsSupported => _backend != Backend.Unsupported;

    public bool TryGet(out string text)
    {
        text = string.Empty;
        return _backend switch
        {
            Backend.Wayland => RunRead("wl-paste", "--no-newline", out text),
            Backend.Xclip   => RunRead("xclip", "-selection clipboard -o", out text),
            Backend.Xsel    => RunRead("xsel", "--clipboard --output", out text),
            Backend.MacOs   => RunRead("pbpaste", "", out text),
            Backend.Windows => WindowsGet(out text),
            _               => false,
        };
    }

    public bool TrySet(string text)
    {
        return _backend switch
        {
            Backend.Wayland => RunWrite("wl-copy", "", text),
            Backend.Xclip   => RunWrite("xclip", "-selection clipboard -i", text),
            Backend.Xsel    => RunWrite("xsel", "--clipboard --input", text),
            Backend.MacOs   => RunWrite("pbcopy", "", text),
            Backend.Windows => WindowsSet(text),
            _               => false,
        };
    }

    private static Backend Detect()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Backend.Windows;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return CommandExists("pbcopy") ? Backend.MacOs : Backend.Unsupported;

        // Linux — prefer Wayland when the display is active
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            && CommandExists("wl-copy") && CommandExists("wl-paste"))
            return Backend.Wayland;

        if (CommandExists("xclip"))  return Backend.Xclip;
        if (CommandExists("xsel"))   return Backend.Xsel;

        return Backend.Unsupported;
    }

    private static bool CommandExists(string cmd)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName               = "which",
                Arguments              = cmd,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            });
            p?.WaitForExit(2000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool RunRead(string cmd, string args, out string text)
    {
        text = string.Empty;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName               = cmd,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            });
            if (p is null) return false;
            text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool RunWrite(string cmd, string args, string text)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName              = cmd,
                Arguments             = args,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute       = false,
            });
            if (p is null) return false;
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool WindowsGet(out string text)
    {
        text = string.Empty;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = "-noprofile -command Get-Clipboard",
                RedirectStandardOutput = true,
                UseShellExecute        = false,
            });
            if (p is null) return false;
            text = p.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool WindowsSet(string text)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName              = "powershell.exe",
                Arguments             = "-noprofile -command \"$input | Set-Clipboard\"",
                RedirectStandardInput = true,
                UseShellExecute       = false,
            });
            if (p is null) return false;
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
