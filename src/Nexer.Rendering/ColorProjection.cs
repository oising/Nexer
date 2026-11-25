using Nexer.Core;

namespace Nexer.Rendering;

/// <summary>Maps colours to what a <see cref="ColorMode"/> can display (xterm default palette).</summary>
internal static class ColorProjection
{
    private static readonly uint[] Ansi16 =
    [
        0x000000, 0xcd0000, 0x00cd00, 0xcdcd00, 0x0000ee, 0xcd00cd, 0x00cdcd, 0xe5e5e5,
        0x7f7f7f, 0xff0000, 0x00ff00, 0xffff00, 0x5c5cff, 0xff00ff, 0x00ffff, 0xffffff,
    ];

    private static ReadOnlySpan<byte> CubeLevels => [0, 95, 135, 175, 215, 255];

    public static CellStyle Project(CellStyle style, ColorMode mode) =>
        mode == ColorMode.TrueColor ? style
            : new CellStyle(Project(style.Foreground, mode), Project(style.Background, mode), style.Flags);

    public static TerminalColor Project(TerminalColor color, ColorMode mode)
    {
        switch (mode)
        {
            case ColorMode.Indexed256 when color.Kind == TerminalColorKind.Rgb:
                return TerminalColor.Indexed(NearestIndexed256(color.Value));
            case ColorMode.Indexed16 when color.Kind == TerminalColorKind.Rgb:
                return TerminalColor.Indexed(Nearest16(color.Value));
            case ColorMode.Indexed16 when color.Kind == TerminalColorKind.Indexed && color.Value >= 16:
                return TerminalColor.Indexed(Nearest16(IndexedToRgb((byte)color.Value)));
            default:
                return color;
        }
    }

    public static uint IndexedToRgb(byte index)
    {
        if (index < 16) return Ansi16[index];
        if (index >= 232)
        {
            int gray = 8 + (index - 232) * 10;
            return (uint)(gray << 16 | gray << 8 | gray);
        }

        int cube = index - 16;
        return (uint)(CubeLevels[cube / 36] << 16 | CubeLevels[cube / 6 % 6] << 8 | CubeLevels[cube % 6]);
    }

    private static byte NearestIndexed256(uint rgb)
    {
        int r = (int)(rgb >> 16 & 0xff), g = (int)(rgb >> 8 & 0xff), b = (int)(rgb & 0xff);
        int ri = NearestCube(r), gi = NearestCube(g), bi = NearestCube(b);
        int cubeIndex = 16 + 36 * ri + 6 * gi + bi;
        int cubeDistance = Distance(rgb, IndexedToRgb((byte)cubeIndex));
        int average = (r + g + b) / 3;
        int grayStep = Math.Clamp((average - 8 + 5) / 10, 0, 23);
        int grayIndex = 232 + grayStep;
        return Distance(rgb, IndexedToRgb((byte)grayIndex)) < cubeDistance ? (byte)grayIndex : (byte)cubeIndex;
    }

    private static int NearestCube(int value)
    {
        int best = 0;
        for (int i = 1; i < CubeLevels.Length; i++)
        {
            if (Math.Abs(CubeLevels[i] - value) < Math.Abs(CubeLevels[best] - value)) best = i;
        }

        return best;
    }

    private static byte Nearest16(uint rgb)
    {
        int best = 0, bestDistance = int.MaxValue;
        for (int i = 0; i < Ansi16.Length; i++)
        {
            int distance = Distance(rgb, Ansi16[i]);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return (byte)best;
    }

    private static int Distance(uint a, uint b)
    {
        int dr = (int)(a >> 16 & 0xff) - (int)(b >> 16 & 0xff);
        int dg = (int)(a >> 8 & 0xff) - (int)(b >> 8 & 0xff);
        int db = (int)(a & 0xff) - (int)(b & 0xff);
        return dr * dr + dg * dg + db * db;
    }
}
