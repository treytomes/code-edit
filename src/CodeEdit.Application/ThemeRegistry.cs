using CodeEdit.Application.Ports;

namespace CodeEdit.Application;

public class ThemeRegistry(IColorTheme initial)
{
    public IColorTheme Active { get; private set; } = initial;

    public event EventHandler? ThemeChanged;

    public void SetTheme(IColorTheme theme)
    {
        Active = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}
