using System.Collections.Concurrent;
using NuzlockeBot.Data;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using EllipseShape = SixLabors.ImageSharp.Drawing.EllipsePolygon;
using RectShape = SixLabors.ImageSharp.Drawing.RectangularPolygon;

namespace NuzlockeBot.Services;

/// <summary>Farbrollen für Tabellenspalten.</summary>
public enum Tone { Green, Yellow, Red, Blue, Gold, Grey }

/// <summary>Ein Pokémon auf einem Banner. Leere Art = noch unbekannt ("?"), leere Beschriftung = kein Namensschild.</summary>
public record BannerMon(string Species, string Caption);

/// <summary>Stimmung der Szene (Himmel, Boden, Hintergrundmuster).</summary>
public enum SceneMood { Day, Dawn, Dusk, Night, Gold, Grey }

/// <summary>Inhalt eines Kampfszenen-Banners.</summary>
/// <param name="BarValue">Füllstand der "Band"-Leiste. Bei <paramref name="BarMax"/> = 0 wird keine Leiste gezeigt.</param>
public record BattleBanner(
    SceneMood Mood, string Title, string Subtitle, string Line1, string Line2,
    IReadOnlyList<BannerMon> Mons, int BarValue = 0, int BarMax = 0, bool Grayscale = false, bool Medal = false,
    string MedalLabel = "");

/// <summary>
/// Zeichnet alle Bilder im Pixel-Stil eines Kampfbildschirms: Banner, Status-Board, Tabellen und Rückblick.
/// Gezeichnet wird in niedriger Auflösung (400 Pixel breit) ohne Kantenglättung und dann 3× pixelgenau vergrößert.
/// Schlägt etwas fehl (z. B. keine Internetverbindung für Sprites), wird null zurückgegeben
/// und die Nachricht geht ohne Bild raus.
/// </summary>
public sealed class ImageRenderer
{
    private const int LW = 400, Zoom = 3;

    private readonly FontFamily? _pixel;   // Press Start 2P – Titel, Namen, Zahlen
    private readonly FontFamily? _small;   // Silkscreen – kleine, schmale Beschriftungen
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ConcurrentDictionary<int, Image<Rgba32>?> _sprites = new();
    private readonly ConcurrentDictionary<int, Image<Rgba32>?> _icons = new();

    public ImageRenderer()
    {
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            var fonts = new FontCollection();
            _pixel = fonts.Add(System.IO.Path.Combine(dir, "PressStart2P-Regular.ttf"));
            _small = fonts.Add(System.IO.Path.Combine(dir, "Silkscreen-Regular.ttf"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bilder] Schriftarten nicht gefunden, Bilder deaktiviert: {ex.Message}");
        }
    }

    private (Font F8, Font F16, Font Small) Fonts() =>
        (_pixel!.Value.CreateFont(8), _pixel!.Value.CreateFont(16), _small!.Value.CreateFont(8));

    // ---------------------------------------------------------------- Farben

    private static readonly Color PixGreen = Rgb(64, 200, 96), PixYellow = Rgb(240, 176, 48), PixRed = Rgb(216, 64, 64),
        PixBlue = Rgb(72, 128, 224), PixGold = Rgb(200, 150, 24), PixMuted = Rgb(128, 128, 136),
        PixOrange = Rgb(216, 128, 32), PixDeadBox = Rgb(226, 224, 228), PixStripe = Rgb(236, 236, 228);

    public static Color ToColor(Tone tone) => tone switch
    {
        Tone.Green => PixGreen,
        Tone.Yellow => PixYellow,
        Tone.Red => PixRed,
        Tone.Blue => PixBlue,
        Tone.Gold => PixGold,
        _ => PixMuted
    };

    private static Color StatusColor(LinkStatus status) => status switch
    {
        LinkStatus.Lebendig => PixGreen,
        LinkStatus.Gebrochen => PixYellow,
        _ => PixRed
    };

    /// <summary>Karomuster im Hintergrund, passend zur Stimmung.</summary>
    private static (Color A, Color B) CheckerFor(SceneMood mood) => mood switch
    {
        SceneMood.Dawn => (Rgb(236, 220, 228), Rgb(228, 210, 220)),
        SceneMood.Dusk => (Rgb(250, 214, 170), Rgb(244, 202, 154)),
        SceneMood.Night => (Rgb(52, 44, 80), Rgb(44, 38, 70)),
        SceneMood.Gold => (Rgb(252, 232, 160), Rgb(246, 222, 140)),
        SceneMood.Grey => (Rgb(196, 200, 206), Rgb(186, 190, 198)),
        _ => (Rgb(200, 224, 240), Rgb(188, 214, 236))
    };

    // ---------------------------------------------------------------- Kampfszenen-Banner

    /// <summary>Stimmung der Kampfszene (Himmel, Boden, Plattformen).</summary>
    private sealed record ScenePalette(Color[] Sky, Color Ground, Color Ground2, Color Plat, Color Rim, Color Cloud,
        bool Stars = false, bool Sparkles = false);

    private static ScenePalette PaletteFor(SceneMood mood) => mood switch
    {
        SceneMood.Dawn => new([Rgb(176, 196, 240), Rgb(208, 200, 232), Rgb(240, 204, 212), Rgb(252, 220, 196)],
            Rgb(128, 184, 104), Rgb(104, 160, 88), Rgb(184, 220, 144), Rgb(96, 148, 72), Rgb(255, 240, 236)),
        SceneMood.Dusk => new([Rgb(232, 128, 80), Rgb(240, 152, 96), Rgb(246, 180, 120), Rgb(250, 206, 152)],
            Rgb(144, 152, 88), Rgb(120, 128, 72), Rgb(196, 196, 128), Rgb(112, 116, 60), Rgb(255, 226, 196)),
        SceneMood.Night => new([Rgb(40, 32, 72), Rgb(64, 40, 88), Rgb(96, 48, 96), Rgb(136, 64, 96)],
            Rgb(48, 64, 72), Rgb(36, 48, 56), Rgb(80, 96, 104), Rgb(40, 52, 60), Rgb(92, 80, 120), Stars: true),
        SceneMood.Gold => new([Rgb(248, 196, 72), Rgb(250, 210, 104), Rgb(252, 224, 140), Rgb(254, 236, 176)],
            Rgb(136, 176, 88), Rgb(112, 152, 72), Rgb(200, 224, 136), Rgb(104, 144, 64), Rgb(255, 248, 224), Sparkles: true),
        SceneMood.Grey => new([Rgb(120, 128, 140), Rgb(140, 148, 158), Rgb(160, 166, 174), Rgb(180, 186, 192)],
            Rgb(104, 116, 104), Rgb(88, 100, 88), Rgb(150, 160, 150), Rgb(88, 98, 88), Rgb(200, 204, 210)),
        _ => new([Rgb(120, 200, 240), Rgb(144, 212, 244), Rgb(168, 224, 248), Rgb(192, 234, 250)],
            Rgb(120, 192, 96), Rgb(96, 168, 80), Rgb(176, 224, 128), Rgb(88, 152, 64), Rgb(248, 252, 255))
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static readonly Color PixInk = Rgb(56, 56, 64), PixInkShadow = Rgb(208, 208, 200), PixFrame = Rgb(40, 48, 72),
        PixBox = Rgb(248, 248, 240), PixBoxInner = Rgb(200, 208, 224), PixTag = Rgb(248, 248, 240);

    /// <summary>Ohne Kantenglättung zeichnen, damit alles pixelgenau bleibt.</summary>
    private static readonly DrawingOptions Crisp = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

    /// <summary>
    /// Banner im Stil eines Kampfbildschirms: Infobox mit Ort, Untertitel und "Band"-Leiste (wie eine KP-Leiste),
    /// die Pokémon auf Plattformen und eine Textbox mit zwei Zeilen.
    /// Gezeichnet wird in 400×136 und dann pixelgenau auf 1200×408 vergrößert.
    /// </summary>
    public Task<MemoryStream?> BattleAsync(BattleBanner banner) =>
        SafeRenderAsync(async () =>
        {
            const int LH = 136;
            var p = PaletteFor(banner.Mood);
            var (f8, f16, _) = Fonts();

            var mons = banner.Mons.Take(6).ToList();
            var sprites = await Task.WhenAll(mons.Select(m => string.IsNullOrEmpty(m.Species)
                ? Task.FromResult<Image<Rgba32>?>(null) : LoadSpriteAsync(m.Species)));

            using var img = new Image<Rgba32>(LW, LH);
            img.Mutate(ctx =>
            {
                // Himmel in Stufen, Sterne/Funkeln, Wolken
                for (var i = 0; i < p.Sky.Length; i++) PixRect(ctx, 0, i * 20, LW, (i + 1) * 20, p.Sky[i]);
                if (p.Stars)
                {
                    var rnd = new Random(7);
                    for (var i = 0; i < 40; i++) PixRect(ctx, rnd.Next(LW), rnd.Next(60), 0, 0, Rgb(236, 232, 255), size: true);
                }
                foreach (var (cx, cy, w) in new[] { (40, 18, 34), (150, 30, 26), (300, 12, 40) })
                {
                    PixRect(ctx, cx, cy, cx + w, cy + 5, p.Cloud);
                    PixRect(ctx, cx + 4, cy - 3, cx + w - 6, cy, p.Cloud);
                    PixRect(ctx, cx + 10, cy - 6, cx + w - 14, cy - 3, p.Cloud);
                }
                if (p.Sparkles)
                    foreach (var (x, y) in new[] { (196, 14), (236, 40), (330, 26), (384, 52), (210, 64) })
                    {
                        PixRect(ctx, x - 3, y, x + 3, y, Color.White);
                        PixRect(ctx, x, y - 3, x, y + 3, Color.White);
                    }

                // Boden mit Grasmuster
                PixRect(ctx, 0, 80, LW, LH, p.Ground);
                for (var y = 84; y < LH; y += 6)
                    for (var x = (y / 6) % 2 * 6; x < LW; x += 12) PixRect(ctx, x, y, x + 2, y + 1, p.Ground2);

                // Pokémon auf Plattformen: bis zu 3 vorne, weitere dahinter (z. B. das Team beim Sieg)
                var showTags = mons.Count <= 3;
                if (mons.Count > 3) DrawGroup(ctx, f8, f16, p, mons, sprites, 3, mons.Count - 3, 62, 306, false, banner.Grayscale);
                DrawGroup(ctx, f8, f16, p, mons, sprites, 0, Math.Min(3, mons.Count), 84, 284, showTags, banner.Grayscale);

                if (banner.Medal) DrawMedal(ctx, f8, 290, banner.MedalLabel);

                // Infobox oben links
                var withBar = banner.BarMax > 0;
                PixBoxShape(ctx, 6, 6, 178, withBar ? 60 : 48);
                var title = Sanitize(banner.Title);
                if (Width(title, f16) <= 160) PixText(ctx, 13, 15, title, f16, PixInk);
                else PixText(ctx, 13, 18, Fit(title, f8, 160), f8, PixInk);
                PixText(ctx, 13, 34, Fit(Sanitize(banner.Subtitle), f8, 160), f8, PixInk);
                if (withBar)
                {
                    PixText(ctx, 13, 46, "BAND", f8, Rgb(232, 160, 48), shadow: false);
                    PixRect(ctx, 50, 46, 170, 53, PixFrame);
                    PixRect(ctx, 52, 48, 168, 51, Rgb(88, 88, 96));
                    var value = Math.Clamp(banner.BarValue, 0, banner.BarMax);
                    if (value > 0)
                    {
                        var color = value >= banner.BarMax ? Rgb(64, 200, 96)
                            : value * 2 >= banner.BarMax ? Rgb(240, 176, 48) : Rgb(216, 64, 64);
                        PixRect(ctx, 52, 48, 52 + 116 * value / banner.BarMax, 51, color);
                    }
                }

                // Textbox unten
                PixBoxShape(ctx, 4, 100, LW - 5, LH - 4);
                PixText(ctx, 14, 109, Fit(Sanitize(banner.Line1), f8, 372), f8, PixInk);
                PixText(ctx, 14, 121, Fit(Sanitize(banner.Line2), f8, 360), f8, PixInk);
                ctx.Fill(Crisp, Rgb(216, 64, 64), new SixLabors.ImageSharp.Drawing.Polygon(
                    new SixLabors.ImageSharp.Drawing.LinearLineSegment(new PointF(LW - 20, 122), new PointF(LW - 12, 122), new PointF(LW - 16, 127))));

                ctx.Resize(LW * Zoom, LH * Zoom, KnownResamplers.NearestNeighbor);
            });

            return await ToPngAsync(img);
        });

    private void DrawGroup(IImageProcessingContext ctx, Font f8, Font f16, ScenePalette p, List<BannerMon> mons,
        Image<Rgba32>?[] sprites, int from, int count, int platformTop, int centerX, bool tags, bool grayscale)
    {
        var start = centerX - (count - 1) * 33;
        for (var i = 0; i < count; i++)
        {
            var cx = start + 66 * i;
            PixOval(ctx, cx - 30, platformTop, cx + 30, platformTop + 14, p.Rim);
            PixOval(ctx, cx - 28, platformTop, cx + 28, platformTop + 11, p.Plat);
            var bottom = platformTop + 8;
            int head;

            if (sprites[from + i] is { } sprite)
            {
                using var s = grayscale ? sprite.Clone(c => c.Grayscale().Opacity(0.8f)) : sprite.Clone();
                var y = Math.Max(2, bottom - s.Height);
                ctx.DrawImage(s, new Point(cx - s.Width / 2, y), 1f);
                head = y;
            }
            else
            {
                PixText(ctx, cx - 8, bottom - 24, "?", f16, PixFrame);   // noch unbekannt
                head = bottom - 24;
            }

            var caption = Sanitize(mons[from + i].Caption).ToUpperInvariant();
            if (tags && caption.Length > 0)
            {
                caption = Fit(caption, f8, 62);
                var w = (int)Width(caption, f8); var ty = Math.Max(4, head - 12);
                PixRect(ctx, cx - w / 2 - 3, ty - 2, cx + w / 2 + 2, ty + 9, PixFrame);
                PixText(ctx, cx - w / 2, ty, caption, f8, PixTag, shadow: false);
            }
        }
    }

    private void DrawMedal(IImageProcessingContext ctx, Font f8, int cx, string label)
    {
        PixPolygon(ctx, Rgb(64, 96, 200), (cx - 14, 30), (cx - 4, 30), (cx + 2, 52), (cx - 8, 52));
        PixPolygon(ctx, Rgb(216, 64, 64), (cx + 14, 30), (cx + 4, 30), (cx - 2, 52), (cx + 8, 52));
        PixOval(ctx, cx - 16, 48, cx + 16, 80, Rgb(176, 128, 24));
        PixOval(ctx, cx - 13, 51, cx + 13, 77, Rgb(248, 200, 64));
        if (label.Length > 0)
            PixText(ctx, cx - (int)Width(label, f8) / 2, 60, label, f8, Rgb(176, 128, 24), shadow: false);
    }

    // ---------------------------------------------------------------- Pixel-Bausteine

    /// <summary>Rechteck mit inklusiven Koordinaten (wie im Pixel-Editor).</summary>
    private static void PixRect(IImageProcessingContext ctx, int x0, int y0, int x1, int y1, Color c, bool size = false)
    {
        if (size) { x1 = x0; y1 = y0; }
        ctx.Fill(Crisp, c, new RectShape(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
    }

    private static void PixOval(IImageProcessingContext ctx, int x0, int y0, int x1, int y1, Color c) =>
        ctx.Fill(Crisp, c, new EllipseShape((x0 + x1) / 2f + 0.5f, (y0 + y1) / 2f + 0.5f, x1 - x0 + 1, y1 - y0 + 1));

    private static void PixPolygon(IImageProcessingContext ctx, Color c, params (int X, int Y)[] pts) =>
        ctx.Fill(Crisp, c, new SixLabors.ImageSharp.Drawing.Polygon(
            new SixLabors.ImageSharp.Drawing.LinearLineSegment(pts.Select(q => new PointF(q.X, q.Y)).ToArray())));

    /// <summary>Fenster wie im Spiel: dunkler Rahmen, heller Innenrand, helle Fläche.</summary>
    private static void PixBoxShape(IImageProcessingContext ctx, int x0, int y0, int x1, int y1)
    {
        PixRect(ctx, x0, y0, x1, y1, PixFrame);
        PixRect(ctx, x0 + 2, y0 + 2, x1 - 2, y1 - 2, PixBoxInner);
        PixRect(ctx, x0 + 3, y0 + 3, x1 - 3, y1 - 3, PixBox);
    }

    /// <summary>
    /// Pixeltext mit Schatten. Die Pixelschrift hat keine echten Groß-Umlaute (Ä, Ö, Ü sehen aus wie ä, ö, ü),
    /// deshalb wird der Grundbuchstabe gezeichnet und die Punkte bzw. der Akzent selbst gesetzt.
    /// </summary>
    private static void PixText(IImageProcessingContext ctx, int x, int y, string text, Font f, Color color, bool shadow = true)
    {
        var u = Math.Max(1, (int)f.Size / 8);
        var layers = shadow ? new[] { (1, PixInkShadow), (0, color) } : new[] { (0, color) };
        foreach (var (off, c) in layers)
        {
            float cx = x + off;
            foreach (var ch in text)
            {
                var b = ch switch { 'Ä' => 'A', 'Ö' => 'O', 'Ü' => 'U', 'É' => 'E', _ => ch };
                var s = b.ToString();
                ctx.DrawText(Crisp, s, f, c, new PointF(cx, y + off));
                var px = (int)cx;
                if (ch is 'Ä' or 'Ö' or 'Ü')
                {
                    PixRect(ctx, px + 2 * u, y + off - 3 * u, px + 4 * u - 1, y + off - u - 1, c);
                    PixRect(ctx, px + 5 * u, y + off - 3 * u, px + 7 * u - 1, y + off - u - 1, c);
                }
                else if (ch == 'É')
                    PixRect(ctx, px + 3 * u, y + off - 3 * u, px + 5 * u - 1, y + off - u - 1, c);
                cx += Width(s, f);
            }
        }
    }

    /// <summary>Zeichen entfernen, die die Pixelschrift nicht kennt (Emojis, Symbole).</summary>
    private static string Sanitize(string? text) =>
        new string((text ?? "").Where(c => !char.IsSurrogate(c) && c != '\uFE0F' && (c < '\u2000' || c == '…' || c == '–')).ToArray()).Trim();

    // ---------------------------------------------------------------- Seiten: Board, Tabelle, Rückblick

    /// <summary>Seite mit Karomuster und kleiner Szene als Kopfzeile (0–45 Pixel).</summary>
    private static Image<Rgba32> CreatePage(SceneMood mood, int height)
    {
        var img = new Image<Rgba32>(LW, height);
        var p = PaletteFor(mood);
        var (a, b) = CheckerFor(mood);
        img.Mutate(ctx =>
        {
            for (var y = 0; y < height; y += 8)
                for (var x = 0; x < LW; x += 8)
                    PixRect(ctx, x, y, x + 7, y + 7, (x / 8 + y / 8) % 2 == 0 ? a : b);

            for (var i = 0; i < p.Sky.Length; i++) PixRect(ctx, 0, i * 8, LW, (i + 1) * 8, p.Sky[i]);
            if (p.Stars)
            {
                var rnd = new Random(7);
                for (var i = 0; i < 24; i++) PixRect(ctx, rnd.Next(LW), rnd.Next(30), 0, 0, Rgb(236, 232, 255), size: true);
            }
            foreach (var (cx, cy, w) in new[] { (220, 10, 30), (330, 6, 40) })
            {
                PixRect(ctx, cx, cy, cx + w, cy + 4, p.Cloud);
                PixRect(ctx, cx + 5, cy - 3, cx + w - 6, cy, p.Cloud);
            }
            PixRect(ctx, 0, 32, LW, 43, p.Ground);
            for (var x = 0; x < LW; x += 12) PixRect(ctx, x, 36, x + 2, 37, p.Ground2);
            PixRect(ctx, 0, 44, LW, 45, PixFrame);
        });
        return img;
    }

    /// <summary>Titelfenster oben links. Gibt die Breite zurück.</summary>
    private static int TitleBox(IImageProcessingContext ctx, string title, string subtitle, Font f8, Font f16, int minWidth = 130)
    {
        var big = Width(title, f16) <= 244;
        var tf = big ? f16 : f8;
        var w = (int)Math.Clamp(Math.Max(Width(title, tf), Width(subtitle, f8)) + 20, minWidth, 386);
        PixBoxShape(ctx, 6, 4, 6 + w, 40);
        PixText(ctx, 13, big ? 10 : 13, big ? title : Fit(title, f8, w - 14), tf, PixInk);
        if (subtitle.Length > 0) PixText(ctx, 13, 29, Fit(subtitle, f8, w - 14), f8, PixInk);
        return w;
    }

    /// <summary>Dunkles Namensschild mit heller Schrift.</summary>
    private static void Tag(IImageProcessingContext ctx, int x, int y, string text, Font f8)
    {
        var w = (int)Width(text, f8);
        PixRect(ctx, x - 2, y - 2, x + w + 1, y + 8, PixFrame);
        PixText(ctx, x, y, text, f8, PixTag, shadow: false);
    }

    /// <summary>Kleine Schrift (Silkscreen) – hat echte Umlaute, braucht keinen Trick.</summary>
    private static void SText(IImageProcessingContext ctx, int x, int y, string text, Color color, Font small) =>
        ctx.DrawText(Crisp, text, small, color, new PointF(x, y));

    private static void Finish(IImageProcessingContext ctx, int height) =>
        ctx.Resize(LW * Zoom, height * Zoom, KnownResamplers.NearestNeighbor);

    /// <summary>
    /// Status-Board: eine Zeile pro Route mit Status-Farbe, Routenname und pro Spieler Icon und Name.
    /// Im Titelfenster steht eine Legende (lebendig / gebrochen / tot).
    /// </summary>
    public Task<MemoryStream?> BoardAsync(SceneMood mood, string title, Run run, IReadOnlyList<RouteLink> links) =>
        SafeRenderAsync(async () =>
        {
            const int RowH = 30, Top = 64, X0 = 128;
            var players = run.Participants.OrderBy(p => p.PlayerId).ToList();
            var colW = (392f - X0) / Math.Max(1, players.Count);
            var height = Top + RowH * Math.Max(1, links.Count) + 6;

            var species = links.SelectMany(l => l.Encounters).Where(e => e.Status == EncounterStatus.Gefangen)
                .Select(e => e.Species).Distinct().ToList();
            var icons = (await Task.WhenAll(species.Select(async s => (s, Img: await LoadIconAsync(s)))))
                .ToDictionary(x => x.s, x => x.Img);
            var (f8, f16, fs) = Fonts();

            using var img = CreatePage(mood, height);
            img.Mutate(ctx =>
            {
                TitleBox(ctx, Sanitize(title), "", f8, f16);
                var lx = 13;
                foreach (var (c, v) in new[]
                         {
                             (PixGreen, run.Links.Count(l => l.Status == LinkStatus.Lebendig)),
                             (PixYellow, run.Links.Count(l => l.Status == LinkStatus.Gebrochen)),
                             (PixRed, run.Links.Count(l => l.Status == LinkStatus.Tot))
                         })
                {
                    PixRect(ctx, lx, 29, lx + 6, 35, PixFrame);
                    PixRect(ctx, lx + 1, 30, lx + 5, 34, c);
                    var s = v.ToString();
                    PixText(ctx, lx + 10, 29, s, f8, PixInk);
                    lx += 10 + (int)Width(s, f8) + 12;
                }

                Tag(ctx, 8, 51, "ROUTE", f8);
                for (var i = 0; i < players.Count; i++)
                    Tag(ctx, (int)(X0 + colW * i + 2), 51, Fit(Sanitize(players[i].Player.Name).ToUpperInvariant(), f8, colW - 8), f8);

                if (links.Count == 0) SText(ctx, 14, Top + 10, "NOCH KEINE EINTRÄGE", PixMuted, fs);

                var y = Top;
                foreach (var link in links)
                {
                    var dead = link.Status == LinkStatus.Tot;
                    PixRect(ctx, 4, y, 395, y + RowH - 3, PixFrame);
                    PixRect(ctx, 6, y + 2, 393, y + RowH - 5, PixBoxInner);
                    PixRect(ctx, 7, y + 3, 392, y + RowH - 6, dead ? PixDeadBox : PixBox);
                    PixRect(ctx, 10, y + 6, 13, y + RowH - 9, StatusColor(link.Status));
                    PixText(ctx, 18, y + 10, Fit(Sanitize(link.Route), f8, 104), f8, dead ? PixMuted : PixInk);

                    for (var i = 0; i < players.Count; i++)
                    {
                        var cx = (int)(X0 + colW * i);
                        var e = link.Encounters.FirstOrDefault(x => x.PlayerId == players[i].PlayerId);
                        if (e is null) { SText(ctx, cx + 4, y + 10, "-", PixMuted, fs); continue; }
                        if (e.Status == EncounterStatus.Verpasst) { SText(ctx, cx + 4, y + 10, "VERPASST", PixOrange, fs); continue; }

                        if (icons.GetValueOrDefault(e.Species) is { } icon)
                        {
                            using var ic = dead ? icon.Clone(c => c.Grayscale().Opacity(0.75f)) : icon.Clone();
                            ctx.DrawImage(ic, new Point(cx + 2 + (30 - ic.Width) / 2, y + (RowH - 3 - ic.Height) / 2), 1f);
                        }

                        var tx = cx + 34; var mw = colW - 38;
                        if (!string.IsNullOrWhiteSpace(e.Nickname))
                        {
                            SText(ctx, tx, y + 6, Fit(Sanitize(e.Nickname).ToUpperInvariant(), fs, mw), dead ? PixMuted : PixInk, fs);
                            SText(ctx, tx, y + 15, Fit(e.Species.ToUpperInvariant(), fs, mw), PixMuted, fs);
                        }
                        else
                            SText(ctx, tx, y + 10, Fit(e.Species.ToUpperInvariant(), fs, mw), dead ? PixMuted : PixInk, fs);
                    }
                    y += RowH;
                }

                Finish(ctx, height);
            });

            return await ToPngAsync(img);
        });

    /// <summary>
    /// Tabelle: Namen links, Zahlen rechtsbündig in der Farbe ihrer Spalte, optional eine goldene Summenzeile.
    /// </summary>
    public Task<MemoryStream?> TableAsync(SceneMood mood, string title, string subtitle, string[] headers, Tone[] columnTones,
        IReadOnlyList<string[]> rows, string[]? total) =>
        SafeRenderAsync(async () =>
        {
            const int RowH = 22, Top = 62, First = 150;
            var height = Top + RowH * Math.Max(1, rows.Count) + 14 + (total is null ? 0 : RowH + 10) + 6;
            var step = (386f - First) / Math.Max(1, headers.Length - 1);
            var (f8, f16, fs) = Fonts();
            float Right(int col) => First + step * (col + 1);

            using var img = CreatePage(mood, height);
            img.Mutate(ctx =>
            {
                TitleBox(ctx, Sanitize(title), Sanitize(subtitle), f8, f16);
                SText(ctx, 14, 51, headers[0], mood == SceneMood.Night ? Rgb(236, 236, 240) : PixFrame, fs);
                for (var i = 1; i < headers.Length; i++)
                    SText(ctx, (int)(Right(i - 1) - Width(headers[i], fs)), 51, headers[i], ToColor(columnTones[i - 1]), fs);

                PixBoxShape(ctx, 4, Top - 2, 395, Top + RowH * rows.Count + 8);
                var y = Top + 3;
                for (var r = 0; r < rows.Count; r++)
                {
                    if (r % 2 == 1) PixRect(ctx, 7, y, 392, y + RowH - 1, PixStripe);
                    DrawRow(ctx, rows[r], y, f8, f16, First, Right, columnTones, bigName: true);
                    y += RowH;
                }

                if (total is not null)
                {
                    var ty = Top + RowH * rows.Count + 12;
                    PixRect(ctx, 4, ty, 395, ty + RowH + 4, PixFrame);
                    PixRect(ctx, 6, ty + 2, 393, ty + RowH + 2, Rgb(252, 226, 120));
                    PixRect(ctx, 7, ty + 3, 392, ty + RowH + 1, Rgb(254, 240, 176));
                    DrawRow(ctx, total, ty + 3, f8, f16, First, Right, columnTones, bigName: false);
                }

                Finish(ctx, height);
            });

            return await ToPngAsync(img);
        });

    private static void DrawRow(IImageProcessingContext ctx, string[] row, int y, Font f8, Font f16, int first,
        Func<int, float> right, Tone[] tones, bool bigName)
    {
        var name = Sanitize(row[0]);
        if (bigName && Width(name, f16) <= first - 20) PixText(ctx, 14, y + 3, name, f16, PixInk);
        else PixText(ctx, 14, y + 7, Fit(bigName ? name : name.ToUpperInvariant(), f8, first - 20), f8, PixInk);

        for (var i = 1; i < row.Length; i++)
        {
            var v = row[i];
            if (v.Length == 0) continue;
            PixText(ctx, (int)(right(i - 1) - Width(v, f16)), y + 3, v, f16, v == "0" ? PixMuted : ToColor(tones[i - 1]));
        }
    }

    /// <summary>
    /// Rückblick eines Runs: Titel mit Ergebnis und Dauer, vier Kennzahlen, Zeitstrahl mit allen Fängen,
    /// verpassten Encountern und Toden, Spieler-Statistik und der nummerierte Friedhof
    /// (Nummern passen zu den Markierungen im Zeitstrahl).
    /// </summary>
    public Task<MemoryStream?> RecapAsync(Run run, IReadOnlyList<PlayerStats> stats) =>
        SafeRenderAsync(async () =>
        {
            const int MaxDeaths = 8;
            var start = run.StartedAt;
            var end = run.EndedAt ?? DateTime.UtcNow;
            var totalHours = Math.Max((end - start).TotalHours, 0.01);

            var (mood, word) = run.Status switch
            {
                RunStatus.Geschafft => (SceneMood.Gold, "GESCHAFFT NACH"),
                RunStatus.Gescheitert => (SceneMood.Night, "GESCHEITERT NACH"),
                RunStatus.Abgebrochen => (SceneMood.Grey, "ABGEBROCHEN NACH"),
                _ => (SceneMood.Day, "LÄUFT SEIT")
            };

            var encounters = run.Links.SelectMany(l => l.Encounters).ToList();
            var deaths = run.Links.Where(l => l.Status == LinkStatus.Tot && l.DiedAt != null).OrderBy(l => l.DiedAt).ToList();
            var shown = deaths.Take(MaxDeaths).ToList();

            var deadSpecies = shown.SelectMany(l => l.Encounters).Where(e => e.Status == EncounterStatus.Gefangen)
                .Select(e => e.Species).Distinct().ToList();
            var icons = (await Task.WhenAll(deadSpecies.Select(async s => (s, Img: await LoadIconAsync(s)))))
                .ToDictionary(x => x.s, x => x.Img);
            var (f8, f16, fs) = Fonts();

            const int YTiles = 52, YLine = YTiles + 46, YPlayers = YLine + 58;
            var playersH = 20 + 18 * stats.Count;
            var yGrave = YPlayers + playersH + 8;
            var graveH = 18 + 26 * Math.Max(1, shown.Count) + (deaths.Count > MaxDeaths ? 12 : 0);
            var height = yGrave + graveH + 6;

            using var img = CreatePage(mood, height);
            img.Mutate(ctx =>
            {
                TitleBox(ctx, $"RUN {run.Number}", $"{word} {FormatDuration(end - start)}", f8, f16);

                // Kennzahlen
                (string Value, string Label, Color Color)[] tiles =
                [
                    (FormatDuration(end - start, compact: true), "DAUER", PixInk),
                    ($"{run.Links.Count}", "ROUTEN", PixInk),
                    ($"{encounters.Count(e => e.Status == EncounterStatus.Gefangen)}", "FÄNGE", PixBlue),
                    ($"{deaths.Count}", "TODE", PixRed)
                ];
                var tileW = (392f - 8 - 3 * 6) / 4;
                for (var i = 0; i < tiles.Length; i++)
                {
                    var x = (int)(4 + i * (tileW + 6));
                    PixBoxShape(ctx, x, YTiles, (int)(x + tileW), YTiles + 38);
                    var big = Width(tiles[i].Value, f16) <= tileW - 10;
                    PixText(ctx, x + 8, big ? YTiles + 7 : YTiles + 10, tiles[i].Value, big ? f16 : f8, tiles[i].Color);
                    SText(ctx, x + 8, YTiles + 27, tiles[i].Label, PixMuted, fs);
                }

                // Zeitstrahl
                PixBoxShape(ctx, 4, YLine - 4, 395, YLine + 48);
                SText(ctx, 12, YLine + 2, "ZEITSTRAHL", PixMuted, fs);
                var lx = 230;
                foreach (var (c, label) in new[] { (PixGreen, "FANG"), (PixYellow, "VERPASST"), (PixRed, "TOD") })
                {
                    PixRect(ctx, lx, YLine + 3, lx + 5, YLine + 8, c);
                    SText(ctx, lx + 8, YLine + 2, label, PixMuted, fs);
                    lx += 14 + (int)Width(label, fs);
                }

                const int Bx0 = 16, Bx1 = 383; var by = YLine + 26;
                int At(DateTime t) => (int)(Bx0 + (Bx1 - Bx0) * Math.Clamp((t - start).TotalHours / totalHours, 0, 1));
                PixRect(ctx, Bx0, by - 1, Bx1, by, Rgb(176, 176, 184));
                foreach (var e in encounters.OrderBy(e => e.CreatedAt))
                {
                    var x = At(e.CreatedAt);
                    PixRect(ctx, x - 1, by - 6, x, by + 5, e.Status == EncounterStatus.Gefangen ? PixGreen : PixYellow);
                }
                for (var i = 0; i < deaths.Count; i++)
                    NumberBadge(ctx, At(deaths[i].DiedAt!.Value), by, i + 1, f8);
                SText(ctx, Bx0, by + 9, "START", PixMuted, fs);
                var endLabel = run.Status == RunStatus.Aktiv ? "JETZT" : "ENDE";
                SText(ctx, (int)(Bx1 - Width(endLabel, fs)), by + 9, endLabel, PixMuted, fs);

                // Spieler
                PixBoxShape(ctx, 4, YPlayers, 395, YPlayers + playersH);
                SText(ctx, 12, YPlayers + 6, "SPIELER", PixMuted, fs);
                (string Head, Color Color)[] heads = [("FANG", PixBlue), ("VERP.", PixYellow), ("TODE", PixRed)];
                for (var i = 0; i < heads.Length; i++)
                    SText(ctx, (int)(250 + i * 64 - Width(heads[i].Head, fs)), YPlayers + 6, heads[i].Head, heads[i].Color, fs);
                var y = YPlayers + 18;
                foreach (var s in stats)
                {
                    PixText(ctx, 12, y + 2, Fit(Sanitize(s.Name).ToUpperInvariant(), f8, 150), f8, PixInk);
                    int[] values = [s.Caught, s.Missed, s.Deaths];
                    for (var i = 0; i < values.Length; i++)
                    {
                        var v = values[i].ToString();
                        PixText(ctx, (int)(250 + i * 64 - Width(v, f8)), y + 2, v, f8, values[i] == 0 ? PixMuted : heads[i].Color);
                    }
                    y += 18;
                }

                // Friedhof
                PixBoxShape(ctx, 4, yGrave, 395, yGrave + graveH);
                SText(ctx, 12, yGrave + 6, "FRIEDHOF", PixMuted, fs);
                y = yGrave + 16;
                if (shown.Count == 0) SText(ctx, 12, y + 8, "NIEMAND GESTORBEN. RESPEKT!", PixMuted, fs);
                for (var i = 0; i < shown.Count; i++)
                {
                    var link = shown[i];
                    var num = (i + 1).ToString();
                    var nw = (int)Width(num, f8);
                    PixRect(ctx, 10, y + 5, 10 + nw + 5, y + 16, PixRed);
                    PixText(ctx, 13, y + 7, num, f8, Color.White, shadow: false);

                    var caught = link.Encounters.Where(e => e.Status == EncounterStatus.Gefangen).OrderBy(e => e.PlayerId).Take(4).ToList();
                    var ix = 392 - caught.Count * 30;
                    PixText(ctx, 32, y + 3, Fit(Sanitize(link.Route), f8, ix - 40), f8, PixInk);
                    var info = $"{FormatElapsed(link.DiedAt!.Value - start)}  {Sanitize(link.DeathCause)}" +
                               (link.CausedByPlayer is null ? "" : $" ({Sanitize(link.CausedByPlayer.Name)})");
                    SText(ctx, 32, y + 13, Fit(info.ToUpperInvariant(), fs, ix - 40), PixMuted, fs);

                    foreach (var e in caught)
                    {
                        if (icons.GetValueOrDefault(e.Species) is { } icon)
                        {
                            using var ic = icon.Clone(c => c.Grayscale().Opacity(0.75f));
                            ctx.DrawImage(ic, new Point(ix + (30 - ic.Width) / 2, y + (24 - ic.Height) / 2), 1f);
                        }
                        ix += 30;
                    }
                    y += 26;
                }
                if (deaths.Count > MaxDeaths) SText(ctx, 12, y, $"+ {deaths.Count - MaxDeaths} WEITERE", PixMuted, fs);

                Finish(ctx, height);
            });

            return await ToPngAsync(img);
        });

    /// <summary>
    /// Levelcap-Liste: alle Pflichtkämpfe mit Ort, Typ und höchstem Level.
    /// Geschaffte Kämpfe sind grau mit Haken, der nächste ist golden hervorgehoben.
    /// </summary>
    public Task<MemoryStream?> LevelCapAsync(IReadOnlyList<BossFight> fights, int progress) =>
        SafeRenderAsync(async () =>
        {
            const int RowH = 30, Top = 52;
            var height = Top + RowH * fights.Count + 6;
            var (f8, f16, fs) = Fonts();
            var next = progress >= 0 && progress < fights.Count ? fights[progress] : null;

            using var img = CreatePage(SceneMood.Day, height);
            img.Mutate(ctx =>
            {
                TitleBox(ctx, next is null ? "LEVELCAP -" : $"LEVELCAP {next.Level}",
                    next is null ? "STORY GESCHAFFT!" : $"NÄCHSTER: {next.Short}", f8, f16);

                var y = Top;
                for (var i = 0; i < fights.Count; i++)
                {
                    var f = fights[i];
                    var done = i < progress; var current = i == progress;
                    PixRect(ctx, 4, y, 395, y + RowH - 3, PixFrame);
                    PixRect(ctx, 6, y + 2, 393, y + RowH - 5, current ? Rgb(252, 226, 120) : PixBoxInner);
                    PixRect(ctx, 7, y + 3, 392, y + RowH - 6, current ? Rgb(254, 240, 176) : done ? PixDeadBox : PixBox);

                    if (done) Checkmark(ctx, 11, y + 9, PixGreen);
                    else if (current) PixPolygon(ctx, PixRed, (13, y + 8), (13, y + 18), (19, y + 13));
                    else SText(ctx, 12, y + 10, f.Nr.ToString(), PixMuted, fs);

                    PixText(ctx, 30, y + 6, Fit(f.Short, f8, 220), f8, done ? PixMuted : PixInk, shadow: !done);
                    SText(ctx, 30, y + 16, Fit($"{f.Place}, {f.Type}".ToUpperInvariant(), fs, 250), PixMuted, fs);

                    var lv = f.Level.ToString();
                    var x = (int)(386 - Width(lv, f16));
                    PixText(ctx, x, y + 6, lv, f16, done ? PixMuted : current ? PixRed : PixInk, shadow: !done);
                    SText(ctx, (int)(x - Width("LV", fs) - 4), y + 13, "LV", PixMuted, fs);
                    y += RowH;
                }

                Finish(ctx, height);
            });

            return await ToPngAsync(img);
        });

    /// <summary>Pixel-Haken für geschaffte Kämpfe.</summary>
    private static void Checkmark(IImageProcessingContext ctx, int x, int y, Color c)
    {
        (int Dx, int Dy)[] pts = [(0, 4), (1, 5), (2, 6), (3, 7), (4, 6), (5, 5), (6, 4), (7, 3), (8, 2), (9, 1), (10, 0)];
        foreach (var (dx, dy) in pts) PixRect(ctx, x + dx, y + dy, x + dx, y + dy + 2, c);
    }

    /// <summary>Rotes Kästchen mit Nummer (Zeitstrahl).</summary>
    private static void NumberBadge(IImageProcessingContext ctx, int cx, int cy, int number, Font f8)
    {
        var s = number.ToString(); var w = (int)Width(s, f8);
        PixRect(ctx, cx - w / 2 - 3, cy - 6, cx + w / 2 + 2, cy + 5, PixRed);
        PixText(ctx, cx - w / 2, cy - 4, s, f8, Color.White, shadow: false);
    }

    /// <summary>"3 TAGEN", "5 STUNDEN", "40 MINUTEN" bzw. kompakt "3T 5H".</summary>
    private static string FormatDuration(TimeSpan span, bool compact = false)
    {
        if (compact)
            return span.TotalDays >= 1 ? $"{(int)span.TotalDays}T {span.Hours}H"
                : span.TotalHours >= 1 ? $"{(int)span.TotalHours}H {span.Minutes}M"
                : $"{Math.Max(1, span.Minutes)}M";

        return span.TotalDays >= 1 ? $"{(int)span.TotalDays} {((int)span.TotalDays == 1 ? "TAG" : "TAGEN")}"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} {((int)span.TotalHours == 1 ? "STUNDE" : "STUNDEN")}"
            : $"{Math.Max(1, span.Minutes)} MINUTEN";
    }

    /// <summary>Zeit seit Run-Start, z. B. "+1T 4H" oder "+3H 12M".</summary>
    private static string FormatElapsed(TimeSpan span) =>
        span.TotalDays >= 1 ? $"+{(int)span.TotalDays}T {span.Hours}H"
        : span.TotalHours >= 1 ? $"+{(int)span.TotalHours}H {span.Minutes}M"
        : $"+{Math.Max(0, span.Minutes)}M";

    // ---------------------------------------------------------------- Allgemeine Bausteine

    private async Task<MemoryStream?> SafeRenderAsync(Func<Task<MemoryStream>> render)
    {
        if (_pixel is null || _small is null) return null;
        try
        {
            return await render();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bilder] Rendern fehlgeschlagen: {ex.Message}");
            return null;
        }
    }

    private static async Task<MemoryStream> ToPngAsync(Image img)
    {
        var ms = new MemoryStream();
        await img.SaveAsPngAsync(ms);
        ms.Position = 0;
        return ms;
    }

    private static float Width(string text, Font font) =>
        TextMeasurer.MeasureAdvance(text, new TextOptions(font)).Width;

    /// <summary>Kürzt Text mit "…", bis er in die Breite passt.</summary>
    private static string Fit(string text, Font font, float maxWidth)
    {
        if (Width(text, font) <= maxWidth) return text;
        while (text.Length > 1 && Width(text + "…", font) > maxWidth)
            text = text[..^1];
        return text.TrimEnd() + "…";
    }

    /// <summary>Kleines Menü-Icon (wie in der Box) laden, gecacht und zugeschnitten.</summary>
    private Task<Image<Rgba32>?> LoadIconAsync(string species) =>
        LoadImageAsync(species, _icons, nr =>
            $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-vii/icons/{nr}.png");

    /// <summary>Sprite aus Schwarz/Weiß laden, gecacht und zugeschnitten.</summary>
    private Task<Image<Rgba32>?> LoadSpriteAsync(string species) =>
        LoadImageAsync(species, _sprites, nr =>
            $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-v/black-white/{nr}.png");

    private async Task<Image<Rgba32>?> LoadImageAsync(string species, ConcurrentDictionary<int, Image<Rgba32>?> cache, Func<int, string> url)
    {
        if (Pokedex.Number(species) is not { } nr) return null;
        if (cache.TryGetValue(nr, out var cached)) return cached;

        Image<Rgba32>? image = null;
        try
        {
            await using var stream = await _http.GetStreamAsync(url(nr));
            image = await Image.LoadAsync<Rgba32>(stream);
            CropToContent(image);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bilder] Bild für #{nr} nicht ladbar: {ex.Message}");
            image?.Dispose();
            return null; // nicht cachen – beim nächsten Mal neu versuchen
        }

        return cache.GetOrAdd(nr, image);
    }

    /// <summary>Transparenten Rand entfernen, damit die Bilder gleichmäßig groß wirken.</summary>
    private static void CropToContent(Image<Rgba32> img)
    {
        int minX = img.Width, minY = img.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < img.Height; y++)
        for (var x = 0; x < img.Width; x++)
        {
            if (img[x, y].A == 0) continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }

        if (maxX >= minX && maxY >= minY)
            img.Mutate(ctx => ctx.Crop(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)));
    }
}
