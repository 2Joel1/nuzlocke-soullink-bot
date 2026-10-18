using Discord;
using Discord.Interactions;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

public class ErfolgeModule(RunService runs, AchievementService achievements, CardFactory cards) : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("erfolge", "Freigeschaltete und gesperrte Erfolge eines Spielers")]
    public Task ErfolgeAsync(
        [Summary("spieler", "Wessen Erfolge? (Standard: deine)")] IUser? spieler = null) =>
        SafeAsync(() => DoShowPlayerAchievementsAsync(spieler ?? Context.User, ephemeral: false));

    [SlashCommand("titel", "Aktuelle Titelträger (Pechvogel, Meisterfänger, ...) und Erfolge aller Spieler")]
    public Task TitelAsync() =>
        SafeAsync(() => DoShowTitlesAsync(ephemeral: false));
}
