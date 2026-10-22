using Discord;
using Discord.Interactions;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

/// <summary>Trainerpass (/profil) und Lieblings-Pokémon (/favorit).</summary>
public class ProfilModule(RunService runs, AchievementService achievements, CardFactory cards, ProfileService profiles)
    : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("profil", "Trainerpass eines Spielers: Statistiken über alle Runs, Titel, Erfolge und Highlights")]
    public Task ProfilAsync(
        [Summary("spieler", "Wessen Profil? (Standard: deins)")] IUser? spieler = null) =>
        SafeAsync(() => DoShowProfileAsync(profiles, spieler ?? Context.User, ephemeral: false));

    [SlashCommand("favorit", "Lieblings-Pokémon für dein Profil festlegen (leer lassen = automatisch das meistgefangene)")]
    public Task FavoritAsync(
        [Summary("pokemon", "Name oder Dex-Nummer"), Autocomplete(typeof(PokemonAutocomplete))] string? pokemon = null) =>
        SafeAsync(async () =>
        {
            var favorite = await profiles.SetFavoriteAsync(GuildId, Context.User.Id, pokemon);
            await RespondAsync(favorite is null
                    ? "↩️ Kein Favorit mehr – dein Profil zeigt wieder automatisch dein meistgefangenes Pokémon."
                    : $"⭐ **{favorite}** ist jetzt dein Lieblings-Pokémon. Schau's dir mit `/profil` an.",
                ephemeral: true);
        });
}
