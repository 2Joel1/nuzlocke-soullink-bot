using Discord.Interactions;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

/// <summary>Levelcap bis zum nächsten Pflichtkampf und Arena-Fortschritt des aktuellen Runs.</summary>
public class LevelCapModule(RunService runs, AchievementService achievements, CardFactory cards)
    : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("levelcap", "Levelcap bis zur nächsten Arena (höchstes Level des Arenaleiters)")]
    public Task LevelCapAsync() => SafeAsync(() => DoShowLevelCapAsync(ephemeral: false));
}

[Group("arena", "Arena-Fortschritt des aktuellen Runs")]
public class ArenaModule(RunService runs, AchievementService achievements, CardFactory cards)
    : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("geschafft", "Nächste Arena (bzw. Top Vier, N, G-Cis) als geschafft eintragen")]
    public Task GeschafftAsync() =>
        SafeAsync(async () =>
        {
            var (run, beaten) = await Runs.AdvanceProgressAsync(GuildId);
            await DeferForImageAsync();
            await SendCardsAsync(false, null, null, await Cards.FightWonAsync(run, beaten), await Cards.LevelCapAsync(run));
        });

    [SlashCommand("setzen", "Fortschritt korrigieren: Anzahl geschaffter Kämpfe")]
    public Task SetzenAsync(
        [Summary("geschafft", "0 = noch keine Arena, 8 = alle Orden, 9 = Top Vier, 10 = N, 11 = G-Cis"), MinValue(0), MaxValue(11)]
        int geschafft) =>
        SafeAsync(async () =>
        {
            var run = await Runs.SetProgressAsync(GuildId, geschafft);
            await DeferForImageAsync();
            await SendCardsAsync(false, $"✏️ Fortschritt von Run #{run.Number} auf **{geschafft}** gesetzt.", null,
                await Cards.LevelCapAsync(run));
        });
}
