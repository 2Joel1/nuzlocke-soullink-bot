using Discord;
using Discord.Interactions;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

/// <summary>Live-Statusnachricht: eine angepinnte Nachricht mit Board + Tabelle, die sich selbst aktualisiert.</summary>
[Group("live", "Live-Statusnachricht verwalten")]
[DefaultMemberPermissions(GuildPermission.ManageChannels)]
public class LiveModule(RunService runs, AchievementService achievements, CardFactory cards, LiveStatusService live)
    : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("einrichten", "Live-Status in diesem Kanal posten (ersetzt einen bestehenden)")]
    public Task EinrichtenAsync() =>
        SafeAsync(async () =>
        {
            var channel = Context.Channel as ITextChannel
                ?? throw new NuzlockeException("Das geht nur in einem Text-Kanal.");

            await DeferAsync(ephemeral: true);
            var warning = await live.SetupAsync(GuildId, channel);
            await FollowupAsync("📡 Live-Status eingerichtet. Die Nachricht aktualisiert sich ab jetzt bei jeder Änderung selbst." +
                                (warning is null ? "" : $"\n⚠️ {warning}"), ephemeral: true);
        });

    [SlashCommand("entfernen", "Live-Status-Nachricht löschen")]
    public Task EntfernenAsync() =>
        SafeAsync(async () =>
        {
            await DeferAsync(ephemeral: true);
            var removed = await live.RemoveAsync(GuildId);
            await FollowupAsync(removed ? "🗑️ Live-Status entfernt." : "Es war kein Live-Status eingerichtet.", ephemeral: true);
        });
}
