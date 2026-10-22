using Microsoft.EntityFrameworkCore;
using NuzlockeBot.Data;

namespace NuzlockeBot.Services;

/// <summary>Regelverstoß oder ungültige Eingabe – wird dem Nutzer direkt angezeigt.</summary>
public class NuzlockeException(string message) : Exception(message);

/// <summary>Ergebnis eines Fangs/Verpassens: der Run (für Teilnehmer) und der betroffene Link.</summary>
public record LinkResult(Run Run, RouteLink Link);

/// <summary>Eine Zeile der Übersichtstabelle.</summary>
/// <param name="Best">Weitester Fortschritt (geschaffte Pflichtkämpfe) in allen Runs des Spielers.</param>
public record PlayerStats(string Name, int Runs, int Alive, int Caught, int Deaths, int Missed, int Best = 0);

public class RunService(IDbContextFactory<NuzlockeDbContext> dbFactory)
{
    /// <summary>
    /// Wird nach jeder Änderung an einem Run ausgelöst (Fang, Tod, Start, Ende, Korrektur …).
    /// Die Live-Statusnachricht hört darauf und aktualisiert sich (Observer-Prinzip).
    /// </summary>
    public event Action<ulong>? RunChanged;

    private void OnRunChanged(ulong guildId) => RunChanged?.Invoke(guildId);

    // ---------------------------------------------------------------- Hilfen

    public static string Normalize(string route) =>
        string.Join(' ', route.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public static IQueryable<Run> RunsWithDetails(NuzlockeDbContext db, ulong guildId) =>
        db.Runs
            .Where(r => r.GuildId == (long)guildId)
            .Include(r => r.Participants).ThenInclude(p => p.Player)
            .Include(r => r.Links).ThenInclude(l => l.Encounters).ThenInclude(e => e.Player)
            .Include(r => r.Links).ThenInclude(l => l.CausedByPlayer)
            .Include(r => r.ArenaClears)
            .AsSplitQuery();

    private static async Task<Run> RequireActiveRunAsync(NuzlockeDbContext db, ulong guildId) =>
        await RunsWithDetails(db, guildId).FirstOrDefaultAsync(r => r.Status == RunStatus.Aktiv)
        ?? throw new NuzlockeException("Es läuft gerade kein Run. Starte einen mit `/run start`.");

    private static Player RequireParticipant(Run run, ulong userId) =>
        run.Participants.Select(p => p.Player).FirstOrDefault(p => p.DiscordUserId == (long)userId)
        ?? throw new NuzlockeException($"Du bist in Run #{run.Number} nicht eingetragen. " +
                                       "Mit `/spieler beitreten` bist du ab dem nächsten Run dabei.");

    private static RouteLink? FindLink(Run run, string route)
    {
        var key = Normalize(route);
        return run.Links.FirstOrDefault(l => l.RouteKey == key);
    }

    private static RouteLink RequireLink(Run run, string route) =>
        FindLink(run, route) ?? throw new NuzlockeException($"Auf **{route}** gibt es in Run #{run.Number} noch keinen Encounter.");

    /// <summary>Status neu berechnen, wenn der Link nicht tot ist (z. B. nach Korrekturen).</summary>
    private static void RecalculateStatus(RouteLink link)
    {
        if (link.Status == LinkStatus.Tot) return;
        link.Status = link.Encounters.Any(e => e.Status == EncounterStatus.Verpasst)
            ? LinkStatus.Gebrochen
            : LinkStatus.Lebendig;
    }

    // ---------------------------------------------------------------- Spieler

    public async Task<Player> JoinAsync(ulong guildId, ulong userId, string name)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Players.FirstOrDefaultAsync(p => p.GuildId == (long)guildId && p.DiscordUserId == (long)userId);
        if (player is null)
        {
            player = new Player { GuildId = (long)guildId, DiscordUserId = (long)userId };
            db.Players.Add(player);
        }

        player.Name = name;
        player.Active = true;
        await db.SaveChangesAsync();
        return player;
    }

    public async Task LeaveAsync(ulong guildId, ulong userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Players.FirstOrDefaultAsync(p => p.GuildId == (long)guildId && p.DiscordUserId == (long)userId && p.Active)
            ?? throw new NuzlockeException("Du bist gar nicht als Spieler eingetragen.");
        player.Active = false;
        await db.SaveChangesAsync();
    }

    public async Task<List<Player>> GetRosterAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Players
            .Where(p => p.GuildId == (long)guildId && p.Active)
            .OrderBy(p => p.Id)
            .ToListAsync();
    }

    // ---------------------------------------------------------------- Runs

    public async Task<(Run Run, int PreviousRuns)> StartRunAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        if (await db.Runs.AnyAsync(r => r.GuildId == (long)guildId && r.Status == RunStatus.Aktiv))
            throw new NuzlockeException("Es läuft bereits ein Run. Beende ihn zuerst mit `/run ende`.");

        var roster = await db.Players.Where(p => p.GuildId == (long)guildId && p.Active).OrderBy(p => p.Id).ToListAsync();
        if (roster.Count == 0)
            throw new NuzlockeException("Noch keine Spieler eingetragen. Jeder Mitspieler muss einmal `/spieler beitreten` ausführen.");

        var previous = await db.Runs.CountAsync(r => r.GuildId == (long)guildId);
        var run = new Run
        {
            GuildId = (long)guildId,
            Number = previous + 1,
            Participants = roster.Select(p => new RunParticipant { Player = p }).ToList()
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync();
        OnRunChanged(guildId);
        return (run, previous);
    }

    // ---------------------------------------------------------------- Arenen / Levelcap

    /// <summary>Nächsten Pflichtkampf als geschafft markieren. Liefert den Run und den besiegten Gegner.</summary>
    public async Task<(Run Run, BossFight Beaten)> AdvanceProgressAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RequireActiveRunAsync(db, guildId);
        var fight = LevelCaps.Next(run.Progress)
            ?? throw new NuzlockeException("Alle Arenen, die Top Vier, N und G-Cis sind schon geschafft. Zeit für `/run ende`!");

        run.Progress++;
        if (run.ArenaClears.All(a => a.Nr != fight.Nr))
            run.ArenaClears.Add(new ArenaClear { Nr = fight.Nr });   // Zeitpunkt für den Zeitstrahl im Rückblick
        await db.SaveChangesAsync();
        OnRunChanged(guildId);
        return (run, fight);
    }

    /// <summary>Fortschritt direkt setzen (Korrektur). 0 = noch keine Arena, 8 = alle Orden, 11 = Story geschafft.</summary>
    public async Task<Run> SetProgressAsync(ulong guildId, int progress)
    {
        if (progress < 0 || progress > LevelCaps.Schwarz.Count)
            throw new NuzlockeException($"Der Fortschritt muss zwischen 0 und {LevelCaps.Schwarz.Count} liegen.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RequireActiveRunAsync(db, guildId);
        run.Progress = progress;
        // Zurückgesetzte Kämpfe verlieren ihren Zeitpunkt; nachgetragene bekommen keinen (Zeitpunkt unbekannt)
        foreach (var clear in run.ArenaClears.Where(a => a.Nr > progress).ToList())
        {
            run.ArenaClears.Remove(clear);
            db.ArenaClears.Remove(clear);
        }
        await db.SaveChangesAsync();
        OnRunChanged(guildId);
        return run;
    }

    public async Task<Run> EndRunAsync(ulong guildId, RunStatus result, string? reason)
    {
        if (result == RunStatus.Aktiv)
            throw new NuzlockeException("Ein Run kann nicht mit dem Ergebnis \"Aktiv\" beendet werden.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RequireActiveRunAsync(db, guildId);

        run.Status = result;
        run.EndedAt = DateTime.UtcNow;
        run.EndReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await db.SaveChangesAsync();
        OnRunChanged(guildId);
        return run;
    }

    /// <summary>Run mit Nummer laden; ohne Nummer den aktiven bzw. zuletzt gespielten Run.</summary>
    public async Task<Run> GetRunAsync(ulong guildId, int? number)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = RunsWithDetails(db, guildId);

        var run = number is { } n
            ? await query.FirstOrDefaultAsync(r => r.Number == n)
            : await query.OrderByDescending(r => r.Status == RunStatus.Aktiv).ThenByDescending(r => r.Number).FirstOrDefaultAsync();

        return run ?? throw new NuzlockeException(number is null
            ? "Es gibt noch keinen Run. Starte einen mit `/run start`."
            : $"Run #{number} gibt es nicht.");
    }

    public async Task<List<Run>> GetAllRunsAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await RunsWithDetails(db, guildId).OrderBy(r => r.Number).ToListAsync();
    }

    /// <summary>Prüft vorab, ob ein Run läuft und der Nutzer mitspielt (für das Bedienfeld).</summary>
    public async Task EnsureParticipantAsync(ulong guildId, ulong userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RequireActiveRunAsync(db, guildId);
        RequireParticipant(run, userId);
    }

    // ---------------------------------------------------------------- Encounter

    public async Task<LinkResult> CatchAsync(ulong guildId, ulong userId, string route, string species, string? nickname)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RequireActiveRunAsync(db, guildId);
        var player = RequireParticipant(run, userId);
        var pokemon = RequirePokemon(species);
        var link = await GetOrCreateLinkAsync(db, run, route);

        if (link.Status == LinkStatus.Tot)
            throw new NuzlockeException($"Der Soul Link auf **{link.Route}** ist bereits tot – hier darf nichts mehr gefangen werden.");
        if (link.Encounters.Any(e => e.PlayerId == player.Id))
            throw new NuzlockeException($"Du hast auf **{link.Route}** schon deinen Encounter. Falls das ein Fehler war: `/korrektur fang-loeschen`.");

        link.Encounters.Add(new Encounter
        {
            Player = player,
            Species = pokemon,
            Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim(),
            Status = EncounterStatus.Gefangen
        });

        await db.SaveChangesAsync();
        OnRunChanged(guildId);
        return new LinkResult(run, link);
    }

    public async Task<LinkResult> MissAsync(ulong guildId, ulong userId, string route, string? species)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RequireActiveRunAsync(db, guildId);
        var player = RequireParticipant(run, userId);
        var pokemon = string.IsNullOrWhiteSpace(species) ? "?" : RequirePokemon(species);
        var link = await GetOrCreateLinkAsync(db, run, route);

        if (link.Encounters.Any(e => e.PlayerId == player.Id))
            throw new NuzlockeException($"Du hast auf **{link.Route}** schon einen Encounter eingetragen.");

        link.Encounters.Add(new Encounter
        {
            Player = player,
            Species = pokemon,
            Status = EncounterStatus.Verpasst
        });
        RecalculateStatus(link);

        await db.SaveChangesAsync();
        OnRunChanged(guildId);
        return new LinkResult(run, link);
    }

    public async Task<LinkResult> DeathAsync(ulong guildId, ulong userId, string route, string cause)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RequireActiveRunAsync(db, guildId);
        var player = RequireParticipant(run, userId);
        var link = RequireLink(run, route);

        if (link.Status == LinkStatus.Tot)
            throw new NuzlockeException($"Der Soul Link auf **{link.Route}** ist schon tot.");
        if (!link.Encounters.Any(e => e.PlayerId == player.Id && e.Status == EncounterStatus.Gefangen))
            throw new NuzlockeException($"Du hast auf **{link.Route}** kein gefangenes Pokémon.");

        link.Status = LinkStatus.Tot;
        link.DiedAt = DateTime.UtcNow;
        link.DeathCause = cause.Trim();
        link.CausedByPlayer = player;

        await db.SaveChangesAsync();
        OnRunChanged(guildId);
        return new LinkResult(run, link);
    }

    public static string RequirePokemon(string input) =>
        Pokedex.Find(input)
        ?? throw new NuzlockeException($"**{input.Trim()}** gibt es in Pokémon Schwarz nicht. " +
                                       $"Meintest du: {string.Join(", ", Pokedex.Suggest(input))}?");

    /// <summary>
    /// Bestehenden Link der Route holen oder neu anlegen. Neue Links nur für bekannte Routen
    /// (Standardliste oder mit /route erstellen angelegt) – verhindert Vertipper wie "Rote 3".
    /// </summary>
    private static async Task<RouteLink> GetOrCreateLinkAsync(NuzlockeDbContext db, Run run, string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            throw new NuzlockeException("Bitte gib eine Route an.");

        var existing = FindLink(run, route);
        if (existing is not null) return existing;

        var key = Normalize(route);
        var name = Routes.Schwarz.FirstOrDefault(r => Normalize(r) == key)
                   ?? (await db.CustomRoutes.FirstOrDefaultAsync(r => r.GuildId == run.GuildId && r.NameKey == key))?.Name
                   ?? throw new NuzlockeException($"Die Route **{route.Trim()}** kenne ich nicht. " +
                                                  "Wähl aus der Vorschlagsliste oder leg sie mit `/route erstellen` an.");

        var link = new RouteLink { Route = name, RouteKey = key };
        run.Links.Add(link);
        return link;
    }

    // ---------------------------------------------------------------- Eigene Routen

    public async Task<CustomRoute> CreateCustomRouteAsync(ulong guildId, string name, string createdBy)
    {
        name = string.Join(' ', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is 0 or > 60)
            throw new NuzlockeException("Der Routenname muss zwischen 1 und 60 Zeichen lang sein.");

        var key = Normalize(name);
        if (Routes.Schwarz.Any(r => Normalize(r) == key))
            throw new NuzlockeException($"**{name}** gibt es schon in der Standardliste.");

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.CustomRoutes.AnyAsync(r => r.GuildId == (long)guildId && r.NameKey == key))
            throw new NuzlockeException($"Die Route **{name}** gibt es schon.");

        var route = new CustomRoute { GuildId = (long)guildId, Name = name, NameKey = key, CreatedBy = createdBy };
        db.CustomRoutes.Add(route);
        await db.SaveChangesAsync();
        return route;
    }

    public async Task<string> DeleteCustomRouteAsync(ulong guildId, string name)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var key = Normalize(name);
        var route = await db.CustomRoutes.FirstOrDefaultAsync(r => r.GuildId == (long)guildId && r.NameKey == key)
            ?? throw new NuzlockeException($"Eine eigene Route **{name.Trim()}** gibt es nicht.");

        // Bereits genutzte Routen bleiben in alten Runs erhalten (RouteLink speichert den Namen selbst)
        db.CustomRoutes.Remove(route);
        await db.SaveChangesAsync();
        return route.Name;
    }

    public async Task<List<CustomRoute>> GetCustomRoutesAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.CustomRoutes.Where(r => r.GuildId == (long)guildId).OrderBy(r => r.Id).ToListAsync();
    }

    // ---------------------------------------------------------------- Korrekturen

    public async Task<LinkResult> RemoveEncounterAsync(ulong guildId, ulong userId, string route)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RequireActiveRunAsync(db, guildId);
        var player = RequireParticipant(run, userId);
        var link = RequireLink(run, route);

        var encounter = link.Encounters.FirstOrDefault(e => e.PlayerId == player.Id)
            ?? throw new NuzlockeException($"Du hast auf **{link.Route}** keinen Encounter.");

        link.Encounters.Remove(encounter);
        db.Encounters.Remove(encounter);

        if (link.Encounters.Count == 0)
        {
            run.Links.Remove(link);
            db.RouteLinks.Remove(link);
        }
        else
        {
            RecalculateStatus(link);
        }

        await db.SaveChangesAsync();
        OnRunChanged(guildId);
        return new LinkResult(run, link);
    }

    public async Task<LinkResult> ReviveAsync(ulong guildId, string route)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RequireActiveRunAsync(db, guildId);
        var link = RequireLink(run, route);

        if (link.Status != LinkStatus.Tot)
            throw new NuzlockeException($"Der Soul Link auf **{link.Route}** ist gar nicht tot.");

        link.Status = LinkStatus.Lebendig;
        link.DiedAt = null;
        link.DeathCause = null;
        link.CausedByPlayerId = null;
        link.CausedByPlayer = null;
        RecalculateStatus(link);

        // Der Tod ist zurückgenommen – ein Tod-Highlight dazu ergibt keinen Sinn mehr
        await db.Highlights.Where(h => h.RouteLinkId == link.Id && h.Kind == HighlightKind.Tod).ExecuteDeleteAsync();
        await db.SaveChangesAsync();
        OnRunChanged(guildId);
        return new LinkResult(run, link);
    }

    // ---------------------------------------------------------------- Statistik

    /// <summary>
    /// Zählt pro Spieler über die übergebenen Runs. "Lebend" = aktuell nutzbare Pokémon
    /// im Run <paramref name="aliveIn"/> (bei der globalen Tabelle der laufende Run).
    /// </summary>
    public static List<PlayerStats> ComputeStats(IReadOnlyCollection<Run> runs, Run? aliveIn)
    {
        var players = runs
            .SelectMany(r => r.Participants.Select(p => p.Player))
            .DistinctBy(p => p.Id)
            .OrderBy(p => p.Id);

        var encounters = runs.SelectMany(r => r.Links).SelectMany(l => l.Encounters).ToList();
        var deadLinks = runs.SelectMany(r => r.Links).Where(l => l.Status == LinkStatus.Tot).ToList();

        return players.Select(p => new PlayerStats(
                Name: p.Name,
                Runs: runs.Count(r => r.Participants.Any(x => x.PlayerId == p.Id)),
                Alive: aliveIn?.Links
                    .Where(l => l.Status == LinkStatus.Lebendig)
                    .Count(l => l.Encounters.Any(e => e.PlayerId == p.Id && e.Status == EncounterStatus.Gefangen)) ?? 0,
                Caught: encounters.Count(e => e.PlayerId == p.Id && e.Status == EncounterStatus.Gefangen),
                Deaths: deadLinks.Count(l => l.CausedByPlayerId == p.Id),
                Missed: encounters.Count(e => e.PlayerId == p.Id && e.Status == EncounterStatus.Verpasst),
                Best: runs.Where(r => r.Participants.Any(x => x.PlayerId == p.Id)).Select(r => r.Progress).DefaultIfEmpty(0).Max()))
            .ToList();
    }

    // ---------------------------------------------------------------- Autocomplete

    /// <summary>
    /// Routen für /fang und /verpasst: zuerst Routen, auf denen Partner schon etwas haben,
    /// dann eigene Routen, dann die Standardliste. Routen, auf denen der Spieler schon fertig ist, fallen weg.
    /// </summary>
    public async Task<List<string>> GetOpenRouteSuggestionsAsync(ulong guildId, ulong userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var custom = await db.CustomRoutes.Where(r => r.GuildId == (long)guildId).OrderBy(r => r.Id).Select(r => r.Name).ToListAsync();
        var run = await RunsWithDetails(db, guildId).FirstOrDefaultAsync(r => r.Status == RunStatus.Aktiv);
        if (run is null) return [.. custom, .. Routes.Schwarz];

        var blocked = run.Links
            .Where(l => l.Status == LinkStatus.Tot || l.Encounters.Any(e => e.Player.DiscordUserId == (long)userId))
            .Select(l => l.RouteKey)
            .ToHashSet();

        var partnerRoutes = run.Links
            .Where(l => !blocked.Contains(l.RouteKey))
            .OrderBy(l => l.Id)
            .Select(l => l.Route);

        var knownKeys = run.Links.Select(l => l.RouteKey).ToHashSet();
        var others = custom.Concat(Routes.Schwarz).Where(r => !knownKeys.Contains(Normalize(r)));

        return [.. partnerRoutes, .. others];
    }

    /// <summary>Lebende/gebrochene Links, auf denen der Spieler ein gefangenes Pokémon hat (für /tod).</summary>
    public async Task<List<(string Route, string Label)>> GetOwnLivingRoutesAsync(ulong guildId, ulong userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RunsWithDetails(db, guildId).FirstOrDefaultAsync(r => r.Status == RunStatus.Aktiv);
        if (run is null) return [];

        return run.Links
            .Where(l => l.Status != LinkStatus.Tot)
            .Select(l => (Link: l, Own: l.Encounters.FirstOrDefault(e =>
                e.Player.DiscordUserId == (long)userId && e.Status == EncounterStatus.Gefangen)))
            .Where(x => x.Own is not null)
            .OrderBy(x => x.Link.Id)
            .Select(x => (x.Link.Route, $"{x.Link.Route} – {Formatting.PokemonText(x.Own!)}"))
            .ToList();
    }

    /// <summary>Alle Routen des aktiven Runs, gefiltert nach Status (null = alle).</summary>
    public async Task<List<string>> GetRunRoutesAsync(ulong guildId, LinkStatus? status, ulong? withEncounterOfUser = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await RunsWithDetails(db, guildId).FirstOrDefaultAsync(r => r.Status == RunStatus.Aktiv);
        if (run is null) return [];

        return run.Links
            .Where(l => status is null || l.Status == status)
            .Where(l => withEncounterOfUser is null || l.Encounters.Any(e => e.Player.DiscordUserId == (long)withEncounterOfUser))
            .OrderBy(l => l.Id)
            .Select(l => l.Route)
            .ToList();
    }
}
