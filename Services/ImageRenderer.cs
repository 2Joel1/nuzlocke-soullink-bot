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

/// <summary>Akzentfarben der Bilder.</summary>
public enum Tone { Green, Yellow, Red, Blue, Gold, Grey }

/// <summary>Ein Pokémon, das auf einem Banner gezeigt wird (mit Beschriftung darunter, z. B. Spielername).</summary>
public record BannerMon(string Species, string Caption);

/// <summary>
/// Zeichnet die großen Bilder für die Nachrichten: Banner, Status-Board und Tabellen.
/// Schlägt etwas fehl (z. B. keine Internetverbindung für Sprites), wird null zurückgegeben
/// und die Nachricht geht ohne Bild raus.
/// </summary>
public sealed class ImageRenderer
{
    // ---------------------------------------------------------------- Farben & Schriften

    private static readonly Color Background = Color.FromRgb(22, 23, 28);
    private static readonly Color Panel = Color.FromRgb(32, 34, 40);
    private static readonly Color PanelAlt = Color.FromRgb(38, 40, 47);
    private static readonly Color PanelTotal = Color.FromRgb(60, 62, 70);
    private static readonly Color Text = Color.FromRgb(240, 241, 245);
    private static readonly Color Muted = Color.FromRgb(150, 154, 165);

    public static Color ToColor(Tone tone) => tone switch
    {
        Tone.Green => Color.FromRgb(59, 165, 92),
        Tone.Yellow => Color.FromRgb(240, 178, 50),
        Tone.Red => Color.FromRgb(216, 60, 62),
        Tone.Blue => Color.FromRgb(88, 140, 240),
        Tone.Gold => Color.FromRgb(230, 194, 41),
        _ => Color.FromRgb(128, 132, 142)
    };

    private readonly FontFamily? _family;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ConcurrentDictionary<int, Image<Rgba32>?> _sprites = new();

    public ImageRenderer()
    {
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            var fonts = new FontCollection();
            _family = fonts.Add(System.IO.Path.Combine(dir, "DejaVuSans.ttf"));
            fonts.Add(System.IO.Path.Combine(dir, "DejaVuSans-Bold.ttf"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bilder] Schriftarten nicht gefunden, Bilder deaktiviert: {ex.Message}");
        }
    }

    private Font Bold(float size) => _family!.Value.CreateFont(size, FontStyle.Bold);
    private Font Regular(float size) => _family!.Value.CreateFont(size, FontStyle.Regular);

    // ---------------------------------------------------------------- Öffentliche Bilder

    /// <summary>
    /// Großes Banner: kleine Überschrift (kicker), großer Titel, bis zu 2 Textzeilen,
    /// rechts die Pokémon. Tote Pokémon werden grau gezeichnet.
    /// </summary>
    public Task<MemoryStream?> BannerAsync(Tone tone, string kicker, string title, IReadOnlyList<string> lines,
        IReadOnlyList<BannerMon> mons, bool grayscale = false) =>
        SafeRenderAsync(async () =>
        {
            const int W = 1200, H = 360;
            var accent = ToColor(tone);
            using var img = CreateBackground(W, H, accent);

            // Pokémon rechts
            var shown = mons.Take(6).ToList();
            var slot = shown.Count <= 3 ? 190 : 150;
            var x = W - 40 - slot * shown.Count;
            var textWidth = (shown.Count == 0 ? W - 56 : x - 30) - 56;

            var sprites = await Task.WhenAll(shown.Select(m => LoadSpriteAsync(m.Species)));

            img.Mutate(ctx =>
            {
                ctx.Fill(accent, new RectShape(0, H - 10, W, 10));
                ctx.DrawText(Fit(kicker.ToUpperInvariant(), Bold(30), textWidth), Bold(30), accent, new PointF(56, 58));
                ctx.DrawText(Fit(title, Bold(76), textWidth), Bold(76), Text, new PointF(52, 100));

                var y = 205f;
                foreach (var line in lines.Take(2))
                {
                    ctx.DrawText(Fit(line, Regular(32), textWidth), Regular(32), Muted, new PointF(56, y));
                    y += 44;
                }

                for (var i = 0; i < shown.Count; i++)
                {
                    var sx = x + slot * i;
                    ctx.Fill(Color.FromRgba(0, 0, 0, 90), new EllipseShape(sx + slot / 2f, H - 67, slot - 50, 22));

                    if (sprites[i] is { } sprite)
                    {
                        using var scaled = Scale(sprite, slot - 30, grayscale);
                        ctx.DrawImage(scaled, new Point(sx + (slot - scaled.Width) / 2, H - 70 - scaled.Height), 1f);
                    }

                    var caption = Fit(shown[i].Caption, Bold(22), slot - 10);
                    var cw = Width(caption, Bold(22));
                    ctx.DrawText(caption, Bold(22), Text, new PointF(sx + (slot - cw) / 2, H - 48));
                }
            });

            return await ToPngAsync(img);
        });

    /// <summary>Board aller (übergebenen) Routen eines Runs: pro Spieler eine Spalte mit Sprite und Namen.</summary>
    public Task<MemoryStream?> BoardAsync(Tone tone, string title, Run run, IReadOnlyList<RouteLink> links) =>
        SafeRenderAsync(async () =>
        {
            var players = run.Participants.OrderBy(p => p.PlayerId).ToList();
            const int RowH = 124, Top = 170, RouteCol = 340;
            var W = Math.Max(1200, RouteCol + players.Count * 300 + 48);
            var H = Top + RowH * Math.Max(links.Count, 1) + 30;
            var colW = (W - RouteCol - 48f) / Math.Max(players.Count, 1);

            // Alle benötigten Sprites vorab laden
            var species = links.SelectMany(l => l.Encounters)
                .Where(e => e.Status == EncounterStatus.Gefangen)
                .Select(e => e.Species).Distinct().ToList();
            var loaded = (await Task.WhenAll(species.Select(async s => (s, Sprite: await LoadSpriteAsync(s)))))
                .ToDictionary(x => x.s, x => x.Sprite);

            using var img = CreateBackground(W, H, ToColor(tone));
            img.Mutate(ctx =>
            {
                ctx.DrawText(Fit(title, Bold(52), W - 96), Bold(52), Text, new PointF(48, 40));
                ctx.DrawText("ROUTE", Bold(24), Muted, new PointF(48, Top - 44));
                for (var i = 0; i < players.Count; i++)
                    ctx.DrawText(Fit(players[i].Player.Name.ToUpperInvariant(), Bold(24), colW - 30), Bold(24), Muted,
                        new PointF(RouteCol + colW * i + 20, Top - 44));

                if (links.Count == 0)
                    ctx.DrawText("Noch keine Einträge", Regular(32), Muted, new PointF(64, Top + 40));

                var y = Top;
                for (var r = 0; r < links.Count; r++)
                {
                    var link = links[r];
                    var dead = link.Status == LinkStatus.Tot;

                    RoundedRect(ctx, 32, y + 6, W - 64, RowH - 12, 16, r % 2 == 0 ? Panel : PanelAlt);
                    RoundedRect(ctx, 32, y + 6, 12, RowH - 12, 6, ToColor(link.Status switch
                    {
                        LinkStatus.Lebendig => Tone.Green,
                        LinkStatus.Gebrochen => Tone.Yellow,
                        _ => Tone.Red
                    }));
                    ctx.DrawText(Fit(link.Route, Bold(28), RouteCol - 80), Bold(28), dead ? Muted : Text,
                        new PointF(64, y + (RowH - 36) / 2f));

                    for (var i = 0; i < players.Count; i++)
                    {
                        var cx = RouteCol + colW * i;
                        var e = link.Encounters.FirstOrDefault(x => x.PlayerId == players[i].PlayerId);

                        if (e is null)
                        {
                            ctx.DrawText("offen", Regular(26), Muted, new PointF(cx + 20, y + 44));
                            continue;
                        }
                        if (e.Status == EncounterStatus.Verpasst)
                        {
                            ctx.DrawText("verpasst", Regular(26), ToColor(Tone.Yellow), new PointF(cx + 20, y + 44));
                            continue;
                        }

                        if (loaded.GetValueOrDefault(e.Species) is { } sprite)
                        {
                            using var scaled = Scale(sprite, 96, dead);
                            ctx.DrawImage(scaled, new Point((int)(cx + 8 + (100 - scaled.Width) / 2f), y + (RowH - scaled.Height) / 2), 1f);
                        }

                        var hasNick = !string.IsNullOrWhiteSpace(e.Nickname);
                        ctx.DrawText(Fit(hasNick ? e.Nickname! : e.Species, Bold(26), colW - 118), Bold(26), dead ? Muted : Text,
                            new PointF(cx + 108, y + 32));
                        if (hasNick)
                            ctx.DrawText(Fit(e.Species, Regular(21), colW - 118), Regular(21), Muted, new PointF(cx + 108, y + 67));
                    }

                    y += RowH;
                }
            });

            return await ToPngAsync(img);
        });

    /// <summary>
    /// Große Tabelle: erste Spalte links (Namen), restliche Spalten rechtsbündig in ihrer Farbe.
    /// Die letzte Zeile kann als Summenzeile hervorgehoben werden.
    /// </summary>
    public Task<MemoryStream?> TableAsync(Tone tone, string title, string[] headers, Tone[] columnTones,
        IReadOnlyList<string[]> rows, bool lastRowIsTotal) =>
        SafeRenderAsync(async () =>
        {
            const int W = 1200, Pad = 48, RowH = 76, Top = 150, FirstCol = 380;
            var H = Top + RowH * Math.Max(rows.Count, 1) + 40;
            var colW = (W - Pad * 2f - FirstCol) / Math.Max(headers.Length - 1, 1);

            using var img = CreateBackground(W, H, ToColor(tone));
            img.Mutate(ctx =>
            {
                ctx.DrawText(Fit(title, Bold(52), W - Pad * 2), Bold(52), Text, new PointF(Pad, 40));

                ctx.DrawText(headers[0].ToUpperInvariant(), Bold(22), Muted, new PointF(Pad, Top - 40));
                for (var i = 1; i < headers.Length; i++)
                {
                    var right = Pad + FirstCol + colW * i - 10;
                    var h = headers[i].ToUpperInvariant();
                    ctx.DrawText(h, Bold(22), ToColor(columnTones[i - 1]), new PointF(right - Width(h, Bold(22)), Top - 40));
                }

                var y = Top;
                for (var r = 0; r < rows.Count; r++)
                {
                    var isTotal = lastRowIsTotal && r == rows.Count - 1;
                    RoundedRect(ctx, Pad - 16, y + 6, W - Pad * 2 + 32, RowH - 12, 14,
                        isTotal ? PanelTotal : r % 2 == 0 ? Panel : PanelAlt);

                    ctx.DrawText(Fit(rows[r][0], Bold(36), FirstCol - 20), Bold(36),
                        isTotal ? ToColor(Tone.Gold) : Text, new PointF(Pad + 6, y + 18));

                    for (var i = 1; i < rows[r].Length; i++)
                    {
                        var v = rows[r][i];
                        var right = Pad + FirstCol + colW * i - 10;
                        var color = v is "0" or "" ? Muted : ToColor(columnTones[i - 1]);
                        ctx.DrawText(v, Bold(40), color, new PointF(right - Width(v, Bold(40)), y + 15));
                    }

                    y += RowH;
                }
            });

            return await ToPngAsync(img);
        });

    /// <summary>
    /// Rückblick eines Runs: Ergebnis und Dauer, vier Kennzahlen, Zeitstrahl mit allen Fängen und Toden,
    /// Spieler-Statistik und der nummerierte Friedhof (Nummern passen zu den Markierungen im Zeitstrahl).
    /// </summary>
    public Task<MemoryStream?> RecapAsync(Run run, IReadOnlyList<PlayerStats> stats) =>
        SafeRenderAsync(async () =>
        {
            const int W = 1200, MaxDeaths = 8;
            var start = run.StartedAt;
            var end = run.EndedAt ?? DateTime.UtcNow;
            var total = Math.Max((end - start).TotalHours, 0.01);

            var (tone, result) = run.Status switch
            {
                RunStatus.Geschafft => (Tone.Gold, "Geschafft"),
                RunStatus.Gescheitert => (Tone.Red, "Gescheitert"),
                RunStatus.Abgebrochen => (Tone.Grey, "Abgebrochen"),
                _ => (Tone.Green, "Läuft seit")
            };
            var accent = ToColor(tone);

            var encounters = run.Links.SelectMany(l => l.Encounters).ToList();
            var deaths = run.Links.Where(l => l.Status == LinkStatus.Tot && l.DiedAt != null).OrderBy(l => l.DiedAt).ToList();
            var shown = deaths.Take(MaxDeaths).ToList();

            // Sprites der gezeigten Toten vorab laden
            var deadSpecies = shown.SelectMany(l => l.Encounters)
                .Where(e => e.Status == EncounterStatus.Gefangen).Select(e => e.Species).Distinct().ToList();
            var sprites = (await Task.WhenAll(deadSpecies.Select(async sp => (sp, Img: await LoadSpriteAsync(sp)))))
                .ToDictionary(x => x.sp, x => x.Img);

            var H = 40 + 150 + 150 + 180 + 50 + 60 * stats.Count + 104 + 96 * Math.Max(1, shown.Count)
                    + (deaths.Count > MaxDeaths ? 40 : 0) + 40;

            using var img = CreateBackground(W, H, accent);
            img.Mutate(ctx =>
            {
                // Kopf
                var duration = FormatDuration(end - start);
                ctx.DrawText($"RÜCKBLICK · RUN #{run.Number}", Bold(30), accent, new PointF(56, 40));
                ctx.DrawText(Fit(run.Status == RunStatus.Aktiv ? $"{result} {duration}" : $"{result} nach {duration}", Bold(64), W - 104),
                    Bold(64), Text, new PointF(52, 82));

                // Kennzahlen
                (string Value, string Label, Color Color)[] tiles =
                [
                    (FormatDuration(end - start, compact: true), "Dauer", Text),
                    ($"{run.Links.Count}", "Routen", Text),
                    ($"{encounters.Count(e => e.Status == EncounterStatus.Gefangen)}", "Fänge", ToColor(Tone.Blue)),
                    ($"{deaths.Count}", "Tode", ToColor(Tone.Red))
                ];
                var tileW = (W - 96 - 3 * 24) / 4f;
                for (var i = 0; i < tiles.Length; i++)
                {
                    var tx = 48 + i * (tileW + 24);
                    RoundedRect(ctx, tx, 190, tileW, 120, 16, Panel);
                    ctx.DrawText(Fit(tiles[i].Value, Bold(52), tileW - 40), Bold(52), tiles[i].Color, new PointF(tx + 24, 206));
                    ctx.DrawText(tiles[i].Label.ToUpperInvariant(), Bold(20), Muted, new PointF(tx + 24, 274));
                }

                // Zeitstrahl
                var y = 350f;
                ctx.DrawText("ZEITSTRAHL", Bold(24), Muted, new PointF(48, y));
                var lx = W - 48 - 560f;
                foreach (var (tone2, label) in new[] { (Tone.Green, "Fang"), (Tone.Yellow, "Verpasst"), (Tone.Red, "Tod") })
                {
                    RoundedRect(ctx, lx, y + 6, 14, 14, 3, ToColor(tone2));
                    ctx.DrawText(label, Regular(20), Muted, new PointF(lx + 22, y));
                    lx += 22 + Width(label, Regular(20)) + 28;
                }

                var barY = y + 90;
                const float X0 = 80, X1 = W - 80;
                float At(DateTime t) => X0 + (X1 - X0) * (float)Math.Clamp((t - start).TotalHours / total, 0, 1);

                RoundedRect(ctx, X0, barY - 4, X1 - X0, 8, 4, PanelAlt);
                foreach (var e in encounters.OrderBy(e => e.CreatedAt))
                {
                    var color = ToColor(e.Status == EncounterStatus.Gefangen ? Tone.Green : Tone.Yellow);
                    ctx.DrawLine(color, 4, new PointF(At(e.CreatedAt), barY - 18), new PointF(At(e.CreatedAt), barY + 18));
                }
                for (var i = 0; i < deaths.Count; i++)
                    NumberBadge(ctx, At(deaths[i].DiedAt!.Value), barY, i + 1);

                ctx.DrawText("Start", Regular(20), Muted, new PointF(X0, barY + 34));
                var endLabel = run.Status == RunStatus.Aktiv ? "Jetzt" : "Ende";
                ctx.DrawText(endLabel, Regular(20), Muted, new PointF(X1 - Width(endLabel, Regular(20)), barY + 34));

                // Spieler
                y = barY + 90;
                ctx.DrawText("SPIELER", Bold(24), Muted, new PointF(48, y));
                (string Head, Tone Tone)[] heads = [("Gefangen", Tone.Blue), ("Verpasst", Tone.Yellow), ("Tode", Tone.Red)];
                for (var i = 0; i < heads.Length; i++)
                {
                    var right = 700 + i * 170;
                    var head = heads[i].Head.ToUpperInvariant();
                    ctx.DrawText(head, Bold(20), ToColor(heads[i].Tone), new PointF(right - Width(head, Bold(20)), y + 4));
                }
                y += 44;
                for (var r = 0; r < stats.Count; r++)
                {
                    RoundedRect(ctx, 32, y + 4, W - 64, 52, 12, r % 2 == 0 ? Panel : PanelAlt);
                    ctx.DrawText(Fit(stats[r].Name, Bold(28), 380), Bold(28), Text, new PointF(56, y + 14));
                    int[] values = [stats[r].Caught, stats[r].Missed, stats[r].Deaths];
                    for (var i = 0; i < values.Length; i++)
                    {
                        var v = values[i].ToString();
                        var right = 700 + i * 170;
                        ctx.DrawText(v, Bold(30), values[i] == 0 ? Muted : ToColor(heads[i].Tone),
                            new PointF(right - Width(v, Bold(30)), y + 12));
                    }
                    y += 60;
                }

                // Friedhof
                y += 30;
                ctx.DrawText("FRIEDHOF", Bold(24), Muted, new PointF(48, y));
                y += 44;
                if (shown.Count == 0)
                {
                    ctx.DrawText("Niemand gestorben – Respekt!", Regular(28), Muted, new PointF(56, y + 20));
                    y += 96;
                }

                for (var i = 0; i < shown.Count; i++)
                {
                    var link = shown[i];
                    RoundedRect(ctx, 32, y + 6, W - 64, 84, 14, i % 2 == 0 ? Panel : PanelAlt);
                    NumberBadge(ctx, 70, y + 48, i + 1);

                    var caught = link.Encounters.Where(e => e.Status == EncounterStatus.Gefangen)
                        .OrderBy(e => e.PlayerId).Take(4).ToList();
                    var spritesX = W - 56 - caught.Count * 84;

                    ctx.DrawText(Fit(link.Route, Bold(28), spritesX - 140), Bold(28), Text, new PointF(112, y + 16));
                    var info = $"{FormatElapsed(link.DiedAt!.Value - start)} · {link.DeathCause}" +
                               (link.CausedByPlayer is null ? "" : $" ({link.CausedByPlayer.Name})");
                    ctx.DrawText(Fit(info, Regular(21), spritesX - 140), Regular(21), Muted, new PointF(112, y + 54));

                    foreach (var e in caught)
                    {
                        if (sprites.GetValueOrDefault(e.Species) is { } sprite)
                        {
                            using var scaled = Scale(sprite, 72, grayscale: true);
                            ctx.DrawImage(scaled, new Point(spritesX + (84 - scaled.Width) / 2, (int)(y + 48 - scaled.Height / 2f)), 1f);
                        }
                        spritesX += 84;
                    }
                    y += 96;
                }

                if (deaths.Count > MaxDeaths)
                    ctx.DrawText($"+ {deaths.Count - MaxDeaths} weitere", Regular(22), Muted, new PointF(56, y + 6));
            });

            return await ToPngAsync(img);
        });

    /// <summary>Roter Kreis mit Nummer (Zeitstrahl und Friedhof).</summary>
    private void NumberBadge(IImageProcessingContext ctx, float cx, float cy, int number)
    {
        ctx.Fill(ToColor(Tone.Red), new EllipseShape(cx, cy, 36, 36));
        var text = number.ToString();
        ctx.DrawText(text, Bold(18), Text, new PointF(cx - Width(text, Bold(18)) / 2, cy - 11));
    }

    /// <summary>"3 Tagen", "5 Stunden", "40 Minuten" bzw. kompakt "3 T 5 h".</summary>
    private static string FormatDuration(TimeSpan span, bool compact = false)
    {
        if (compact)
            return span.TotalDays >= 1 ? $"{(int)span.TotalDays} T {span.Hours} h"
                : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} m"
                : $"{Math.Max(1, span.Minutes)} m";

        return span.TotalDays >= 1 ? $"{(int)span.TotalDays} {((int)span.TotalDays == 1 ? "Tag" : "Tagen")}"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} {((int)span.TotalHours == 1 ? "Stunde" : "Stunden")}"
            : $"{Math.Max(1, span.Minutes)} Minuten";
    }

    /// <summary>Zeit seit Run-Start, z. B. "+1T 4h" oder "+3h 12m".</summary>
    private static string FormatElapsed(TimeSpan span) =>
        span.TotalDays >= 1 ? $"+{(int)span.TotalDays}T {span.Hours}h"
        : span.TotalHours >= 1 ? $"+{(int)span.TotalHours}h {span.Minutes}m"
        : $"+{Math.Max(0, span.Minutes)}m";

    // ---------------------------------------------------------------- Bausteine

    private async Task<MemoryStream?> SafeRenderAsync(Func<Task<MemoryStream>> render)
    {
        if (_family is null) return null;
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

    /// <summary>Dunkler Hintergrund mit Farbverlauf in der Akzentfarbe und dezenten Diagonalstreifen.</summary>
    private static Image<Rgba32> CreateBackground(int w, int h, Color accent)
    {
        var img = new Image<Rgba32>(w, h, Background.ToPixel<Rgba32>());
        var a = accent.ToPixel<Rgba32>();
        var bg = Background.ToPixel<Rgba32>();
        var tinted = Color.FromRgb(
            (byte)(bg.R + (a.R - bg.R) * 0.45f),
            (byte)(bg.G + (a.G - bg.G) * 0.45f),
            (byte)(bg.B + (a.B - bg.B) * 0.45f));

        img.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(w * 0.75f, 0),
                GradientRepetitionMode.None, new ColorStop(0, tinted), new ColorStop(1, Background)));

            var stripe = Color.FromRgba(255, 255, 255, 7);
            for (var x = -h; x < w; x += 36)
                ctx.DrawLine(stripe, 12, new PointF(x, h), new PointF(x + h, 0));
        });
        return img;
    }

    /// <summary>Abgerundetes Rechteck aus zwei Rechtecken und vier Kreisen (nur deckende Farben!).</summary>
    private static void RoundedRect(IImageProcessingContext ctx, float x, float y, float w, float h, float r, Color color)
    {
        ctx.Fill(color, new RectShape(x + r, y, w - 2 * r, h));
        ctx.Fill(color, new RectShape(x, y + r, w, h - 2 * r));
        foreach (var (cx, cy) in new[] { (x + r, y + r), (x + w - r, y + r), (x + r, y + h - r), (x + w - r, y + h - r) })
            ctx.Fill(color, new EllipseShape(cx, cy, 2 * r, 2 * r));
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

    /// <summary>Sprite pixelgenau (Nearest Neighbor) auf Zielgröße vergrößern, optional grau.</summary>
    private static Image<Rgba32> Scale(Image<Rgba32> sprite, int target, bool grayscale)
    {
        var factor = Math.Max(1, Math.Min(target / sprite.Width, target / sprite.Height));
        return sprite.Clone(ctx =>
        {
            ctx.Resize(sprite.Width * factor, sprite.Height * factor, KnownResamplers.NearestNeighbor);
            if (grayscale) ctx.Grayscale().Opacity(0.75f);
        });
    }

    /// <summary>Sprite aus Schwarz/Weiß laden (einmal pro Pokémon, danach aus dem Cache) und auf den Inhalt zuschneiden.</summary>
    private async Task<Image<Rgba32>?> LoadSpriteAsync(string species)
    {
        if (Pokedex.Number(species) is not { } nr) return null;
        if (_sprites.TryGetValue(nr, out var cached)) return cached;

        Image<Rgba32>? sprite = null;
        try
        {
            var url = $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-v/black-white/{nr}.png";
            await using var stream = await _http.GetStreamAsync(url);
            sprite = await Image.LoadAsync<Rgba32>(stream);
            CropToContent(sprite);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bilder] Sprite #{nr} nicht ladbar: {ex.Message}");
            sprite?.Dispose();
            return null; // nicht cachen – beim nächsten Mal neu versuchen
        }

        return _sprites.GetOrAdd(nr, sprite);
    }

    /// <summary>Transparenten Rand entfernen, damit die Sprites gleichmäßig groß wirken.</summary>
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
