using Discord.Interactions;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

[Group("spieler", "Mitspieler der Challenge verwalten")]
public class SpielerModule(RunService runs, AchievementService achievements, CardFactory cards) : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("beitreten", "Trag dich als Mitspieler ein (gilt ab dem nächsten Run)")]
    public Task BeitretenAsync(
        [Summary("name", "Anzeigename im Bot (Standard: dein Discord-Name)")] string? name = null) =>
        SafeAsync(() => DoJoinAsync(name));

    [SlashCommand("verlassen", "Ab dem nächsten Run nicht mehr mitspielen")]
    public Task VerlassenAsync() =>
        SafeAsync(async () =>
        {
            await Runs.LeaveAsync(GuildId, Context.User.Id);
            await RespondAsync("👋 Du bist ab dem nächsten Run nicht mehr dabei. Der aktuelle Run bleibt unverändert.");
        });

    [SlashCommand("liste", "Alle eingetragenen Mitspieler")]
    public Task ListeAsync() =>
        SafeAsync(async () =>
        {
            var roster = await Runs.GetRosterAsync(GuildId);
            await RespondAsync(roster.Count == 0
                ? "Noch niemand eingetragen. `/spieler beitreten`"
                : "**Mitspieler:** " + string.Join(", ", roster.Select(p => p.Name)));
        });
}
