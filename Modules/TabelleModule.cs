using Discord.Interactions;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

[Group("tabelle", "Übersichtstabelle aller Spieler")]
public class TabelleModule(RunService runs, AchievementService achievements, CardFactory cards) : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("run", "Tabelle für einen Run: Lebend, Gefangen, Tode, Verpasst")]
    public Task RunTabelleAsync(
        [Summary("nummer", "Run-Nummer (leer = aktueller Run)"), MinValue(1)] int nummer = 0) =>
        SafeAsync(async () =>
        {
            await Runs.GetRunAsync(GuildId, nummer > 0 ? nummer : null); // prüft vorab, ob es den Run gibt
            await DeferForImageAsync();
            await SendCardsAsync(false, null, null, await RunTableCardAsync(nummer > 0 ? nummer : null));
        });

    [SlashCommand("global", "Tabelle über alle Runs")]
    public Task GlobalTabelleAsync() =>
        SafeAsync(async () =>
        {
            await Runs.GetRunAsync(GuildId, null); // prüft vorab, ob es überhaupt einen Run gibt
            await DeferForImageAsync();
            await SendCardsAsync(false, null, null, await GlobalTableCardAsync());
        });
}
