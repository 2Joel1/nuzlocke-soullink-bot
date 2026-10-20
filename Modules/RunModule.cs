using System.Text;
using Discord;
using Discord.Interactions;
using NuzlockeBot.Data;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

public enum RunErgebnis
{
    [ChoiceDisplay("💀 Gescheitert (Wipe / Regelbruch)")] Gescheitert,
    [ChoiceDisplay("🏆 Geschafft")] Geschafft,
    [ChoiceDisplay("⏹️ Abgebrochen")] Abgebrochen
}

[Group("run", "Runs starten, beenden und auflisten")]
public class RunModule(RunService runs, AchievementService achievements, CardFactory cards) : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("start", "Neuen Run mit allen eingetragenen Spielern starten")]
    public Task StartAsync() => SafeAsync(DoStartRunAsync);

    [SlashCommand("ende", "Aktuellen Run beenden")]
    public Task EndeAsync(
        [Summary("ergebnis", "Wie ist der Run ausgegangen?")] RunErgebnis ergebnis,
        [Summary("grund", "z. B. \"Wipe bei Aloe\"")] string? grund = null) =>
        SafeAsync(() => DoEndRunAsync(ToStatus(ergebnis), grund));

    public static RunStatus ToStatus(RunErgebnis ergebnis) => ergebnis switch
    {
        RunErgebnis.Geschafft => RunStatus.Geschafft,
        RunErgebnis.Abgebrochen => RunStatus.Abgebrochen,
        _ => RunStatus.Gescheitert
    };

    [SlashCommand("rueckblick", "Rückblick eines Runs: Dauer, Zeitstrahl, Spieler, Friedhof")]
    public Task RueckblickAsync(
        [Summary("nummer", "Run-Nummer (leer = aktueller bzw. letzter Run)"), MinValue(1)] int nummer = 0) =>
        SafeAsync(() => DoShowRecapAsync(nummer > 0 ? nummer : null));

    [SlashCommand("liste", "Alle bisherigen Runs und Statistik")]
    public Task ListeAsync() =>
        SafeAsync(async () =>
        {
            var all = await Runs.GetAllRunsAsync(GuildId);
            if (all.Count == 0)
                throw new NuzlockeException("Es gibt noch keinen Run.");

            var sb = new StringBuilder();
            foreach (var r in all)
            {
                var dead = r.Links.Count(l => l.Status == LinkStatus.Tot);
                sb.Append($"**#{r.Number}** {Formatting.RunStatusText(r.Status)} · {LevelCaps.Reached(r)} · {r.Links.Count} Routen, {dead} Tote");
                if (!string.IsNullOrWhiteSpace(r.EndReason)) sb.Append($" · _{r.EndReason}_");
                sb.AppendLine();
            }

            var deadLinks = all.SelectMany(r => r.Links).Where(l => l.Status == LinkStatus.Tot).ToList();
            var topCauses = deadLinks
                .Where(l => !string.IsNullOrWhiteSpace(l.DeathCause))
                .GroupBy(l => l.DeathCause!.Trim().ToLowerInvariant())
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{g.First().DeathCause} ({g.Count()}×)");
            var topPlayers = deadLinks
                .Where(l => l.CausedByPlayer is not null)
                .GroupBy(l => l.CausedByPlayer!.Name)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{g.Key} ({g.Count()}×)");

            var finished = all.Count(r => r.Status != RunStatus.Aktiv);
            var embed = new EmbedBuilder()
                .WithTitle("📜 Alle Runs")
                .WithDescription(Formatting.Truncate(sb.ToString(), 3500))
                .AddField("Verbrauchte Runs", finished.ToString(), inline: true)
                .AddField("Soul Links gestorben", deadLinks.Count.ToString(), inline: true)
                .AddField("Häufigste Todesursache", OrDash(topCauses))
                .AddField("Meiste Tode ausgelöst", OrDash(topPlayers))
                .AddField("Gefährlichster Gegner", OrDash(all
                    .Where(r => r.Status == RunStatus.Gescheitert && LevelCaps.Next(r.Progress) is not null)
                    .GroupBy(r => LevelCaps.Next(r.Progress)!.Trainer)
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .Select(g => $"{g.Key} ({g.Count()}× gescheitert)")))
                .WithColor(Formatting.Grey)
                .Build();

            await RespondAsync(embed: embed);
        });

    private static string OrDash(IEnumerable<string> items)
    {
        var text = string.Join(", ", items);
        return string.IsNullOrEmpty(text) ? "–" : text;
    }
}
