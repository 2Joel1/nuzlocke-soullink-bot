using Discord;
using Discord.Interactions;
using NuzlockeBot.Data;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

// ---------------------------------------------------------------- Formulare (Modals)

public class FangModal : IModal
{
    public string Title => "🎯 Fang eintragen";

    [InputLabel("Route")]
    [ModalTextInput("route", maxLength: 60)]
    public string Route { get; set; } = "";

    [InputLabel("Pokémon (Name oder Dex-Nummer)")]
    [ModalTextInput("pokemon", placeholder: "z. B. Krabby oder 98", maxLength: 30)]
    public string Pokemon { get; set; } = "";

    [InputLabel("Spitzname (optional)")]
    [RequiredInput(false)]
    [ModalTextInput("spitzname", minLength: 0, maxLength: 30)]
    public string? Spitzname { get; set; }
}

public class TodModal : IModal
{
    public string Title => "💀 Tod eintragen";

    [InputLabel("Route")]
    [ModalTextInput("route", maxLength: 60)]
    public string Route { get; set; } = "";

    [InputLabel("Ursache")]
    [ModalTextInput("ursache", placeholder: "z. B. Arenaleiterin Aloe", maxLength: 100)]
    public string Ursache { get; set; } = "";
}

public class RunEndeModal : IModal
{
    public string Title => "⏹️ Run beenden";

    [InputLabel("Grund (optional)")]
    [RequiredInput(false)]
    [ModalTextInput("grund", placeholder: "z. B. Wipe bei Aloe", minLength: 0, maxLength: 100)]
    public string? Grund { get; set; }
}

public class RouteModal : IModal
{
    public string Title => "🗺️ Eigene Route erstellen";

    [InputLabel("Name der Route")]
    [ModalTextInput("name", placeholder: "z. B. Static Zekrom", maxLength: 60)]
    public string Name { get; set; } = "";
}

/// <summary>
/// Bedienfeld: /menu postet eine Nachricht mit Buttons. Die Buttons funktionieren dauerhaft,
/// auch nach einem Neustart des Bots (feste Custom-IDs mit Präfix "nz:").
/// </summary>
public class MenuModule(RunService runs, AchievementService achievements, CardFactory cards, ProfileService profiles)
    : NuzlockeModuleBase(runs, achievements, cards)
{
    [SlashCommand("menu", "Bedienfeld mit allen Funktionen als Buttons posten")]
    public Task MenuAsync() =>
        SafeAsync(async () =>
        {
            await DeferForImageAsync();
            await SendCardsAsync(false, null, BuildMenu(), await Cards.MenuAsync());
        });

    private static MessageComponent BuildMenu() =>
        new ComponentBuilder()
            .WithButton("Fang", "nz:fang", ButtonStyle.Success, new Emoji("🎯"), row: 0)
            .WithButton("Verpasst", "nz:verpasst", ButtonStyle.Secondary, new Emoji("🟡"), row: 0)
            .WithButton("Tod", "nz:tod", ButtonStyle.Danger, new Emoji("💀"), row: 0)
            .WithButton("Levelcap", "nz:levelcap", ButtonStyle.Primary, new Emoji("📈"), row: 0)
            .WithButton("Status", "nz:status", ButtonStyle.Primary, new Emoji("📋"), row: 1)
            .WithButton("Tabelle", "nz:tabelle", ButtonStyle.Primary, new Emoji("📊"), row: 1)
            .WithButton("Friedhof", "nz:friedhof", ButtonStyle.Primary, new Emoji("🪦"), row: 1)
            .WithButton("Erfolge", "nz:erfolge", ButtonStyle.Primary, new Emoji("🏅"), row: 1)
            .WithButton("Profil", "nz:profil", ButtonStyle.Primary, new Emoji("🪪"), row: 1)
            .WithButton("Beitreten", "nz:beitreten", ButtonStyle.Secondary, new Emoji("➕"), row: 2)
            .WithButton("Run starten", "nz:run-start", ButtonStyle.Secondary, new Emoji("▶️"), row: 2)
            .WithButton("Run beenden", "nz:run-ende", ButtonStyle.Secondary, new Emoji("⏹️"), row: 2)
            .WithButton("Route erstellen", "nz:route", ButtonStyle.Secondary, new Emoji("🗺️"), row: 2)
            .Build();

    /// <summary>Auswahlmenü (max. 25 Einträge – mehr erlaubt Discord nicht).</summary>
    private static MessageComponent Select(string customId, string placeholder, IEnumerable<(string Value, string Label)> options)
    {
        var menu = new SelectMenuBuilder().WithCustomId(customId).WithPlaceholder(placeholder);
        foreach (var (value, label) in options.Take(25))
            menu.AddOption(Cut(label), Cut(value));
        return new ComponentBuilder().WithSelectMenu(menu).Build();
    }

    private static string Cut(string s) => s.Length <= 100 ? s : s[..100];

    // ---------------------------------------------------------------- Fang

    [ComponentInteraction("nz:fang")]
    public Task FangButtonAsync() =>
        SafeAsync(async () =>
        {
            await Runs.EnsureParticipantAsync(GuildId, Context.User.Id);
            var routes = await Runs.GetOpenRouteSuggestionsAsync(GuildId, Context.User.Id);
            if (routes.Count == 0) throw new NuzlockeException("Keine offenen Routen mehr.");

            await RespondAsync("🎯 Auf welcher Route hast du gefangen?" +
                               (routes.Count > 25 ? "\n_Es werden die ersten 25 Routen gezeigt – alle anderen über `/fang`._" : ""),
                components: Select("nz:fang-route", "Route wählen", routes.Select(r => (r, r))), ephemeral: true);
        });

    [ComponentInteraction("nz:fang-route")]
    public Task FangRouteAsync(string[] selected) =>
        SafeAsync(() => RespondWithModalAsync<FangModal>("nz:fang-modal",
            modifyModal: m => m.UpdateTextInput("route", t => t.Value = selected[0])));

    [ModalInteraction("nz:fang-modal")]
    public Task FangModalAsync(FangModal modal) =>
        SafeAsync(() => DoCatchAsync(modal.Route, modal.Pokemon, modal.Spitzname));

    // ---------------------------------------------------------------- Verpasst

    [ComponentInteraction("nz:verpasst")]
    public Task VerpasstButtonAsync() =>
        SafeAsync(async () =>
        {
            await Runs.EnsureParticipantAsync(GuildId, Context.User.Id);
            var routes = await Runs.GetOpenRouteSuggestionsAsync(GuildId, Context.User.Id);
            if (routes.Count == 0) throw new NuzlockeException("Keine offenen Routen mehr.");

            await RespondAsync("🟡 Auf welcher Route hast du den Encounter verpasst?",
                components: Select("nz:verpasst-route", "Route wählen", routes.Select(r => (r, r))), ephemeral: true);
        });

    [ComponentInteraction("nz:verpasst-route")]
    public Task VerpasstRouteAsync(string[] selected) =>
        SafeAsync(() => DoMissAsync(selected[0], null));

    // ---------------------------------------------------------------- Tod

    [ComponentInteraction("nz:tod")]
    public Task TodButtonAsync() =>
        SafeAsync(async () =>
        {
            await Runs.EnsureParticipantAsync(GuildId, Context.User.Id);
            var living = await Runs.GetOwnLivingRoutesAsync(GuildId, Context.User.Id);
            if (living.Count == 0) throw new NuzlockeException("Du hast gerade keine lebenden Pokémon.");

            await RespondAsync("💀 Welches Pokémon ist gestorben?",
                components: Select("nz:tod-route", "Pokémon wählen", living.Select(x => (x.Route, x.Label))), ephemeral: true);
        });

    [ComponentInteraction("nz:tod-route")]
    public Task TodRouteAsync(string[] selected) =>
        SafeAsync(() => RespondWithModalAsync<TodModal>("nz:tod-modal",
            modifyModal: m => m.UpdateTextInput("route", t => t.Value = selected[0])));

    [ModalInteraction("nz:tod-modal")]
    public Task TodModalAsync(TodModal modal) =>
        SafeAsync(() => DoDeathAsync(modal.Route, modal.Ursache));

    // ---------------------------------------------------------------- Übersichten (nur für dich sichtbar)

    [ComponentInteraction("nz:levelcap")]
    public Task LevelcapButtonAsync() => SafeAsync(() => DoShowLevelCapAsync(ephemeral: true));

    [ComponentInteraction("nz:status")]
    public Task StatusButtonAsync() => SafeAsync(() => DoShowStatusAsync(null, ephemeral: true));

    [ComponentInteraction("nz:tabelle")]
    public Task TabelleButtonAsync() =>
        SafeAsync(async () =>
        {
            await DeferForImageAsync(ephemeral: true);
            await SendCardsAsync(true, null, null, await RunTableCardAsync(null), await GlobalTableCardAsync());
        });

    [ComponentInteraction("nz:profil")]
    public Task ProfilButtonAsync() => SafeAsync(() => DoShowProfileAsync(profiles, Context.User, ephemeral: true));

    [ComponentInteraction("nz:friedhof")]
    public Task FriedhofButtonAsync() => SafeAsync(() => DoShowGraveyardAsync(null, ephemeral: true));

    [ComponentInteraction("nz:erfolge")]
    public Task ErfolgeButtonAsync() =>
        SafeAsync(async () =>
        {
            var (player, unlocked, locked) = await Achievements.GetPlayerAchievementsAsync(GuildId, Context.User.Id);
            var (titles, counts) = await Achievements.GetTitlesAsync(GuildId);
            await DeferForImageAsync(ephemeral: true);

            var own = new Card(
                new EmbedBuilder()
                    .WithTitle($"🏅 Erfolge von {player.Name} – {unlocked.Count}/{unlocked.Count + locked.Count}")
                    .WithDescription(Formatting.PlayerAchievementsEmbed(player, null, unlocked, locked).Description)
                    .WithThumbnailUrl(Context.User.GetDisplayAvatarUrl())
                    .WithColor(Formatting.Gold),
                null, "");
            await SendCardsAsync(true, null, null, own, await Cards.TitlesAsync(titles, counts, AchievementService.All.Count));
        });

    // ---------------------------------------------------------------- Verwaltung

    [ComponentInteraction("nz:beitreten")]
    public Task BeitretenButtonAsync() => SafeAsync(() => DoJoinAsync(null));

    [ComponentInteraction("nz:run-start")]
    public Task RunStartButtonAsync() => SafeAsync(DoStartRunAsync);

    [ComponentInteraction("nz:run-ende")]
    public Task RunEndeButtonAsync() =>
        SafeAsync(() => RespondAsync("⏹️ Wie ist der Run ausgegangen?",
            components: Select("nz:run-ende-select", "Ergebnis wählen",
            [
                (nameof(RunStatus.Gescheitert), "💀 Gescheitert (Wipe / Regelbruch)"),
                (nameof(RunStatus.Geschafft), "🏆 Geschafft"),
                (nameof(RunStatus.Abgebrochen), "⏹️ Abgebrochen")
            ]), ephemeral: true));

    [ComponentInteraction("nz:run-ende-select")]
    public Task RunEndeSelectAsync(string[] selected) =>
        SafeAsync(() => RespondWithModalAsync<RunEndeModal>($"nz:run-ende-modal:{selected[0]}"));

    /// <summary>Das Ergebnis steckt in der Custom-ID ("nz:run-ende-modal:Geschafft").</summary>
    [ModalInteraction("nz:run-ende-modal:*")]
    public Task RunEndeModalAsync(string ergebnis, RunEndeModal modal) =>
        SafeAsync(() => DoEndRunAsync(Enum.Parse<RunStatus>(ergebnis), modal.Grund));

    [ComponentInteraction("nz:route")]
    public Task RouteButtonAsync() =>
        SafeAsync(() => RespondWithModalAsync<RouteModal>("nz:route-modal"));

    [ModalInteraction("nz:route-modal")]
    public Task RouteModalAsync(RouteModal modal) =>
        SafeAsync(() => DoCreateRouteAsync(modal.Name));
}
