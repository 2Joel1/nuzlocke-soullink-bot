using Discord.Interactions;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

[Group("korrektur", "Vertipper im aktuellen Run korrigieren")]
public class KorrekturModule(RunService runs, AchievementService achievements, CardFactory cards) : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("fang-loeschen", "Deinen Encounter auf einer Route löschen (z. B. falsch eingetragen)")]
    public Task FangLoeschenAsync(
        [Summary("route", "Route"), Autocomplete(typeof(OwnEncounterRouteAutocomplete))] string route) =>
        SafeAsync(async () =>
        {
            var (_, link) = await Runs.RemoveEncounterAsync(GuildId, Context.User.Id, route);
            await RespondAsync($"🗑️ Dein Encounter auf **{link.Route}** wurde gelöscht. Trag ihn mit `/fang` neu ein.");
        });

    [SlashCommand("tod-zuruecknehmen", "Einen versehentlich eingetragenen Tod rückgängig machen")]
    public Task TodZuruecknehmenAsync(
        [Summary("route", "Route"), Autocomplete(typeof(DeadRouteAutocomplete))] string route) =>
        SafeAsync(async () =>
        {
            var (run, link) = await Runs.ReviveAsync(GuildId, route);
            await RespondAsync(embed: Formatting.LinkEmbed(
                $"✨ {link.Route}: Tod zurückgenommen", run, link, Formatting.Green,
                $"Korrigiert von {DisplayName}."));
        });

    [SlashCommand("entwicklung-zuruecknehmen", "Die letzte Entwicklung deines Pokémon rückgängig machen")]
    public Task EntwicklungZuruecknehmenAsync(
        [Summary("route", "Route"), Autocomplete(typeof(OwnEvolvedRouteAutocomplete))] string route) =>
        SafeAsync(async () =>
        {
            var r = await Runs.UndoEvolutionAsync(GuildId, Context.User.Id, route);
            await RespondAsync($"↩️ {r.Link.Route}: Entwicklung zurückgenommen – **{r.From}** ist wieder **{r.To}**.");
        });
}
