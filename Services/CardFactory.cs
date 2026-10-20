using Discord;
using NuzlockeBot.Data;

namespace NuzlockeBot.Services;

/// <summary>
/// Eine Nachricht-Karte: Embed plus (optional) großes Bild darunter.
/// Ist <see cref="Image"/> null (Rendern fehlgeschlagen), enthält das Embed den kompletten Text als Fallback.
/// </summary>
public sealed record Card(EmbedBuilder Embed, MemoryStream? Image, string FileName);

public static class CardExtensions
{
    /// <summary>
    /// Wandelt Karten in Embeds + Dateianhänge um. Jede Karte mit Bild verweist per "attachment://" auf ihre Datei;
    /// das große Bild ersetzt dabei das kleine Vorschaubild.
    /// </summary>
    public static (Embed[] Embeds, List<FileAttachment> Files) Prepare(this IEnumerable<Card> cards)
    {
        var list = cards.ToList();
        var embeds = list.Select(c =>
        {
            if (c.Image is not null)
            {
                c.Embed.ThumbnailUrl = null;
                c.Embed.WithImageUrl($"attachment://{c.FileName}");
            }
            return c.Embed.Build();
        }).ToArray();

        var files = list.Where(c => c.Image is not null).Select(c => new FileAttachment(c.Image!, c.FileName)).ToList();
        return (embeds, files);
    }
}

/// <summary>Baut die Karten für alle Nachrichten mit Bild.</summary>
public class CardFactory(ImageRenderer renderer)
{
    private static List<BannerMon> CaughtMons(Run run, RouteLink link) =>
        run.Participants.OrderBy(p => p.PlayerId)
            .Select(p => (p.Player, Encounter: link.Encounters.FirstOrDefault(e => e.PlayerId == p.PlayerId)))
            .Where(x => x.Encounter?.Status == EncounterStatus.Gefangen)
            .Select(x => new BannerMon(x.Encounter!.Species, x.Player.Name))
            .ToList();

    /// <summary>Alle Teilnehmer mit ihrem Pokémon auf dieser Route – wer noch nichts hat, bekommt ein "?".</summary>
    private static List<BannerMon> LinkMons(Run run, RouteLink link) =>
        run.Participants.OrderBy(p => p.PlayerId)
            .Select(p => (p.Player, Encounter: link.Encounters.FirstOrDefault(e => e.PlayerId == p.PlayerId)))
            .Select(x => new BannerMon(x.Encounter?.Status == EncounterStatus.Gefangen ? x.Encounter.Species : "", x.Player.Name))
            .ToList();

    private static string Up(string? s) => (s ?? "").ToUpperInvariant();

    public static SceneMood MoodFor(RunStatus status) => status switch
    {
        RunStatus.Aktiv => SceneMood.Day,
        RunStatus.Geschafft => SceneMood.Gold,
        RunStatus.Gescheitert => SceneMood.Night,
        _ => SceneMood.Grey
    };

    /// <summary>"AKTIV", "GESCHAFFT", "GESCHEITERT", "ABGEBROCHEN".</summary>
    public static string StatusWord(RunStatus status) => status switch
    {
        RunStatus.Aktiv => "AKTIV",
        RunStatus.Geschafft => "GESCHAFFT",
        RunStatus.Gescheitert => "GESCHEITERT",
        _ => "ABGEBROCHEN"
    };

    /// <summary>"MAX und LISA" bzw. "JOEL, MAX und LISA".</summary>
    private static string NameList(IEnumerable<string> names)
    {
        var list = names.Select(Up).ToList();
        return list.Count <= 1 ? string.Join("", list) : $"{string.Join(", ", list[..^1])} und {list[^1]}";
    }

    /// <summary>"BOB (KRABBY)" bzw. "ein KRABBY".</summary>
    private static string MonText(Encounter e, bool article = false) =>
        string.IsNullOrWhiteSpace(e.Nickname) ? (article ? $"ein {Up(e.Species)}" : Up(e.Species)) : $"{Up(e.Nickname)} ({Up(e.Species)})";

    private static int CaughtCount(Run run, RouteLink link) =>
        run.Participants.Count(p => link.Encounters.Any(e => e.PlayerId == p.PlayerId && e.Status == EncounterStatus.Gefangen));

    // ---------------------------------------------------------------- Fang / Verpasst / Tod

    public async Task<Card> CatchAsync(Run run, RouteLink link, Encounter me, string statusLine)
    {
        var embed = Formatting.LinkEmbedBuilder($"🎯 {link.Route}: {Formatting.PokemonText(me)} gefangen",
            run, link, Formatting.Green, statusLine, me.Species);

        var missing = run.Participants.Count(p => link.Encounters.All(e => e.PlayerId != p.PlayerId));
        var others = run.Participants.Where(p => p.PlayerId != me.PlayerId).OrderBy(p => p.PlayerId).Select(p => p.Player.Name).ToList();
        var line2 = link.Status == LinkStatus.Gebrochen ? "Ein Partner hat verpasst. Ab in die Box!"
            : missing > 0 ? $"Noch {missing} Spieler offen auf dieser Route."
            : others.Count > 0 ? $"Soul Link mit {NameList(others)} steht." : "Der Soul Link steht.";

        var image = await renderer.BattleAsync(new BattleBanner(SceneMood.Day, Up(link.Route), $"RUN {run.Number}",
            $"{Up(me.Player.Name)} hat {MonText(me, article: true)} gefangen!", line2,
            LinkMons(run, link), CaughtCount(run, link), run.Participants.Count));
        return new Card(embed, image, "fang.png");
    }

    public async Task<Card> MissAsync(Run run, RouteLink link, Encounter me)
    {
        var embed = Formatting.LinkEmbedBuilder($"🟡 {link.Route}: Encounter verpasst", run, link, Formatting.Yellow,
            "Der Soul Link dieser Route ist gebrochen – gefangene Partner sind nicht nutzbar.", me.Species);
        var image = await renderer.BattleAsync(new BattleBanner(SceneMood.Dusk, Up(link.Route), $"RUN {run.Number}",
            $"{Up(me.Player.Name)} hat den Encounter verpasst…", "Soul Link gebrochen. Partner in die Box!",
            LinkMons(run, link), CaughtCount(run, link), run.Participants.Count));
        return new Card(embed, image, "verpasst.png");
    }

    public async Task<Card> DeathAsync(Run run, RouteLink link, Encounter me)
    {
        var alive = run.Links.Count(l => l.Status == LinkStatus.Lebendig);
        var embed = Formatting.LinkEmbedBuilder($"💀 Soul Link gerissen: {link.Route}", run, link, Formatting.Red,
            $"**Ursache:** {link.DeathCause}\n**Ausgelöst von:** {link.CausedByPlayer?.Name}\n\nNoch {alive} lebende Links." +
            (alive == 0 ? "\n\n☠️ **Keine lebenden Links mehr** – Zeit für `/run ende`?" : ""),
            me.Species);

        var fallen = link.Encounters.Where(e => e.Status == EncounterStatus.Gefangen).OrderBy(e => e.PlayerId)
            .Select(e => string.IsNullOrWhiteSpace(e.Nickname) ? e.Species : e.Nickname!).ToList();
        var line2 = fallen.Count == 1 ? $"{Up(fallen[0])} ist fort…" : $"{NameList(fallen)} sind fort…";
        if (line2.Length > 45) line2 = $"Alle {fallen.Count} Partner sind fort…";

        var image = await renderer.BattleAsync(new BattleBanner(SceneMood.Night, Up(link.Route), $"RUN {run.Number}",
            $"{link.DeathCause?.TrimEnd('!', '.', ' ')}!", line2, CaughtMons(run, link), 0, run.Participants.Count, Grayscale: true));
        return new Card(embed, image, "tod.png");
    }

    // ---------------------------------------------------------------- Runs

    public async Task<Card> RunStartAsync(Run run, int previous)
    {
        var names = run.Participants.OrderBy(p => p.PlayerId).Select(p => p.Player.Name).ToList();
        var embed = new EmbedBuilder()
            .WithTitle($"🎮 Run #{run.Number} gestartet")
            .WithDescription($"**Spieler:** {string.Join(", ", names)}\n**Bisher verbrauchte Runs:** {previous}\n\n" +
                             "Viel Glück – und Finger weg vom Kritischen Treffer.")
            .WithColor(Formatting.Green);
        var image = await renderer.BattleAsync(new BattleBanner(SceneMood.Dawn, $"RUN {run.Number}",
            previous == 1 ? "1 RUN VERBRAUCHT" : $"{previous} RUNS VERBRAUCHT",
            $"Run {run.Number} beginnt! Viel Glück!", $"Mit {NameList(names)}.",
            names.Select(n => new BannerMon("", n)).ToList()));
        return new Card(embed, image, "run-start.png");
    }

    public async Task<Card> RunEndAsync(Run run)
    {
        var (mood, word) = run.Status switch
        {
            RunStatus.Geschafft => (SceneMood.Gold, "geschafft!"),
            RunStatus.Gescheitert => (SceneMood.Night, "gescheitert"),
            _ => (SceneMood.Grey, "abgebrochen")
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
        var alive = run.Links.Count(l => l.Status == LinkStatus.Lebendig);
        var embed = new EmbedBuilder()
            .WithTitle($"Run #{run.Number} {word}")
            .WithDescription((run.EndReason is null ? "" : $"**Grund:** {run.EndReason}\n") +
                             $"{run.Links.Count} Routen · {dead} Soul Links gestorben")
            .WithColor(mood switch { SceneMood.Gold => Formatting.Gold, SceneMood.Night => Formatting.Red, _ => Formatting.Grey });

        var line1 = run.Status switch
        {
            RunStatus.Geschafft => "Ihr habt es geschafft! Glückwunsch!",
            RunStatus.Gescheitert => $"Run {run.Number} ist vorbei…",
            _ => $"Run {run.Number} wurde abgebrochen."
        };
        var stats = $"{run.Links.Count} Routen, {dead} Soul Links gestorben.";
        var image = await renderer.BattleAsync(new BattleBanner(mood, $"RUN {run.Number}", Up(word),
            line1, string.IsNullOrWhiteSpace(run.EndReason) ? stats : run.EndReason!,
            mons, alive, Math.Max(run.Links.Count, 1), Grayscale: run.Status != RunStatus.Geschafft));
        return new Card(embed, image, "run-ende.png");
    }

    /// <summary>Rückblick mit Kennzahlen, Zeitstrahl, Spielern und Friedhof.</summary>
    public async Task<Card> RecapAsync(Run run)
    {
        var stats = RunService.ComputeStats([run], aliveIn: run);
        var dead = run.Links.Where(l => l.Status == LinkStatus.Tot).ToList();
        var end = run.EndedAt ?? DateTime.UtcNow;
        var hours = (end - run.StartedAt).TotalHours;
        var topCause = dead.Where(l => !string.IsNullOrWhiteSpace(l.DeathCause))
            .GroupBy(l => l.DeathCause!.Trim().ToLowerInvariant())
            .OrderByDescending(g => g.Count()).FirstOrDefault()?.First().DeathCause;

        var embed = new EmbedBuilder()
            .WithTitle($"📜 Rückblick – Run #{run.Number}")
            .WithDescription(
                $"**Dauer:** {(hours >= 24 ? $"{hours / 24:0.#} Tage" : $"{hours:0.#} Stunden")}\n" +
                $"**Routen:** {run.Links.Count} · **Fänge:** {stats.Sum(x => x.Caught)} · **Tode:** {dead.Count}" +
                (topCause is null ? "" : $"\n**Häufigste Todesursache:** {topCause}"))
            .WithColor(run.Status switch
            {
                RunStatus.Geschafft => Formatting.Gold,
                RunStatus.Gescheitert => Formatting.Red,
                RunStatus.Aktiv => Formatting.Green,
                _ => Formatting.Grey
            });

        var image = await renderer.RecapAsync(run, stats);
        return new Card(embed, image, "rueckblick.png");
    }

    /// <summary>Status als Board: alle Routen, lebende zuerst.</summary>
    public async Task<Card> StatusAsync(Run run, string fileName = "status.png")
    {
        var links = run.Links.OrderBy(l => l.Status).ThenBy(l => l.Id).ToList();
        var image = await renderer.BoardAsync(MoodFor(run.Status), $"RUN {run.Number}", run, links);
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
        var image = dead.Count == 0 ? null : await renderer.BoardAsync(SceneMood.Night, "FRIEDHOF", run, dead);
        return new Card(Formatting.GraveyardEmbedBuilder(run), image, "friedhof.png");
    }

    // ---------------------------------------------------------------- Tabellen

    /// <param name="imageTitle">Titel im Bild (z. B. "RUN 4"), <paramref name="imageSubtitle"/> darunter (z. B. "AKTIV").</param>
    public async Task<Card> StatsAsync(string title, IReadOnlyList<PlayerStats> stats, bool global, Tone tone, string fileName,
        string imageTitle, string imageSubtitle)
    {
        var headers = global
            ? new[] { "SPIELER", "RUNS", "LEBT", "FANG", "TODE", "VERP." }
            : new[] { "SPIELER", "LEBT", "FANG", "TODE", "VERP." };
        var tones = global
            ? new[] { Tone.Grey, Tone.Green, Tone.Blue, Tone.Red, Tone.Yellow }
            : new[] { Tone.Green, Tone.Blue, Tone.Red, Tone.Yellow };

        var rows = stats.Select(s => global
                ? new[] { s.Name, $"{s.Runs}", $"{s.Alive}", $"{s.Caught}", $"{s.Deaths}", $"{s.Missed}" }
                : new[] { s.Name, $"{s.Alive}", $"{s.Caught}", $"{s.Deaths}", $"{s.Missed}" })
            .ToList();

        string[]? total = null;
        if (stats.Count > 1)
        {
            string[] sums = [$"{stats.Sum(s => s.Alive)}", $"{stats.Sum(s => s.Caught)}", $"{stats.Sum(s => s.Deaths)}", $"{stats.Sum(s => s.Missed)}"];
            total = (global ? new[] { "Gesamt", "" } : new[] { "Gesamt" }).Concat(sums).ToArray();
        }

        var mood = tone switch { Tone.Gold => SceneMood.Gold, Tone.Grey => SceneMood.Grey, _ => SceneMood.Day };
        var image = await renderer.TableAsync(mood, imageTitle, imageSubtitle, headers, tones, rows, total);
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
        var first = unlocked[0];
        var single = unlocked.Count == 1;
        var title = single ? Up(first.Achievement.Name) : $"{unlocked.Count} ERFOLGE";
        var line1 = single ? $"{Up(first.Player.Name)} erhält einen Erfolg!" : $"{unlocked.Count} neue Erfolge freigeschaltet!";
        var line2 = single ? first.Achievement.Description
            : string.Join(", ", unlocked.Take(3).Select(u => $"{Up(u.Player.Name)}: {u.Achievement.Name}"));
        var mons = unlocked.Where(u => u.Species is not null)
            .Select(u => new BannerMon(u.Species!, u.Player.Name)).Take(3).ToList();

        var image = await renderer.BattleAsync(new BattleBanner(SceneMood.Gold, title, "ERFOLG!", line1, line2,
            mons, Medal: mons.Count == 0));
        return new Card(Formatting.AchievementAnnouncementBuilder(unlocked), image, "erfolg.png");
    }

    public async Task<Card> TitlesAsync(IReadOnlyList<PlayerTitle> titles, IReadOnlyList<(string Name, int Count)> counts, int total)
    {
        var rows = counts.Select(c => new[] { c.Name, $"{c.Count}/{total}" }).ToList();
        var image = rows.Count == 0
            ? null
            : await renderer.TableAsync(SceneMood.Gold, "ERFOLGE", "PRO SPIELER", ["SPIELER", "ERFOLGE"], [Tone.Gold], rows, null);
        return new Card(Formatting.TitlesEmbedBuilder(titles, counts, total, includeTable: image is null), image, "titel.png");
    }

    public async Task<Card> MenuAsync()
    {
        var image = await renderer.BattleAsync(new BattleBanner(SceneMood.Day, "NUZLOCKE", "POKÉMON SCHWARZ",
            "Was möchtest du tun?", "Wähle unten eine Aktion.",
            [new BannerMon("", ""), new BannerMon("", ""), new BannerMon("", "")]));
        return new Card(Formatting.MenuEmbedBuilder(), image, "menu.png");
    }
}
