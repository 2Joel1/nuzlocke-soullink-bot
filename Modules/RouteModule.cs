using Discord.Interactions;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

[Group("route", "Eigene Routen/Orte verwalten (z. B. Static-Pokémon)")]
public class RouteModule(RunService runs, AchievementService achievements, CardFactory cards) : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("erstellen", "Eigene Route anlegen – sie erscheint bei allen in der Vorschlagsliste")]
    public Task ErstellenAsync(
        [Summary("name", "z. B. \"Static Zekrom\" oder \"Geschenk Ei\"")] string name) =>
        SafeAsync(() => DoCreateRouteAsync(name));

    [SlashCommand("loeschen", "Eigene Route entfernen (bereits eingetragene Fänge bleiben erhalten)")]
    public Task LoeschenAsync(
        [Summary("name", "Route"), Autocomplete(typeof(CustomRouteAutocomplete))] string name) =>
        SafeAsync(async () =>
        {
            var deleted = await Runs.DeleteCustomRouteAsync(GuildId, name);
            await RespondAsync($"🗑️ Route **{deleted}** entfernt.");
        });

    [SlashCommand("liste", "Alle eigenen Routen")]
    public Task ListeAsync() =>
        SafeAsync(async () =>
        {
            var routes = await Runs.GetCustomRoutesAsync(GuildId);
            await RespondAsync(routes.Count == 0
                ? "Noch keine eigenen Routen. Leg eine mit `/route erstellen` an."
                : "**Eigene Routen:**\n" + string.Join("\n", routes.Select(r => $"• {r.Name} _(von {r.CreatedBy})_")));
        });
}
