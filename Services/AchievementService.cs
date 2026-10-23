using Microsoft.EntityFrameworkCore;
using NuzlockeBot.Data;

namespace NuzlockeBot.Services;

/// <summary>Alles, was ein Erfolg über einen Spieler wissen muss.</summary>
public record PlayerFacts(
    Player Player,
    List<Run> Runs,                                                    // Runs, in denen er mitgespielt hat
    List<(Run Run, RouteLink Link, Encounter Encounter)> Encounters,   // eigene Encounter
    List<(Run Run, RouteLink Link)> CausedDeaths,                      // Tode, die er ausgelöst hat
    HashSet<string> CustomRouteKeys,
    List<(int RunId, HashSet<int> StarredBy)> OwnHighlights);          // Highlights seiner Ereignisse mit Sterne-Gebern

/// <summary>Ein Erfolg: einmal freigeschaltet, bleibt er (auch nach Korrekturen).</summary>
public record Achievement(string Key, string Emoji, string Name, string Description, Func<PlayerFacts, bool> Unlocked);

/// <summary>Titel: gehört immer dem aktuell Führenden (ändert sich also).</summary>
/// <param name="HolderIds">Spieler-IDs der Titelträger (für das Profil).</param>
public record PlayerTitle(string Emoji, string Name, string Description, string Holders, string Value, IReadOnlyList<int> HolderIds);

public record UnlockedAchievement(Player Player, Achievement Achievement, string? Species);

public class AchievementService(IDbContextFactory<NuzlockeDbContext> dbFactory)
{
    // ---------------------------------------------------------------- Definitionen

    private static IEnumerable<(Run Run, RouteLink Link, Encounter Encounter)> Caught(PlayerFacts f) =>
        f.Encounters.Where(x => x.Encounter.Status == EncounterStatus.Gefangen);

    private static bool IsFinished(Run r) => r.Status is RunStatus.Gescheitert or RunStatus.Geschafft;

    /// <summary>Starter aller Generationen bis 5 (inkl. Entwicklungen).</summary>
    private static readonly HashSet<int> StarterNumbers =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9,
        152, 153, 154, 155, 156, 157, 158, 159, 160,
        252, 253, 254, 255, 256, 257, 258, 259, 260,
        387, 388, 389, 390, 391, 392, 393, 394, 395,
        495, 496, 497, 498, 499, 500, 501, 502, 503
    ];

    public static readonly IReadOnlyList<Achievement> All =
    [
        // Fangen
        new("erster-fang", "🎣", "Erster Fang", "Dein erstes Pokémon gefangen",
            f => Caught(f).Any()),
        new("hamster", "📦", "Hamster", "25 Pokémon gefangen (über alle Runs)",
            f => Caught(f).Count() >= 25),
        new("pokedex", "📕", "Pokédex-Forscher", "100 Pokémon gefangen (über alle Runs)",
            f => Caught(f).Count() >= 100),
        new("legendaer", "🐉", "Legendär", "Ein legendäres oder mysteriöses Pokémon gefangen",
            f => Caught(f).Any(x => Pokedex.Number(x.Encounter.Species) is { } nr && Pokedex.Legendaries.Contains(nr))),
        new("starter-trio", "🌱", "Starter-Sammler", "Ein Starter-Pokémon einer beliebigen Generation gefangen – außer am Start",
            f => Caught(f).Any(x => Pokedex.Number(x.Encounter.Species) is { } nr && StarterNumbers.Contains(nr)
                                   && RunService.Normalize(x.Link.Route) != "starter")),
        new("entdecker", "🗺️", "Entdecker", "Etwas auf einer selbst angelegten Route gefangen",
            f => Caught(f).Any(x => f.CustomRouteKeys.Contains(x.Link.RouteKey))),
        new("volles-team", "👥", "Volles Team", "6 lebende Soul Links gleichzeitig im laufenden Run",
            f => f.Runs.Where(r => r.Status == RunStatus.Aktiv).Any(r =>
                r.Links.Count(l => l.Status == LinkStatus.Lebendig &&
                                   l.Encounters.Any(e => e.PlayerId == f.Player.Id && e.Status == EncounterStatus.Gefangen)) >= 6)),

        // Entwicklung
        new("nanu", "✨", "Nanu?", "Dein erstes Pokémon entwickelt",
            f => Caught(f).Any(x => x.Encounter.Evolutions.Count > 0)),
        new("endstufe", "🔺", "Endstufe", "Ein Pokémon zweimal entwickelt",
            f => Caught(f).Any(x => x.Encounter.Evolutions.Count >= 2)),
        new("metamorphose", "🦋", "Metamorphose", "10 Entwicklungen (über alle Runs)",
            f => Caught(f).Sum(x => x.Encounter.Evolutions.Count) >= 10),

        // Pech
        new("erster-tod", "⚰️", "Willkommen im Club", "Deinen ersten Soul-Link-Tod ausgelöst",
            f => f.CausedDeaths.Count > 0),
        new("totengraeber", "🪦", "Totengräber", "10 Soul-Link-Tode ausgelöst (über alle Runs)",
            f => f.CausedDeaths.Count >= 10),
        new("blitztod", "⚡", "Blitzkrieg", "Einen Tod auf einer der ersten 3 Routen eines Runs ausgelöst",
            f => f.CausedDeaths.Any(x => x.Run.Links.OrderBy(l => l.Id).Take(3).Any(l => l.Id == x.Link.Id))),
        new("wipe", "💥", "Wipe-Verursacher", "Den letzten Tod eines gescheiterten Runs ausgelöst",
            f => f.Runs.Where(r => r.Status == RunStatus.Gescheitert).Any(r =>
                r.Links.Where(l => l.Status == LinkStatus.Tot && l.DiedAt != null)
                    .OrderByDescending(l => l.DiedAt).FirstOrDefault()?.CausedByPlayerId == f.Player.Id)),
        new("fluchtmagnet", "🏃", "Fluchtmagnet", "3 Encounter in einem einzigen Run verpasst",
            f => f.Encounters.Where(x => x.Encounter.Status == EncounterStatus.Verpasst)
                .GroupBy(x => x.Run.Id).Any(g => g.Count() >= 3)),

        // Durchhalten
        new("unberuehrbar", "🧊", "Eiskalt", "Einen Run mit mind. 8 Routen beendet, ohne selbst einen Tod auszulösen",
            f => f.Runs.Where(r => IsFinished(r) && r.Links.Count >= 8)
                .Any(r => r.Links.All(l => l.CausedByPlayerId != f.Player.Id))),
        new("stammgast", "🔁", "Stammgast", "An 5 Runs teilgenommen",
            f => f.Runs.Count >= 5),
        new("unkaputtbar", "🧱", "Unkaputtbar", "An 10 Runs teilgenommen",
            f => f.Runs.Count >= 10),
        new("champion", "🏆", "Champion", "Einen Run geschafft",
            f => f.Runs.Any(r => r.Status == RunStatus.Geschafft)),

        // Highlights
        new("publikumsliebling", "⭐", "Publikumsliebling", "Ein eigener Moment hat Sterne von allen Mitspielern des Runs",
            f => f.OwnHighlights.Any(h => f.Runs.FirstOrDefault(r => r.Id == h.RunId) is { } run
                && run.Participants.Count >= 2
                && run.Participants.Where(x => x.PlayerId != f.Player.Id).All(x => h.StarredBy.Contains(x.PlayerId)))),
    ];

    public static Achievement? Find(string key) => All.FirstOrDefault(a => a.Key == key);

    // ---------------------------------------------------------------- Laden

    private record GuildData(List<Run> Runs, List<Player> Players, List<PlayerAchievement> Unlocked, HashSet<string> CustomKeys,
        List<Highlight> Highlights);

    private static async Task<GuildData> LoadAsync(NuzlockeDbContext db, ulong guildId)
    {
        var runs = await RunService.RunsWithDetails(db, guildId).ToListAsync();
        var players = await db.Players.Where(p => p.GuildId == (long)guildId).OrderBy(p => p.Id).ToListAsync();
        var playerIds = players.Select(p => p.Id).ToList();
        var unlocked = await db.PlayerAchievements.Where(a => playerIds.Contains(a.PlayerId)).ToListAsync();
        var customKeys = (await db.CustomRoutes.Where(r => r.GuildId == (long)guildId).Select(r => r.NameKey).ToListAsync()).ToHashSet();
        var highlights = await db.Highlights.Where(h => h.GuildId == (long)guildId)
            .Include(h => h.Stars).Include(h => h.Encounter).Include(h => h.RouteLink).ToListAsync();
        return new GuildData(runs, players, unlocked, customKeys, highlights);
    }

    private static PlayerFacts FactsFor(Player p, GuildData data)
    {
        var runs = data.Runs.Where(r => r.Participants.Any(x => x.PlayerId == p.Id)).ToList();
        var encounters = data.Runs
            .SelectMany(r => r.Links.SelectMany(l => l.Encounters
                .Where(e => e.PlayerId == p.Id)
                .Select(e => (Run: r, Link: l, Encounter: e))))
            .ToList();
        var deaths = data.Runs
            .SelectMany(r => r.Links
                .Where(l => l.Status == LinkStatus.Tot && l.CausedByPlayerId == p.Id)
                .Select(l => (Run: r, Link: l)))
            .ToList();
        var own = data.Highlights
            .Where(h => (h.Encounter?.PlayerId ?? h.RouteLink?.CausedByPlayerId) == p.Id)
            .Select(h => (h.RunId, h.Stars.Select(s => s.PlayerId).ToHashSet()))
            .ToList();
        return new PlayerFacts(p, runs, encounters, deaths, data.CustomKeys, own);
    }

    // ---------------------------------------------------------------- Öffentliche API

    /// <summary>
    /// Prüft alle Erfolge aller Spieler und speichert neue. Wird nach jeder Aktion aufgerufen.
    /// Liefert die neu freigeschalteten Erfolge zum Verkünden.
    /// </summary>
    public async Task<List<UnlockedAchievement>> CheckAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var data = await LoadAsync(db, guildId);
        var already = data.Unlocked.Select(a => (a.PlayerId, a.Key)).ToHashSet();
        var result = new List<UnlockedAchievement>();

        foreach (var player in data.Players)
        {
            var facts = FactsFor(player, data);
            foreach (var achievement in All)
            {
                if (already.Contains((player.Id, achievement.Key)) || !achievement.Unlocked(facts)) continue;

                db.PlayerAchievements.Add(new PlayerAchievement { PlayerId = player.Id, Key = achievement.Key });
                result.Add(new UnlockedAchievement(player, achievement, SpeciesFor(achievement, facts)));
            }
        }

        if (result.Count > 0) await db.SaveChangesAsync();
        return result;
    }

    /// <summary>Passendes Pokémon-Bild zum Erfolg (z. B. das gefangene Legendäre).</summary>
    private static string? SpeciesFor(Achievement achievement, PlayerFacts f) => achievement.Key switch
    {
        "legendaer" => Caught(f).LastOrDefault(x => Pokedex.Number(x.Encounter.Species) is { } nr && Pokedex.Legendaries.Contains(nr)).Encounter?.Species,
        "starter-trio" => Caught(f).LastOrDefault(x => Pokedex.Number(x.Encounter.Species) is { } nr && StarterNumbers.Contains(nr)).Encounter?.Species,
        "erster-fang" => Caught(f).FirstOrDefault().Encounter?.Species,
        "nanu" or "metamorphose" => Caught(f).Where(x => x.Encounter.Evolutions.Count > 0)
            .MaxBy(x => x.Encounter.Evolutions.Max(e => e.CreatedAt)).Encounter?.Species,
        "endstufe" => Caught(f).LastOrDefault(x => x.Encounter.Evolutions.Count >= 2).Encounter?.Species,
        _ => null
    };

    /// <summary>Erfolge eines Spielers: freigeschaltete (mit Datum) und noch gesperrte.</summary>
    public async Task<(Player Player, List<(Achievement Achievement, DateTime UnlockedAt)> Unlocked, List<Achievement> Locked)>
        GetPlayerAchievementsAsync(ulong guildId, ulong userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Players.FirstOrDefaultAsync(p => p.GuildId == (long)guildId && p.DiscordUserId == (long)userId)
            ?? throw new NuzlockeException("Dieser Nutzer ist kein Spieler. Mit `/spieler beitreten` eintragen.");

        var rows = await db.PlayerAchievements.Where(a => a.PlayerId == player.Id).ToListAsync();
        var unlocked = rows
            .Select(r => (Achievement: Find(r.Key), r.UnlockedAt))
            .Where(x => x.Achievement is not null)
            .OrderBy(x => x.UnlockedAt)
            .Select(x => (x.Achievement!, x.UnlockedAt))
            .ToList();
        var locked = All.Where(a => rows.All(r => r.Key != a.Key)).ToList();

        return (player, unlocked, locked);
    }

    /// <summary>Aktuelle Titelträger über alle Runs plus Erfolgszahl pro Spieler.</summary>
    public async Task<(List<PlayerTitle> Titles, List<(string Name, int Count)> Counts)> GetTitlesAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var data = await LoadAsync(db, guildId);

        var facts = data.Players
            .Where(p => data.Runs.Any(r => r.Participants.Any(x => x.PlayerId == p.Id)))
            .Select(p => FactsFor(p, data))
            .ToList();

        var counts = facts
            .Select(f => (f.Player.Name, Count: data.Unlocked.Count(a => a.PlayerId == f.Player.Id)))
            .OrderByDescending(x => x.Count)
            .ToList();

        var titles = new List<PlayerTitle>();

        void Add(string emoji, string name, string description, Func<PlayerFacts, double> score,
                 Func<double, string> format, bool lowestWins = false, Func<PlayerFacts, bool>? eligible = null)
        {
            var candidates = facts.Where(f => eligible?.Invoke(f) ?? true).Select(f => (f, Score: score(f))).ToList();
            if (!lowestWins) candidates = candidates.Where(c => c.Score > 0).ToList();
            if (candidates.Count == 0)
            {
                titles.Add(new PlayerTitle(emoji, name, description, "_noch niemand_", "", []));
                return;
            }

            var best = lowestWins ? candidates.Min(c => c.Score) : candidates.Max(c => c.Score);
            var holders = candidates.Where(c => Math.Abs(c.Score - best) < 0.0001).Select(c => c.f.Player).ToList();
            titles.Add(new PlayerTitle(emoji, name, description, string.Join(" & ", holders.Select(h => h.Name)), format(best),
                holders.Select(h => h.Id).ToList()));
        }

        Add("💀", "Pechvogel", "die meisten Soul-Link-Tode ausgelöst",
            f => f.CausedDeaths.Count, v => $"{v} Tode");
        Add("🎣", "Meisterfänger", "die meisten Pokémon gefangen",
            f => Caught(f).Count(), v => $"{v} Fänge");
        Add("🙈", "Grobmotoriker", "die meisten Encounter verpasst",
            f => f.Encounters.Count(x => x.Encounter.Status == EncounterStatus.Verpasst), v => $"{v}× verpasst");
        Add("🛡️", "Schutzengel", "die wenigsten Tode pro Fang (ab 5 Fängen)",
            f => (double)f.CausedDeaths.Count / Caught(f).Count(), v => $"{v:0.00} Tode/Fang",
            lowestWins: true, eligible: f => Caught(f).Count() >= 5);
        Add("🏅", "Trophäenjäger", "die meisten Erfolge",
            f => data.Unlocked.Count(a => a.PlayerId == f.Player.Id), v => $"{v}/{All.Count}");

        return (titles, counts);
    }
}
