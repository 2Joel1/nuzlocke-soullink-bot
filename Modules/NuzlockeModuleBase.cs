using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using NuzlockeBot.Data;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

/// <summary>
/// Gemeinsame Basis aller Module. Enthält die eigentlichen Aktionen (Fang, Tod, ...),
/// damit Slash-Commands und das Bedienfeld (/menu) exakt dasselbe tun.
/// </summary>
public abstract class NuzlockeModuleBase(RunService runs, AchievementService achievements, CardFactory cards)
    : InteractionModuleBase<SocketInteractionContext>
{
    protected RunService Runs { get; } = runs;
    protected AchievementService Achievements { get; } = achievements;
    protected CardFactory Cards { get; } = cards;

    protected ulong GuildId =>
        Context.Guild?.Id ?? throw new NuzlockeException("Der Bot funktioniert nur auf einem Server, nicht in DMs.");

    protected string DisplayName =>
        (Context.User as SocketGuildUser)?.DisplayName ?? Context.User.GlobalName ?? Context.User.Username;

    /// <summary>Führt einen Command aus und zeigt Regelverstöße nur dem Aufrufer an.</summary>
    protected async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (NuzlockeException ex)
        {
            if (Context.Interaction.HasResponded)
                await FollowupAsync($"⚠️ {ex.Message}", ephemeral: true);
            else
                await RespondAsync($"⚠️ {ex.Message}", ephemeral: true);
        }
    }

    // ---------------------------------------------------------------- Karten (Embed + Bild) senden

    /// <summary>
    /// Bilder zu erzeugen kann länger als die 3 Sekunden dauern, die Discord für eine Antwort erlaubt.
    /// Deshalb erst "Bot denkt nach…" anzeigen, dann rendern und nachreichen.
    /// </summary>
    protected async Task DeferForImageAsync(bool ephemeral = false)
    {
        if (Context.Interaction.HasResponded) return;

        switch (Context.Interaction)
        {
            case SocketMessageComponent component:
                await component.DeferLoadingAsync(ephemeral);
                break;
            case SocketModal modal:
                await modal.DeferLoadingAsync(ephemeral);
                break;
            default:
                await DeferAsync(ephemeral);
                break;
        }
    }

    /// <summary>Sendet eine oder mehrere Karten in einer Nachricht. Jede Karte bekommt ihr Bild unten im Embed.</summary>
    protected async Task SendCardsAsync(bool ephemeral, string? text, MessageComponent? components, params Card[] cards)
    {
        var (embeds, files) = cards.Prepare();
        try
        {
            var responded = Context.Interaction.HasResponded;
            if (files.Count == 0 && !responded)
                await RespondAsync(text, embeds: embeds, ephemeral: ephemeral, components: components);
            else if (files.Count == 0)
                await FollowupAsync(text, embeds: embeds, ephemeral: ephemeral, components: components);
            else if (!responded)
                await RespondWithFilesAsync(files, text, embeds: embeds, ephemeral: ephemeral, components: components);
            else
                await FollowupWithFilesAsync(files, text, embeds: embeds, ephemeral: ephemeral, components: components);
        }
        finally
        {
            foreach (var file in files) file.Dispose();
        }
    }

    /// <summary>Nach jeder Aktion: neue Erfolge prüfen und öffentlich verkünden.</summary>
    /// <summary>⭐-Button unter Fang-, Verpasst- und Tod-Bannern. Funktioniert dauerhaft (auch nach Neustart).</summary>
    protected static MessageComponent HighlightButton(HighlightKind kind, int id) =>
        new ComponentBuilder()
            .WithButton("Highlight", $"nz:hl:{HighlightService.Key(kind, id)}", ButtonStyle.Secondary, new Emoji("⭐"))
            .Build();

    protected async Task AnnounceAchievementsAsync()
    {
        var unlocked = await Achievements.CheckAsync(GuildId);
        if (unlocked.Count > 0)
            await SendCardsAsync(false, null, null, await Cards.AchievementAsync(unlocked));
    }

    // ---------------------------------------------------------------- Aktionen

    protected async Task DoCatchAsync(string route, string pokemon, string? nickname)
    {
        var (run, link) = await Runs.CatchAsync(GuildId, Context.User.Id, route, pokemon, nickname);
        await DeferForImageAsync();

        var missing = run.Participants.Count(p => link.Encounters.All(e => e.PlayerId != p.PlayerId));
        var statusLine = link.Status == LinkStatus.Gebrochen
            ? "🟡 Ein Partner hat diesen Encounter verpasst – das Pokémon ist nicht nutzbar (ab in die Box)."
            : missing > 0 ? $"Noch {missing} Spieler offen auf dieser Route." : "🔗 Soul Link komplett!";

        var me = link.Encounters.First(e => e.Player.DiscordUserId == (long)Context.User.Id);
        await SendCardsAsync(false, null, HighlightButton(HighlightKind.Fang, me.Id), await Cards.CatchAsync(run, link, me, statusLine));
        await AnnounceAchievementsAsync();
    }

    protected async Task DoMissAsync(string route, string? pokemon)
    {
        var (run, link) = await Runs.MissAsync(GuildId, Context.User.Id, route, pokemon);
        await DeferForImageAsync();

        var me = link.Encounters.First(e => e.Player.DiscordUserId == (long)Context.User.Id);
        await SendCardsAsync(false, null, HighlightButton(HighlightKind.Verpasst, me.Id), await Cards.MissAsync(run, link, me));
        await AnnounceAchievementsAsync();
    }

    protected async Task DoDeathAsync(string route, string cause)
    {
        var (run, link) = await Runs.DeathAsync(GuildId, Context.User.Id, route, cause);
        await DeferForImageAsync();

        var me = link.Encounters.First(e => e.Player.DiscordUserId == (long)Context.User.Id);
        await SendCardsAsync(false, null, HighlightButton(HighlightKind.Tod, link.Id), await Cards.DeathAsync(run, link, me));
        await AnnounceAchievementsAsync();
    }

    protected async Task DoEvolveAsync(string route, string pokemon)
    {
        var result = await Runs.EvolveAsync(GuildId, Context.User.Id, route, pokemon);
        await DeferForImageAsync();
        await SendCardsAsync(false, null, null, await Cards.EvolutionAsync(result));
        await AnnounceAchievementsAsync();
    }

    protected async Task DoJoinAsync(string? name)
    {
        var player = await Runs.JoinAsync(GuildId, Context.User.Id, string.IsNullOrWhiteSpace(name) ? DisplayName : name.Trim());
        await RespondAsync($"✅ **{player.Name}** ist dabei – ab dem nächsten Run.");
    }

    protected async Task DoStartRunAsync()
    {
        var (run, previous) = await Runs.StartRunAsync(GuildId);
        await DeferForImageAsync();
        await SendCardsAsync(false, null, null, await Cards.RunStartAsync(run, previous));
    }

    protected async Task DoEndRunAsync(RunStatus status, string? reason)
    {
        var run = await Runs.EndRunAsync(GuildId, status, reason);
        await DeferForImageAsync();

        await SendCardsAsync(false,
            status == RunStatus.Geschafft ? "🏆 **GG!** Run geschafft!" : $"Neuer Versuch mit `/run start`.",
            null,
            await Cards.RunEndAsync(run),
            await Cards.RecapAsync(run));
        await AnnounceAchievementsAsync();
    }

    protected async Task DoCreateRouteAsync(string name)
    {
        var route = await Runs.CreateCustomRouteAsync(GuildId, name, DisplayName);
        await RespondAsync($"🗺️ Route **{route.Name}** angelegt. Sie steht ab jetzt bei allen in `/fang` und `/verpasst` zur Auswahl – auch in künftigen Runs.");
    }

    // ---------------------------------------------------------------- Übersichten

    protected async Task DoShowStatusAsync(int? number, bool ephemeral)
    {
        var run = await Runs.GetRunAsync(GuildId, number);
        await DeferForImageAsync(ephemeral);
        await SendCardsAsync(ephemeral, null, null, await Cards.StatusAsync(run));
    }

    protected async Task DoShowGraveyardAsync(int? number, bool ephemeral)
    {
        var run = await Runs.GetRunAsync(GuildId, number);
        await DeferForImageAsync(ephemeral);
        await SendCardsAsync(ephemeral, null, null, await Cards.GraveyardAsync(run));
    }

    protected async Task DoShowLevelCapAsync(bool ephemeral)
    {
        var run = await Runs.GetRunAsync(GuildId, null);
        await DeferForImageAsync(ephemeral);
        await SendCardsAsync(ephemeral, null, null, await Cards.LevelCapAsync(run));
    }

    protected async Task DoShowRecapAsync(int? number)
    {
        var run = await Runs.GetRunAsync(GuildId, number);
        await DeferForImageAsync();
        await SendCardsAsync(false, null, null, await Cards.RecapAsync(run));
    }

    protected async Task<Card> RunTableCardAsync(int? number)
    {
        var run = await Runs.GetRunAsync(GuildId, number);
        var stats = RunService.ComputeStats([run], aliveIn: run);
        return await Cards.StatsAsync($"📊 Run #{run.Number} – {Formatting.RunStatusText(run.Status)}", stats, global: false,
            run.Status == RunStatus.Aktiv ? Tone.Green : Tone.Grey, "tabelle-run.png",
            $"RUN {run.Number}", CardFactory.StatusWord(run.Status));
    }

    protected async Task<Card> GlobalTableCardAsync()
    {
        var all = await Runs.GetAllRunsAsync(GuildId);
        if (all.Count == 0) throw new NuzlockeException("Es gibt noch keinen Run.");

        var active = all.FirstOrDefault(r => r.Status == RunStatus.Aktiv);
        var stats = RunService.ComputeStats(all, aliveIn: active);
        var used = all.Count(r => r.Status != RunStatus.Aktiv);
        return await Cards.StatsAsync($"🌍 Alle Runs – {used} verbraucht", stats,
            global: true, Tone.Gold, "tabelle-global.png", "ALLE RUNS", used == 1 ? "1 RUN VERBRAUCHT" : $"{used} RUNS VERBRAUCHT");
    }

    protected async Task DoShowPlayerAchievementsAsync(IUser user, bool ephemeral)
    {
        var (player, unlocked, locked) = await Achievements.GetPlayerAchievementsAsync(GuildId, user.Id);
        await RespondAsync(
            embed: Formatting.PlayerAchievementsEmbed(player, user.GetDisplayAvatarUrl(), unlocked, locked),
            ephemeral: ephemeral);
    }

    protected async Task DoShowProfileAsync(ProfileService profiles, IUser user, bool ephemeral)
    {
        var profile = await profiles.GetProfileAsync(GuildId, user.Id);
        await DeferForImageAsync(ephemeral);
        await SendCardsAsync(ephemeral, null, null, await Cards.ProfileAsync(profile, user.GetDisplayAvatarUrl()));
    }

    protected async Task DoShowTitlesAsync(bool ephemeral)
    {
        var (titles, counts) = await Achievements.GetTitlesAsync(GuildId);
        await DeferForImageAsync(ephemeral);
        await SendCardsAsync(ephemeral, null, null, await Cards.TitlesAsync(titles, counts, AchievementService.All.Count));
    }
}
