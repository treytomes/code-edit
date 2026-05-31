using CodeEdit.Domain;
using Terminal.Gui.Drawing;

namespace CodeEdit.Presentation.Views;

// Only class permitted to reference both ColorPair and Terminal.Gui.Attribute.
public static class ColorPairMapper
{
    public static Terminal.Gui.Drawing.Attribute ToAttribute(ColorPair pair)
        => new(
            new Color(pair.Foreground.R, pair.Foreground.G, pair.Foreground.B, 255),
            new Color(pair.Background.R, pair.Background.G, pair.Background.B, 255));
}
