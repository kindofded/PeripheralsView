namespace PeripheralsView;

/// <summary>Icon colours (straight-alpha ARGB), from the Windows 11 palette, with dark- and light-taskbar variants.</summary>
internal static class Palette
{
    public static uint Normal(bool light) => light ? 0xFF1C1C1C : 0xFFFFFFFF;
    public static uint Low(bool light) => light ? 0xFF9D5D00 : 0xFFFCE100;
    public static uint Critical(bool light) => light ? 0xFFC42B1C : 0xFFFF99A4;
    public static uint Success(bool light) => light ? 0xFF0F7B0F : 0xFF6CCB5F;

    // Level → colour stops; between stops the colour is blended. Light variants are darker to read on a light taskbar.
    private static readonly (int Level, uint Dark, uint Light)[] Stops =
    [
        (5, 0xFFFF6B6B, 0xFFC42B1C), // red
        (20, 0xFFFF9A3C, 0xFFCA5010), // orange
        (40, 0xFFFCE100, 0xFF9D7A00), // yellow
        (70, 0xFF6CCB5F, 0xFF0F7B0F), // green
    ];

    public static uint ForLevel(int level, bool light)
    {
        if (level <= Stops[0].Level)
            return light ? Stops[0].Light : Stops[0].Dark;
        for (int i = 1; i < Stops.Length; i++)
        {
            if (level <= Stops[i].Level)
            {
                var (loLevel, loDark, loLight) = Stops[i - 1];
                var (hiLevel, hiDark, hiLight) = Stops[i];
                float t = (float)(level - loLevel) / (hiLevel - loLevel);
                return Blend(light ? loLight : loDark, light ? hiLight : hiDark, t);
            }
        }
        return light ? Stops[^1].Light : Stops[^1].Dark;
    }

    private static uint Blend(uint a, uint b, float t)
    {
        uint Channel(int shift) => (uint)MathF.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }
}
