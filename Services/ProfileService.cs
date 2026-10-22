using Microsoft.EntityFrameworkCore;
using NuzlockeBot.Data;

namespace NuzlockeBot.Services;

/// <summary>Ein Run aus Sicht eines Spielers (Zeile im Profil).</summary>
public record ProfileRun(int Number, RunStatus Status, int Progress, int Caught, int Deaths, int Missed);

/// <summary>Das eigene Pokémon, das am längsten im Team war (bis zum Tod, Run-Ende oder bis jetzt).</summary>
public record Companion(string Species, string? Nickname, int RunNumber, TimeSpan Duration, bool StillAlive);

/// <summary>Alles, was im Trainerpass (/profil) steht.</summary>
/// <param name="Favorite">Gewähltes Lieblings-Pokémon, sonst das meistgefangene (null, wenn noch nichts gefangen).</param>
/// <param name="Survived">Gefangene Pokémon, deren Soul Link nicht gestorben ist.</param>
/// <param name="NemesisCause">Häufigste Todesursache der selbst ausgelösten Tode.</param>
public record PlayerProfile(
    Player Player,
    string? Favorite, bool FavoriteChosen,
    DateTime Since,
    int Runs, int Wins, int BestProgress,
    int Caught, int Deaths, int Missed, int Survived,
    IReadOnlyList<PlayerTitle> Titles,
    string? NemesisCause, int NemesisCount,
    Companion? Companion,
    HighlightView? TopHighlight, int StarsReceived,
    IReadOnlyList<(Achievement Achievement, DateTime UnlockedAt)> Achievements,
    IReadOnlyList<ProfileRun> History);

/// <summary>Spielerprofile: Statistiken über alle Runs, Titel, Erfolge, Highlights und Lieblings-Pokémon.</summary>
public class ProfileService(IDbContextFactory<NuzlockeDbContext> dbFactory, AchievementService achievements, HighlightService highlights)
{
    public async Task<PlayerProfile> GetProfileAsync(ulong guildId, ulong userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Players.FirstOrDefaultAsync(p => p.GuildId == (long)guildId && p.DiscordUserId == (long)userId)
            ?? throw new NuzlockeException("Dieser Nutzer hat noch kein Profil – das gibt es ab dem ersten Run.");

        var runs = await RunService.RunsWithDetails(db, guildId)
            .Where(r => r.Participants.Any(p => p.PlayerId == player.Id))
            .OrderBy(r => r.Number)
            .ToListAsync();
        if (runs.Count == 0)
            throw new NuzlockeException($"**{player.Name}** hat noch an keinem Run teilgenommen – das Profil gibt es ab dem ersten Run.");

        var own = runs
            .SelectMany(r => r.Links.SelectMany(l => l.Encounters
                .Where(e => e.PlayerId == player.Id)
                .Select(e => (Run: r, Link: l, Encounter: e))))
            .ToList();
        var caught = own.Where(x => x.Encounter.Status == EncounterStatus.Gefangen).ToList();
        var caused = runs.SelectMany(r => r.Links).Where(l => l.Status == LinkStatus.Tot && l.CausedByPlayerId == player.Id).ToList();

        // Lieblings-Pokémon: selbst gewählt, sonst das meistgefangene (bei Gleichstand das zuletzt gefangene)
        var favorite = player.FavoriteSpecies ?? caught
            .GroupBy(x => x.Encounter.Species)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(x => x.Encounter.CreatedAt))
            .FirstOrDefault()?.Key;

        // Erzfeind: häufigste Ursache der selbst ausgelösten Tode
        var nemesis = caused
            .Where(l => !string.IsNullOrWhiteSpace(l.DeathCause))
            .GroupBy(l => RunService.Normalize(l.DeathCause!))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(l => l.DiedAt))
            .FirstOrDefault();

        // Treuester Begleiter: am längsten im Team (gebrochene Links zählen nicht – die sitzen in der Box)
        var now = DateTime.UtcNow;
        var companion = caught
            .Where(x => x.Link.Status != LinkStatus.Gebrochen)
            .Select(x => new Companion(x.Encounter.Species, x.Encounter.Nickname, x.Run.Number,
                (x.Link.DiedAt ?? x.Run.EndedAt ?? now) - x.Encounter.CreatedAt,
                x.Link.Status == LinkStatus.Lebendig && x.Run.Status == RunStatus.Aktiv))
            .Where(c => c.Duration > TimeSpan.Zero)
            .OrderByDescending(c => c.Duration)
            .FirstOrDefault();

        var (allTitles, _) = await achievements.GetTitlesAsync(guildId);
        var (_, unlocked, _) = await achievements.GetPlayerAchievementsAsync(guildId, userId);
        var ownHighlights = await highlights.ListAsync(guildId, null, userId, null);

        var history = runs
            .OrderByDescending(r => r.Number)
            .Select(r => new ProfileRun(r.Number, r.Status, r.Progress,
                r.Links.Sum(l => l.Encounters.Count(e => e.PlayerId == player.Id && e.Status == EncounterStatus.Gefangen)),
                r.Links.Count(l => l.Status == LinkStatus.Tot && l.CausedByPlayerId == player.Id),
                r.Links.Sum(l => l.Encounters.Count(e => e.PlayerId == player.Id && e.Status == EncounterStatus.Verpasst))))
            .ToList();

        return new PlayerProfile(
            Player: player,
            Favorite: favorite,
            FavoriteChosen: player.FavoriteSpecies is not null,
            Since: runs.Min(r => r.StartedAt),
            Runs: runs.Count,
            Wins: runs.Count(r => r.Status == RunStatus.Geschafft),
            BestProgress: runs.Max(r => r.Progress),
            Caught: caught.Count,
            Deaths: caused.Count,
            Missed: own.Count(x => x.Encounter.Status == EncounterStatus.Verpasst),
            Survived: caught.Count(x => x.Link.Status != LinkStatus.Tot),
            Titles: allTitles.Where(t => t.HolderIds.Contains(player.Id)).ToList(),
            NemesisCause: nemesis?.First().DeathCause!.Trim(),
            NemesisCount: nemesis?.Count() ?? 0,
            Companion: companion,
            TopHighlight: ownHighlights.FirstOrDefault(),
            StarsReceived: ownHighlights.Sum(h => h.Stars),
            Achievements: unlocked,
            History: history);
    }

    /// <summary>Lieblings-Pokémon setzen. Ohne Angabe wird wieder automatisch das meistgefangene gezeigt.</summary>
    public async Task<string?> SetFavoriteAsync(ulong guildId, ulong userId, string? pokemon)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Players.FirstOrDefaultAsync(p => p.GuildId == (long)guildId && p.DiscordUserId == (long)userId)
            ?? throw new NuzlockeException("Du bist noch kein Spieler. Mit `/spieler beitreten` eintragen.");

        player.FavoriteSpecies = string.IsNullOrWhiteSpace(pokemon) ? null : RunService.RequirePokemon(pokemon);
        await db.SaveChangesAsync();
        return player.FavoriteSpecies;
    }
}
