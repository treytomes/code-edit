using CodeEdit.Domain;
using Terminal.Gui.Drawing;

namespace CodeEdit.Presentation.Views;

// Only class permitted to reference both ColorPair and Terminal.Gui.Attribute.
public static class ColorPairMapper
{
    public static Terminal.Gui.Drawing.Attribute ToAttribute(ColorPair pair)
        => new((ColorName16)pair.Foreground, (ColorName16)pair.Background);
}
