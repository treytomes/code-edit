namespace CodeEdit.Domain;

public readonly record struct Rgb(byte R, byte G, byte B);

public readonly record struct ColorPair(Rgb Foreground, Rgb Background)
{
    public static ColorPair Of(byte fR, byte fG, byte fB, byte bR, byte bG, byte bB)
        => new(new Rgb(fR, fG, fB), new Rgb(bR, bG, bB));
}
