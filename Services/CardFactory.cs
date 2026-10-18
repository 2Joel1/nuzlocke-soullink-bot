using Discord;
using NuzlockeBot.Data;

namespace NuzlockeBot.Services;

/// <summary>
/// Eine Nachricht-Karte: Embed plus (optional) großes Bild darunter.
/// Ist <see cref="Image"/> null (Rendern fehlgeschlagen), enthält das Embed den kompletten Text als Fallback.
/// </summary>
public sealed record Card(EmbedBuilder Embed, MemoryStream? Image, string FileName);

/// <summary>Baut die Karten für alle Nachrichten mit Bild.</summary>
public class CardFactory(ImageRenderer renderer)
{
    private static List<BannerMon> CaughtMons(Run run, RouteLink link) =>
        run.Participants.OrderBy(p => p.PlayerId)
            .Select(p => (p.Player, Encounter: link.Encounters.FirstOrDefault(e => e.PlayerId == p.PlayerId)))
            .Where(x => x.Encounter?.Status == EncounterStatus.Gefangen)
            .Select(x => new BannerMon(x.Encounter!.Species, x.Player.Name))
            .ToList();

    // ---------------------------------------------------------------- Fang / Verpasst / Tod

    public async Task<Card> CatchAsync(Run run, RouteLink link, Encounter me, string statusLine)
    {
        var embed = Formatting.LinkEmbedBuilder($"🎯 {link.Route}: {Formatting.PokemonText(me)} gefangen",
            run, link, Formatting.Green, statusLine, me.Species);
        var image = await renderer.BannerAsync(Tone.Green, $"Fang · Run #{run.Number}", link.Route,
            [$"{me.Player.Name}: {Formatting.PokemonText(me)}", StripMarkdown(statusLine)], CaughtMons(run, link));
        return new Card(embed, image, "fang.png");
    }

    public async Task<Card> MissAsync(Run run, RouteLink link, Encounter me)
    {
        var embed = Formatting.LinkEmbedBuilder($"🟡 {link.Route}: Encounter verpasst", run, link, Formatting.Yellow,
            "Der Soul Link dieser Route ist gebrochen – gefangene Partner sind nicht nutzbar.", me.Species);
        var image = await renderer.BannerAsync(Tone.Yellow, $"Verpasst · Run #{run.Number}", link.Route,
            [$"{me.Player.Name} hat den Encounter verpasst", "Soul Link gebrochen – Partner nicht nutzbar"], CaughtMons(run, link));
        return new Card(embed, image, "verpasst.png");
    }

    public async Task<Card> DeathAsync(Run run, RouteLink link, Encounter me)
    {
        var alive = run.Links.Count(l => l.Status == LinkStatus.Lebendig);
        var embed = Formatting.LinkEmbedBuilder($"💀 Soul Link gerissen: {link.Route}", run, link, Formatting.Red,
            $"**Ursache:** {link.DeathCause}\n**Ausgelöst von:** {link.CausedByPlayer?.Name}\n\nNoch {alive} lebende Links." +
            (alive == 0 ? "\n\n☠️ **Keine lebenden Links mehr** – Zeit für `/run ende`?" : ""),
            me.Species);
        var image = await renderer.BannerAsync(Tone.Red, $"Soul Link gerissen · Run #{run.Number}", link.Route,
            [link.DeathCause ?? "", $"ausgelöst von {link.CausedByPlayer?.Name} · noch {alive} lebende Links"],
            CaughtMons(run, link), grayscale: true);
        return new Card(embed, image, "tod.png");
    }

    // ---------------------------------------------------------------- Runs

    public async Task<Card> RunStartAsync(Run run, int previous)
    {
        var names = string.Join(", ", run.Participants.OrderBy(p => p.PlayerId).Select(p => p.Player.Name));
        var embed = new EmbedBuilder()
            .WithTitle($"🎮 Run #{run.Number} gestartet")
            .WithDescription($"**Spieler:** {names}\n**Bisher verbrauchte Runs:** {previous}\n\n" +
                             "Viel Glück – und Finger weg vom Kritischen Treffer.")
            .WithColor(Formatting.Green);
        var image = await renderer.BannerAsync(Tone.Green, "Neuer Run", $"Run #{run.Number}",
            [$"Spieler: {names}", $"Bisher verbrauchte Runs: {previous}"], []);
        return new Card(embed, image, "run-start.png");
    }

    public async Task<Card> RunEndAsync(Run run)
    {
        var (tone, word) = run.Status switch
        {
            RunStatus.Geschafft => (Tone.Gold, "geschafft!"),
            RunStatus.Gescheitert => (Tone.Red, "gescheitert"),
            _ => (Tone.Grey, "abgebrochen")
        };

        // Bei Erfolg das überlebende Team zeigen, sonst den letzten Soul Link, der gestorben ist
        List<BannerMon> mons;
        if (run.Status == RunStatus.Geschafft)
        {
            mons = run.Links.Where(l => l.Status == LinkStatus.Lebendig)
                .SelectMany(l => CaughtMons(run, l)).Take(6).ToList();
        }
        else
        {
            var last = run.Links.Where(l => l.Status == LinkStatus.Tot).OrderByDescending(l => l.DiedAt).FirstOrDefault();
            mons = last is null ? new List<BannerMon>() : CaughtMons(run, last);
        }

        var dead = run.Links.Count(l => l.Status == LinkStatus.Tot);
        var embed = new EmbedBuilder()
            .WithTitle($"Run #{run.Number} {word}")
            .WithDescription((run.EndReason is null ? "" : $"**Grund:** {run.EndReason}\n") +
                             $"{run.Links.Count} Routen · {dead} Soul Links gestorben")
            .WithColor(tone switch { Tone.Gold => Formatting.Gold, Tone.Red => Formatting.Red, _ => Formatting.Grey });
        var image = await renderer.BannerAsync(tone, "Run beendet", $"Run #{run.Number} {word}",
            [run.EndReason ?? "", $"{run.Links.Count} Routen · {dead} Soul Links gestorben"],
            mons, grayscale: run.Status != RunStatus.Geschafft);
        return new Card(embed, image, "run-ende.png");
    }

    /// <summary>Status als Board: alle Routen, lebende zuerst.</summary>
    public async Task<Card> StatusAsync(Run run, string fileName = "status.png")
    {
        var links = run.Links.OrderBy(l => l.Status).ThenBy(l => l.Id).ToList();
        var tone = run.Status switch
        {
            RunStatus.Aktiv => Tone.Green,
            RunStatus.Geschafft => Tone.Gold,
            RunStatus.Gescheitert => Tone.Red,
            _ => Tone.Grey
        };

        var image = await renderer.BoardAsync(tone, $"Run #{run.Number} – {StripEmoji(Formatting.RunStatusText(run.Status))}", run, links);
        var embed = image is null
            ? Formatting.RunOverviewBuilder(run)
            : new EmbedBuilder()
                .WithTitle($"📋 Run #{run.Number}")
                .WithDescription($"**Status:** {Formatting.RunStatusText(run.Status)} · gestartet {Formatting.Timestamp(run.StartedAt)}" +
                                 (run.EndReason is null ? "" : $"\n**Grund:** {run.EndReason}"))
                .WithColor(Formatting.Green)
                .WithFooter(Formatting.Counts(run));
        return new Card(embed, image, fileName);
    }

    public async Task<Card> GraveyardAsync(Run run)
    {
        var dead = run.Links.Where(l => l.Status == LinkStatus.Tot).OrderBy(l => l.DiedAt).ToList();
        var image = dead.Count == 0 ? null : await renderer.BoardAsync(Tone.Red, $"Friedhof – Run #{run.Number}", run, dead);
        return new Card(Formatting.GraveyardEmbedBuilder(run), image, "friedhof.png");
    }

    // ---------------------------------------------------------------- Tabellen

    public async Task<Card> StatsAsync(string title, IReadOnlyList<PlayerStats> stats, bool global, Tone tone, string fileName)
    {
        var headers = global
            ? new[] { "Spieler", "Runs", "Lebend", "Gefangen", "Tode", "Verpasst" }
            : new[] { "Spieler", "Lebend", "Gefangen", "Tode", "Verpasst" };
        var tones = global
            ? new[] { Tone.Grey, Tone.Green, Tone.Blue, Tone.Red, Tone.Yellow }
            : new[] { Tone.Green, Tone.Blue, Tone.Red, Tone.Yellow };

        var rows = stats.Select(s => global
                ? new[] { s.Name, $"{s.Runs}", $"{s.Alive}", $"{s.Caught}", $"{s.Deaths}", $"{s.Missed}" }
                : new[] { s.Name, $"{s.Alive}", $"{s.Caught}", $"{s.Deaths}", $"{s.Missed}" })
            .ToList();

        var withTotal = stats.Count > 1;
        if (withTotal)
        {
            string[] sums = [$"{stats.Sum(s => s.Alive)}", $"{stats.Sum(s => s.Caught)}", $"{stats.Sum(s => s.Deaths)}", $"{stats.Sum(s => s.Missed)}"];
            rows.Add((global ? new[] { "Gesamt", "" } : new[] { "Gesamt" }).Concat(sums).ToArray());
        }

        var image = await renderer.TableAsync(tone, StripEmoji(title), headers, tones, rows, withTotal);
        var embed = image is null
            ? Formatting.StatsEmbedBuilder(title, stats, global, tone == Tone.Gold ? Formatting.Gold : Formatting.Green)
            : new EmbedBuilder()
                .WithTitle(title)
                .WithColor(tone == Tone.Gold ? Formatting.Gold : Formatting.Green)
                .WithFooter((global ? "Lebend = im laufenden Run · " : "") + "Tode = Soul Links, deren Tod der Spieler ausgelöst hat");
        return new Card(embed, image, fileName);
    }

    // ---------------------------------------------------------------- Erfolge, Titel, Menü

    public async Task<Card> AchievementAsync(IReadOnlyList<UnlockedAchievement> unlocked)
    {
        var title = unlocked.Count == 1 ? unlocked[0].Achievement.Name : $"{unlocked.Count} neue Erfolge";
        var lines = unlocked.Count == 1
            ? new List<string> { $"{unlocked[0].Player.Name}: {unlocked[0].Achievement.Description}" }
            : unlocked.Take(2).Select(u => $"{u.Player.Name}: {u.Achievement.Name}")
                .Append(unlocked.Count > 2 ? $"… und {unlocked.Count - 2} weitere" : "")
                .Take(2).ToList();
        var mons = unlocked.Where(u => u.Species is not null)
            .Select(u => new BannerMon(u.Species!, u.Player.Name)).Take(3).ToList();

        var image = await renderer.BannerAsync(Tone.Gold, "Erfolg freigeschaltet", title, lines, mons);
        return new Card(Formatting.AchievementAnnouncementBuilder(unlocked), image, "erfolg.png");
    }

    public async Task<Card> TitlesAsync(IReadOnlyList<PlayerTitle> titles, IReadOnlyList<(string Name, int Count)> counts, int total)
    {
        var rows = counts.Select(c => new[] { c.Name, $"{c.Count}/{total}" }).ToList();
        var image = rows.Count == 0
            ? null
            : await renderer.TableAsync(Tone.Gold, "Erfolge pro Spieler", ["Spieler", "Erfolge"], [Tone.Gold], rows, false);
        return new Card(Formatting.TitlesEmbedBuilder(titles, counts, total, includeTable: image is null), image, "titel.png");
    }

    public async Task<Card> MenuAsync()
    {
        var image = await renderer.BannerAsync(Tone.Green, "Bedienfeld", "Nuzlocke Soul Link",
            ["Pokémon Schwarz · Randomizer", "Alle Funktionen per Button"], []);
        return new Card(Formatting.MenuEmbedBuilder(), image, "menu.png");
    }

    // ---------------------------------------------------------------- Text-Helfer

    /// <summary>Emojis entfernen (die Bild-Schriftart kann keine Emojis darstellen).</summary>
    private static string StripEmoji(string text) =>
        new string(text.Where(c => !char.IsSurrogate(c) && c != '️' && c is < '☀' or > '➿').ToArray()).Trim();

    private static string StripMarkdown(string text) => StripEmoji(text.Replace("**", "").Replace("`", ""));
}
