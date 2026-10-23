using Discord.Interactions;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

public class EncounterModule(RunService runs, AchievementService achievements, CardFactory cards) : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("fang", "Gefangenes Pokémon für eine Route eintragen")]
    public Task FangAsync(
        [Summary("route", "Route oder Ort"), Autocomplete(typeof(OpenRouteAutocomplete))] string route,
        [Summary("pokemon", "Welches Pokémon?"), Autocomplete(typeof(PokemonAutocomplete))] string pokemon,
        [Summary("spitzname", "Optionaler Spitzname")] string? spitzname = null) =>
        SafeAsync(() => DoCatchAsync(route, pokemon, spitzname));

    [SlashCommand("verpasst", "Encounter auf einer Route verpasst (geflohen, besiegt, ...)")]
    public Task VerpasstAsync(
        [Summary("route", "Route oder Ort"), Autocomplete(typeof(OpenRouteAutocomplete))] string route,
        [Summary("pokemon", "Welches Pokémon war es? (optional)"), Autocomplete(typeof(PokemonAutocomplete))] string? pokemon = null) =>
        SafeAsync(() => DoMissAsync(route, pokemon));

    [SlashCommand("tod", "Eines deiner Pokémon ist gestorben – alle Partner sterben mit")]
    public Task TodAsync(
        [Summary("route", "Route des gestorbenen Pokémon"), Autocomplete(typeof(OwnLivingRouteAutocomplete))] string route,
        [Summary("ursache", "z. B. \"Arenaleiterin Aloe\" oder \"Krit von Wild-Tragosso\"")] string ursache) =>
        SafeAsync(() => DoDeathAsync(route, ursache));

    [SlashCommand("entwicklung", "Eines deiner Pokémon hat sich entwickelt")]
    public Task EntwicklungAsync(
        [Summary("route", "Route des Pokémon"), Autocomplete(typeof(OwnLivingRouteAutocomplete))] string route,
        [Summary("pokemon", "Zu welchem Pokémon?"), Autocomplete(typeof(PokemonAutocomplete))] string pokemon) =>
        SafeAsync(() => DoEvolveAsync(route, pokemon));

    [SlashCommand("status", "Übersicht über den aktuellen (oder einen alten) Run")]
    public Task StatusAsync(
        [Summary("run", "Run-Nummer (leer = aktueller Run)"), MinValue(1)] int run = 0) =>
        SafeAsync(() => DoShowStatusAsync(run > 0 ? run : null, ephemeral: false));

    [SlashCommand("friedhof", "Alle gestorbenen Soul Links eines Runs")]
    public Task FriedhofAsync(
        [Summary("run", "Run-Nummer (leer = aktueller Run)"), MinValue(1)] int run = 0) =>
        SafeAsync(() => DoShowGraveyardAsync(run > 0 ? run : null, ephemeral: false));
}
