using Retro.Crt;

namespace Husky;

internal static class Banner
{
    // Pure-ASCII figlet-style logo: charm without UTF-8 mojibake when
    // the cast lands in a clipboard, terminal, or text editor that
    // doesn't speak the same encoding as the launcher's stdout. The
    // Crt.Banner.Gradient call still applies the cool ice-blue ->
    // bright cyan vertical gradient, so it lights up just fine on a
    // truecolor terminal even though the glyphs themselves are ASCII.
    private static readonly string[] Logo =
    [
        @"   _   _ _   _ ____  _  ____   __",
        @"  | | | | | | / ___|| |/ /\ \ / /",
        @"  | |_| | | | \___ \| ' /  \ V / ",
        @"  |  _  | |_| |___) | . \   | |  ",
        @"  |_| |_|\___/|____/|_|\_\  |_|  ",
    ];

    // Husky-fur palette: cool ice-blue at the top, brighter cyan at the
    // belly. Gradient endpoints are truecolor; on Standard16 terminals Crt
    // silently falls back to the first colour, which still reads as the
    // banner's signature cyan.
    private static readonly Color GradientFrom = Color.Rgb(120, 180, 220);
    private static readonly Color GradientTo   = Color.Rgb(150, 240, 255);

    public static void Render(string version)
    {
        Retro.Crt.Banner.Gradient(Logo, from: GradientFrom, to: GradientTo);
        Crt.WriteLine();

        Crt.Write("  ");
        using (Crt.WithStyle(fg: Color.LightCyan, bold: true)) Crt.Write("Husky");
        Crt.Write(" ");
        using (Crt.WithStyle(fg: Color.DarkGray)) Crt.Write($"v{version}");
        Crt.WriteLine();

        using (Crt.WithStyle(fg: Color.DarkGray)) Crt.WriteLine("  your loyal app launcher");
        Crt.WriteLine();
    }
}
