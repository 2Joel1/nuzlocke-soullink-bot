using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using NuzlockeBot.Data;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

/// <summary>Gemeinsame Logik: Eingabe filtern, max. 25 Vorschläge (Discord-Limit).</summary>
public abstract class RouteAutocompleteBase : AutocompleteHandler
{
    protected abstract Task<List<(string Value, string Label)>> GetOptionsAsync(RunService runs, ulong guildId, ulong userId);

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        if (context.Guild is null)
            return AutocompletionResult.FromSuccess(Array.Empty<AutocompleteResult>());

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var runs = services.GetRequiredService<RunService>();
        var options = await GetOptionsAsync(runs, context.Guild.Id, context.User.Id);

        var results = options
            .Where(o => o.Label.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(o => new AutocompleteResult(Cut(o.Label), Cut(o.Value)));

        return AutocompletionResult.FromSuccess(results);
    }

    private static string Cut(string s) => s.Length <= 100 ? s : s[..100];
}

/// <summary>/fang, /verpasst: offene Routen (Partner-Routen zuerst, dann Standardliste).</summary>
public class OpenRouteAutocomplete : RouteAutocompleteBase
{
    protected override async Task<List<(string Value, string Label)>> GetOptionsAsync(RunService runs, ulong guildId, ulong userId) =>
        (await runs.GetOpenRouteSuggestionsAsync(guildId, userId)).Select(r => (r, r)).ToList();
}

/// <summary>/tod: eigene lebende Pokémon, angezeigt als "Route 3 – Bob (Krabby)".</summary>
public class OwnLivingRouteAutocomplete : RouteAutocompleteBase
{
    protected override async Task<List<(string Value, string Label)>> GetOptionsAsync(RunService runs, ulong guildId, ulong userId) =>
        (await runs.GetOwnLivingRoutesAsync(guildId, userId)).Select(x => (x.Route, x.Label)).ToList();
}

/// <summary>/korrektur fang-loeschen: Routen, auf denen der Spieler einen Encounter hat.</summary>
public class OwnEncounterRouteAutocomplete : RouteAutocompleteBase
{
    protected override async Task<List<(string Value, string Label)>> GetOptionsAsync(RunService runs, ulong guildId, ulong userId) =>
        (await runs.GetRunRoutesAsync(guildId, status: null, withEncounterOfUser: userId)).Select(r => (r, r)).ToList();
}

/// <summary>/korrektur tod-zuruecknehmen: tote Links des aktiven Runs.</summary>
public class DeadRouteAutocomplete : RouteAutocompleteBase
{
    protected override async Task<List<(string Value, string Label)>> GetOptionsAsync(RunService runs, ulong guildId, ulong userId) =>
        (await runs.GetRunRoutesAsync(guildId, LinkStatus.Tot)).Select(r => (r, r)).ToList();
}

/// <summary>Alle 649 Pokémon aus Schwarz. Treffer am Wortanfang zuerst, angezeigt als "#098 Krabby".</summary>
public class PokemonAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        var input = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim();

        var results = Pokedex.Names
            .Select((name, i) => (Name: name, Nr: i + 1))
            .Where(p => p.Name.Contains(input, StringComparison.OrdinalIgnoreCase) || p.Nr.ToString() == input)
            .OrderBy(p => p.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Nr)
            .Take(25)
            .Select(p => new AutocompleteResult($"#{p.Nr:000} {p.Name}", p.Name));

        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}

/// <summary>/route loeschen: eigene Routen des Servers.</summary>
public class CustomRouteAutocomplete : RouteAutocompleteBase
{
    protected override async Task<List<(string Value, string Label)>> GetOptionsAsync(RunService runs, ulong guildId, ulong userId) =>
        (await runs.GetCustomRoutesAsync(guildId)).Select(r => (r.Name, r.Name)).ToList();
}
