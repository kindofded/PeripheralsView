namespace PeripheralsView;

internal enum Badge
{
    None,
    Charging,
    Asleep,
    Offline,
}

/// <param name="Level">0–100 fills the glyph; -1 draws the outline only.</param>
/// <param name="Color">Straight-alpha ARGB.</param>
internal readonly record struct IconSpec(DeviceKind Kind, int Level, Badge Badge, uint Color, bool ShowNumber, int Size);

/// <summary>
/// Draws tray icons in code: glyphs are signed-distance shapes on a 16-unit grid (anti-aliased at any DPI),
/// numbers are GDI text. No image files, no GDI+.
/// </summary>
internal static unsafe class IconRenderer
{
    private const string NumberFont = "Segoe UI";
    private const float Stroke = 1.5f;
    private const float BadgeX = 12.25f, BadgeY = 12.25f, BadgeCut = 4.75f;

    public static nint CreateIcon(in IconSpec spec) => ToIcon(Render(spec), spec.Size);

    /// <summary>Straight-alpha ARGB pixels, top-down rows.</summary>
    public static uint[] Render(in IconSpec spec)
    {
        int n = spec.Size;
        var alpha = new float[n * n];
        bool number = spec.ShowNumber && spec.Level >= 0 && spec.Badge != Badge.Offline;
        if (number)
            DrawNumber(alpha, n, spec.Level);
        else
            DrawGlyph(alpha, n, spec);

        float opacity = spec.Badge == Badge.Offline || (number && spec.Badge == Badge.Asleep) ? 0.6f : 1f;
        var pixels = new uint[n * n];
        uint rgb = spec.Color & 0x00FFFFFF;
        float colorAlpha = (spec.Color >> 24) / 255f;
        for (int i = 0; i < pixels.Length; i++)
        {
            uint a = (uint)MathF.Round(Math.Clamp(alpha[i], 0, 1) * opacity * colorAlpha * 255);
            pixels[i] = a == 0 ? 0 : a << 24 | rgb;
        }
        return pixels;
    }

    private static void DrawGlyph(float[] alpha, int n, in IconSpec spec)
    {
        float unit = 16f / n; // grid units per pixel: the anti-aliasing width
        float level = spec.Level < 0 ? -1 : Math.Clamp(spec.Level, 0, 100) / 100f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float px = (x + 0.5f) * unit, py = (y + 0.5f) * unit;
                float a = spec.Kind switch
                {
                    DeviceKind.Mouse => Mouse(px, py, level, unit),
                    DeviceKind.Keyboard => Keyboard(px, py, level, unit),
                    DeviceKind.Headset => Headset(px, py, level, unit),
                    DeviceKind.Earbuds => Earbuds(px, py, level, unit),
                    _ => Battery(px, py, level, unit),
                };

                if (spec.Badge is Badge.Charging or Badge.Asleep)
                {
                    a *= 1 - Cover(Circle(px, py, BadgeX, BadgeY, BadgeCut), unit);
                    float bx = px - BadgeX, by = py - BadgeY;
                    float shape = spec.Badge == Badge.Charging
                        ? Bolt(bx, by)
                        : MathF.Max(Circle(bx, by, 0, 0, 3.4f), -Circle(bx, by, 1.7f, -1.5f, 2.7f)); // crescent moon
                    a = MathF.Max(a, Cover(shape, unit));
                }
                else if (spec.Badge == Badge.Offline)
                {
                    a *= 1 - Cover(Segment(px, py, 1.5f, 1.5f, 14.5f, 14.5f, 2.1f), unit);
                    a = MathF.Max(a, Cover(Segment(px, py, 1.5f, 1.5f, 14.5f, 14.5f, 0.85f), unit));
                }
                alpha[y * n + x] = a;
            }
        }
    }

    // ---------------------------------------------------------------- glyphs (16-unit grid, outline stroke inside the edge)

    private static float Mouse(float px, float py, float level, float unit)
    {
        float body = RoundRect(px, py, 8, 8, 4.75f, 7.25f, 4.75f);
        float a = Cover(Ring(body), unit);
        float wheel = Segment(px, py, 8, 3.6f, 8, 5.4f, 0.7f);
        if (level >= 0)
        {
            float inner = RoundRect(px, py, 8, 8, 2.25f, 4.75f, 2.25f);
            float top = 12.75f - level * 9.5f;
            float fill = Cover(MathF.Max(inner, top - py), unit) * (1 - Cover(wheel - 0.8f, unit));
            a = MathF.Max(a, fill);
        }
        return MathF.Max(a, Cover(wheel, unit));
    }

    private static float Keyboard(float px, float py, float level, float unit)
    {
        float body = RoundRect(px, py, 8, 8, 7.25f, 4.75f, 2f);
        float a = Cover(Ring(body), unit);
        if (level < 0)
            return a;

        // 2 × 4 keys filling 3.25..12.75 × 5.75..10.25, lit left to right.
        const float left = 3.25f, top = 5.75f, gap = 1f, keyW = 1.625f, keyH = 1.75f;
        int col = Math.Clamp((int)((px - left) / (keyW + gap)), 0, 3);
        int row = Math.Clamp((int)((py - top) / (keyH + gap)), 0, 1);
        float key = RoundRect(px, py, left + col * (keyW + gap) + keyW / 2, top + row * (keyH + gap) + keyH / 2, keyW / 2, keyH / 2, 0.3f);
        float right = left + level * 9.5f;
        return MathF.Max(a, Cover(MathF.Max(key, px - right), unit));
    }

    /// <summary>Headphones: the ear cups fill from the bottom like a liquid level.</summary>
    private static float Headset(float px, float py, float level, float unit)
    {
        float band = MathF.Abs(Circle(px, py, 8, 9, 6.25f) + Stroke / 2) - Stroke / 2;
        float a = Cover(MathF.Max(band, py - 8), unit); // stop at the cups' top edge
        float top = 15.25f - Math.Max(level, 0) * 7.5f;
        foreach (float cx in (ReadOnlySpan<float>)[3.5f, 12.5f])
        {
            float cup = RoundRect(px, py, cx, 11.5f, 2.75f, 3.75f, 1.5f);
            a = MathF.Max(a, Cover(Ring(cup), unit));
            if (level >= 0)
                a = MathF.Max(a, Cover(MathF.Max(cup, top - py), unit));
        }
        return a;
    }

    /// <summary>Two buds, round heads with stems hanging down and inwards; both fill from the bottom.</summary>
    private static float Earbuds(float px, float py, float level, float unit)
    {
        float a = 0;
        float top = 13.75f - Math.Max(level, 0) * 11.25f;
        foreach (float side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            float head = Circle(px, py, 8 + side * 3.9f, 5.4f, 2.9f);
            float stem = Segment(px, py, 8 + side * 3.2f, 7.5f, 8 + side * 2.4f, 12.5f, 1.25f);
            float bud = MathF.Min(head, stem);
            a = MathF.Max(a, Cover(Ring(bud), unit));
            if (level >= 0)
                a = MathF.Max(a, Cover(MathF.Max(bud, top - py), unit));
        }
        return a;
    }

    private static float Battery(float px, float py, float level, float unit)
    {
        float body = RoundRect(px, py, 7.25f, 8, 6.5f, 4.25f, 1.5f);
        float a = MathF.Max(Cover(Ring(body), unit), Cover(RoundRect(px, py, 14.75f, 8, 1f, 1.75f, 0.5f), unit));
        if (level >= 0)
        {
            float inner = RoundRect(px, py, 7.25f, 8, 4f, 1.75f, 0.5f);
            a = MathF.Max(a, Cover(MathF.Max(inner, px - (3.25f + level * 8f)), unit));
        }
        return a;
    }

    private static readonly (float X, float Y)[] BoltPoints =
        [(1f, -3.9f), (-2.4f, 0.55f), (-0.15f, 0.55f), (-1f, 3.9f), (2.4f, -0.55f), (0.15f, -0.55f)];

    private static float Bolt(float px, float py) => Polygon(px, py, BoltPoints);

    // ---------------------------------------------------------------- signed distance helpers (negative = inside)

    private static float Cover(float distance, float unit) => Math.Clamp(0.5f - distance / unit, 0, 1);

    private static float Ring(float body) => MathF.Max(body, -(body + Stroke));

    private static float Circle(float px, float py, float cx, float cy, float r) => MathF.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - r;

    private static float RoundRect(float px, float py, float cx, float cy, float hx, float hy, float r)
    {
        float qx = MathF.Abs(px - cx) - hx + r, qy = MathF.Abs(py - cy) - hy + r;
        float ox = MathF.Max(qx, 0), oy = MathF.Max(qy, 0);
        return MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(qx, qy), 0) - r;
    }

    private static float Segment(float px, float py, float ax, float ay, float bx, float by, float r)
    {
        float pax = px - ax, pay = py - ay, bax = bx - ax, bay = by - ay;
        float h = Math.Clamp((pax * bax + pay * bay) / (bax * bax + bay * bay), 0, 1);
        float dx = pax - bax * h, dy = pay - bay * h;
        return MathF.Sqrt(dx * dx + dy * dy) - r;
    }

    private static float Polygon(float px, float py, (float X, float Y)[] v)
    {
        float d = (px - v[0].X) * (px - v[0].X) + (py - v[0].Y) * (py - v[0].Y);
        float sign = 1;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            float ex = v[j].X - v[i].X, ey = v[j].Y - v[i].Y;
            float wx = px - v[i].X, wy = py - v[i].Y;
            float h = Math.Clamp((wx * ex + wy * ey) / (ex * ex + ey * ey), 0, 1);
            float bx = wx - ex * h, by = wy - ey * h;
            d = MathF.Min(d, bx * bx + by * by);
            bool c1 = py >= v[i].Y, c2 = py < v[j].Y, c3 = ex * wy > ey * wx;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
                sign = -sign;
        }
        return sign * MathF.Sqrt(d);
    }

    // ---------------------------------------------------------------- number

    /// <summary>
    /// GDI draws grayscale-antialiased white text on black; the red channel becomes coverage. The text is drawn
    /// on a larger scratch bitmap and its ink box is centred, because GDI centres the font cell, not the digits.
    /// </summary>
    private static void DrawNumber(float[] alpha, int n, int level)
    {
        string text = Math.Clamp(level, 0, 100).ToString();
        int scratch = n * 2;
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Native.BITMAPINFOHEADER),
            biWidth = scratch,
            biHeight = -scratch,
            biPlanes = 1,
            biBitCount = 32,
        };
        void* bits;
        nint dc = Native.CreateCompatibleDC(0);
        nint bitmap = Native.CreateDIBSection(dc, &header, 0, &bits, 0, 0);
        nint oldBitmap = Native.SelectObject(dc, bitmap);
        Native.SetTextColor(dc, 0x00FFFFFF);
        Native.SetBkMode(dc, Native.TRANSPARENT);
        var pixels = new Span<uint>(bits, scratch * scratch);

        for (int em = (int)(n * 1.25f); em >= 6; em--)
        {
            nint font = Native.CreateFontW(-em, 0, 0, 0, Native.FW_SEMIBOLD, 0, 0, 0, 0, 0, 0, Native.ANTIALIASED_QUALITY, 0, NumberFont);
            nint oldFont = Native.SelectObject(dc, font);
            pixels.Clear();
            var rect = new Native.RECT { Right = scratch, Bottom = scratch };
            fixed (char* p = text)
                Native.DrawTextW(dc, p, text.Length, &rect, 0x1 /* DT_CENTER */ | 0x4 /* DT_VCENTER */ | Native.DT_SINGLELINE | Native.DT_NOPREFIX | Native.DT_NOCLIP);
            Native.GdiFlush();
            Native.SelectObject(dc, oldFont);
            Native.DeleteObject(font);

            int minX = scratch, minY = scratch, maxX = -1, maxY = -1;
            for (int y = 0; y < scratch; y++)
            {
                for (int x = 0; x < scratch; x++)
                {
                    if ((pixels[y * scratch + x] >> 16 & 0xFF) > 8)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }
            int width = maxX - minX + 1, height = maxY - minY + 1;
            if (maxX >= 0 && (width > n || height > n * 0.8f) && em > 6)
                continue;

            int offsetX = (n - width) / 2 - minX, offsetY = (n - height + 1) / 2 - minY;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int sx = x - offsetX, sy = y - offsetY;
                    if (sx >= 0 && sy >= 0 && sx < scratch && sy < scratch)
                        alpha[y * n + x] = (pixels[sy * scratch + sx] >> 16 & 0xFF) / 255f;
                }
            }
            break;
        }

        Native.SelectObject(dc, oldBitmap);
        Native.DeleteObject(bitmap);
        Native.DeleteDC(dc);
    }

    // ---------------------------------------------------------------- HICON

    private static nint ToIcon(uint[] pixels, int size)
    {
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Native.BITMAPINFOHEADER),
            biWidth = size,
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
        };
        void* bits;
        nint color = Native.CreateDIBSection(0, &header, 0, &bits, 0, 0);
        pixels.AsSpan().CopyTo(new Span<uint>(bits, pixels.Length));

        // The 1-bpp mask is ignored for 32-bit icons with alpha, but must exist and be zero (= "use colour").
        int maskStride = (size + 15) / 16 * 2;
        byte* maskBits = stackalloc byte[maskStride * size];
        new Span<byte>(maskBits, maskStride * size).Clear();
        nint mask = Native.CreateBitmap(size, size, 1, 1, maskBits);

        var info = new Native.ICONINFO { fIcon = 1, hbmMask = mask, hbmColor = color };
        nint icon = Native.CreateIconIndirect(&info);
        Native.DeleteObject(color);
        Native.DeleteObject(mask);
        return icon;
    }
}
