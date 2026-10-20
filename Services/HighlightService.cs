using Microsoft.EntityFrameworkCore;
using NuzlockeBot.Data;

namespace NuzlockeBot.Services;

/// <summary>Ein Highlight, fertig aufbereitet zum Anzeigen.</summary>
/// <param name="Title">Kurzbeschreibung, z. B. "Rudis Tod" oder "Zekrom gefangen".</param>
/// <param name="Owner">Spieler, dem das Ereignis gehört (Fänger bzw. Auslöser des Todes).</param>
/// <param name="Phrase">Für Sätze, z. B. "den Tod von Rudi" oder "den Fang von Zekrom".</param>
public record HighlightView(
    int Id, HighlightKind Kind, int RunNumber, string Route, string Owner, int OwnerPlayerId,
    string Species, string? Nickname, string Title, string? Detail, string? Note,
    IReadOnlyList<string> StarredBy, DateTime CreatedAt, string Phrase)
{
    public int Stars => StarredBy.Count;
}

/// <summary>Ergebnis eines Klicks auf ⭐ bzw. /highlight.</summary>
/// <param name="Added">true = Stern vergeben, false = Stern zurückgenommen.</param>
/// <param name="Created">true = das Ereignis ist mit diesem Stern neu zum Highlight geworden.</param>
/// <param name="Highlight">Das Highlight nach der Änderung, null wenn der letzte Stern entfernt wurde.</param>
public record StarResult(HighlightView? Highlight, bool Added, bool Created, string PlayerName);

/// <summary>
/// Highlights: besondere Momente (Fang, verpasster Encounter, Tod), die Spieler mit Sternen markieren.
/// Ein Ereignis ist höchstens einmal ein Highlight; jeder weitere Spieler gibt nur einen Stern dazu.
/// </summary>
public class HighlightService(IDbContextFactory<NuzlockeDbContext> dbFactory)
{
    /// <summary>Zieladresse eines Ereignisses in Buttons und Autocomplete, z. B. "f:12", "v:13", "t:4".</summary>
    public static string Key(HighlightKind kind, int id) => kind switch
    {
        HighlightKind.Fang => $"f:{id}",
        HighlightKind.Verpasst => $"v:{id}",
        _ => $"t:{id}"
    };

    public static (HighlightKind Kind, int Id) ParseKey(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var id))
            throw new NuzlockeException("Dieses Ereignis kenne ich nicht. Wähl es bitte aus der Vorschlagsliste.");
        var kind = parts[0] switch
        {
            "f" => HighlightKind.Fang,
            "v" => HighlightKind.Verpasst,
            "t" => HighlightKind.Tod,
            _ => throw new NuzlockeException("Dieses Ereignis kenne ich nicht. Wähl es bitte aus der Vorschlagsliste.")
        };
        return (kind, id);
    }

    private static IQueryable<Highlight> WithDetails(NuzlockeDbContext db) =>
        db.Highlights
            .Include(h => h.Run)
            .Include(h => h.Encounter).ThenInclude(e => e!.Player)
            .Include(h => h.Encounter).ThenInclude(e => e!.RouteLink)
            .Include(h => h.RouteLink).ThenInclude(l => l!.Encounters).ThenInclude(e => e.Player)
            .Include(h => h.RouteLink).ThenInclude(l => l!.CausedByPlayer)
            .Include(h => h.Stars).ThenInclude(s => s.Player)
            .AsSplitQuery();

    // ---------------------------------------------------------------- Stern vergeben / zurücknehmen

    /// <summary>
    /// Stern für ein Ereignis. Mit <paramref name="toggle"/> (Button) nimmt ein zweiter Klick den Stern zurück;
    /// über /highlight bleibt der Stern und nur die Notiz wird ggf. aktualisiert.
    /// Ohne Sterne verschwindet das Highlight wieder.
    /// </summary>
    public async Task<StarResult> StarAsync(ulong guildId, ulong userId, string key, string? note, bool toggle)
    {
        var (kind, id) = ParseKey(key);
        await using var db = await dbFactory.CreateDbContextAsync();

        var player = await db.Players.FirstOrDefaultAsync(p => p.GuildId == (long)guildId && p.DiscordUserId == (long)userId)
            ?? throw new NuzlockeException("Nur Mitspieler können Highlights vergeben. Trag dich mit `/spieler beitreten` ein.");

        // Ereignis prüfen und zugehörigen Run bestimmen
        int runId;
        if (kind == HighlightKind.Tod)
        {
            var link = await db.RouteLinks.Include(l => l.Run).FirstOrDefaultAsync(l => l.Id == id && l.Run.GuildId == (long)guildId)
                ?? throw new NuzlockeException("Diesen Tod gibt es nicht mehr (vielleicht korrigiert).");
            if (link.Status != LinkStatus.Tot)
                throw new NuzlockeException("Dieser Soul Link ist nicht (mehr) tot.");
            runId = link.RunId;
        }
        else
        {
            var enc = await db.Encounters.Include(e => e.RouteLink).ThenInclude(l => l.Run)
                .FirstOrDefaultAsync(e => e.Id == id && e.RouteLink.Run.GuildId == (long)guildId)
                ?? throw new NuzlockeException("Diesen Encounter gibt es nicht mehr (vielleicht korrigiert).");
            var expected = kind == HighlightKind.Fang ? EncounterStatus.Gefangen : EncounterStatus.Verpasst;
            if (enc.Status != expected)
                throw new NuzlockeException("Dieser Encounter passt nicht zur gewählten Art.");
            runId = enc.RouteLink.RunId;
        }

        var existingQuery = db.Highlights.Include(h => h.Stars).Where(h => h.GuildId == (long)guildId && h.Kind == kind);
        existingQuery = kind == HighlightKind.Tod
            ? existingQuery.Where(h => h.RouteLinkId == id)
            : existingQuery.Where(h => h.EncounterId == id);
        var highlight = await existingQuery.FirstOrDefaultAsync();

        var created = false;
        if (highlight is null)
        {
            highlight = new Highlight
            {
                GuildId = (long)guildId, RunId = runId, Kind = kind,
                EncounterId = kind == HighlightKind.Tod ? null : id,
                RouteLinkId = kind == HighlightKind.Tod ? id : null,
                CreatedByPlayerId = player.Id
            };
            db.Highlights.Add(highlight);
            created = true;
        }

        if (!string.IsNullOrWhiteSpace(note))
            highlight.Note = note.Trim().Length > 120 ? note.Trim()[..120] : note.Trim();

        var existing = highlight.Stars.FirstOrDefault(s => s.PlayerId == player.Id);
        var added = true;
        if (existing is null)
        {
            highlight.Stars.Add(new HighlightStar { PlayerId = player.Id });
        }
        else if (toggle)
        {
            highlight.Stars.Remove(existing);
            db.HighlightStars.Remove(existing);
            added = false;
        }
        else if (string.IsNullOrWhiteSpace(note))
        {
            throw new NuzlockeException("Du hast diesen Moment schon markiert. Mit einer Notiz kannst du ihn ergänzen.");
        }

        if (highlight.Stars.Count == 0)
            db.Highlights.Remove(highlight);   // letzter Stern weg → kein Highlight mehr

        await db.SaveChangesAsync();

        HighlightView? view = null;
        if (highlight.Stars.Count > 0)
        {
            var highlightId = highlight.Id;
            view = ToView(await WithDetails(db).FirstAsync(h => h.Id == highlightId));
        }

        return new StarResult(view, added, created, player.Name);
    }

    // ---------------------------------------------------------------- Abfragen

    /// <summary>Highlights gefiltert und nach Sternen sortiert (bei Gleichstand das neueste zuerst).</summary>
    public async Task<List<HighlightView>> ListAsync(ulong guildId, int? runNumber, ulong? ownerUserId, HighlightKind? kind)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = WithDetails(db).Where(h => h.GuildId == (long)guildId);
        if (runNumber is { } n) query = query.Where(h => h.Run.Number == n);
        if (kind is { } k) query = query.Where(h => h.Kind == k);

        var views = (await query.ToListAsync()).Select(ToView);
        if (ownerUserId is { } uid)
        {
            var owner = await db.Players.FirstOrDefaultAsync(p => p.GuildId == (long)guildId && p.DiscordUserId == (long)uid);
            views = views.Where(v => owner is not null && v.OwnerPlayerId == owner.Id);
        }

        return views.OrderByDescending(v => v.Stars).ThenByDescending(v => v.CreatedAt).ToList();
    }

    /// <summary>Ereignisse für das Autocomplete von /highlight: neueste zuerst, über alle Runs.</summary>
    public async Task<List<(string Key, string Label)>> GetEventOptionsAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var allRuns = await RunService.RunsWithDetails(db, guildId).OrderByDescending(r => r.Number).ToListAsync();

        var options = new List<(string Key, string Label, DateTime At)>();
        foreach (var run in allRuns)
        {
            foreach (var link in run.Links)
            {
                foreach (var e in link.Encounters)
                {
                    var caught = e.Status == EncounterStatus.Gefangen;
                    options.Add((Key(caught ? HighlightKind.Fang : HighlightKind.Verpasst, e.Id),
                        $"{(caught ? "🎯" : "🟡")} #{run.Number} {link.Route} – {(caught ? Formatting.PokemonText(e) : "verpasst")} ({e.Player.Name})",
                        e.CreatedAt));
                }
                if (link.Status == LinkStatus.Tot)
                {
                    var fallen = link.Encounters.FirstOrDefault(e => e.PlayerId == link.CausedByPlayerId && e.Status == EncounterStatus.Gefangen);
                    options.Add((Key(HighlightKind.Tod, link.Id),
                        $"💀 #{run.Number} {link.Route} – Tod{(fallen is null ? "" : $" von {Formatting.PokemonText(fallen)}")}: {link.DeathCause}",
                        link.DiedAt ?? DateTime.MinValue));
                }
            }
        }

        return options.OrderByDescending(o => o.At).Select(o => (o.Key, o.Label)).ToList();
    }

    /// <summary>Die besten Highlights eines Runs (für den Rückblick).</summary>
    public async Task<List<HighlightView>> TopForRunAsync(ulong guildId, int runNumber, int count) =>
        (await ListAsync(guildId, runNumber, null, null)).Take(count).ToList();

    // ---------------------------------------------------------------- Aufbereitung

    private static HighlightView ToView(Highlight h)
    {
        var starredBy = h.Stars.OrderBy(s => s.CreatedAt).Select(s => s.Player.Name).ToList();

        if (h.Kind == HighlightKind.Tod && h.RouteLink is { } link)
        {
            var owner = link.CausedByPlayer;
            var fallen = link.Encounters.FirstOrDefault(e => e.PlayerId == link.CausedByPlayerId && e.Status == EncounterStatus.Gefangen);
            var name = fallen is null ? "Soul Link" : string.IsNullOrWhiteSpace(fallen.Nickname) ? fallen.Species : fallen.Nickname!;
            return new HighlightView(h.Id, h.Kind, h.Run.Number, link.Route, owner?.Name ?? "?", owner?.Id ?? 0,
                fallen?.Species ?? "", fallen?.Nickname, $"{name} stirbt", link.DeathCause, h.Note, starredBy, h.CreatedAt,
                $"den Tod von {name}");
        }

        var e2 = h.Encounter!;
        var caught = h.Kind == HighlightKind.Fang;
        var title = caught
            ? (string.IsNullOrWhiteSpace(e2.Nickname) ? $"{e2.Species} gefangen" : $"{e2.Nickname} ({e2.Species}) gefangen")
            : (e2.Species == "?" ? "Encounter verpasst" : $"{e2.Species} verpasst");
        var mon = string.IsNullOrWhiteSpace(e2.Nickname) ? e2.Species : e2.Nickname!;
        var phrase = caught ? $"den Fang von {mon}"
            : e2.Species == "?" ? $"den verpassten Encounter auf {e2.RouteLink.Route}" : $"das verpasste {e2.Species}";
        return new HighlightView(h.Id, h.Kind, h.Run.Number, e2.RouteLink.Route, e2.Player.Name, e2.PlayerId,
            e2.Species, e2.Nickname, title, null, h.Note, starredBy, h.CreatedAt, phrase);
    }
}
