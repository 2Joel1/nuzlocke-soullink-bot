using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using NuzlockeBot.Data;
using NuzlockeBot.Services;

namespace NuzlockeBot.Modules;

/// <summary>Filter für /highlights (eigenes Enum mit "Alle", damit der Parameter optional bleiben kann).</summary>
public enum HighlightArt
{
    Alle,
    [ChoiceDisplay("🎯 Fänge")] Fang,
    [ChoiceDisplay("🟡 Verpasst")] Verpasst,
    [ChoiceDisplay("💀 Tode")] Tod
}

/// <summary>/highlight-Autocomplete: alle Ereignisse (Fänge, verpasste Encounter, Tode), neueste zuerst.</summary>
public class HighlightEventAutocomplete : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        if (context.Guild is null) return AutocompletionResult.FromSuccess(Array.Empty<AutocompleteResult>());
        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var highlights = (HighlightService)services.GetService(typeof(HighlightService))!;
        var options = await highlights.GetEventOptionsAsync(context.Guild.Id);

        return AutocompletionResult.FromSuccess(options
            .Where(o => o.Label.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(o => new AutocompleteResult(o.Label.Length <= 100 ? o.Label : o.Label[..99] + "…", o.Key)));
    }
}

/// <summary>Highlights: besondere Momente markieren (⭐) und als Hall of Fame anzeigen.</summary>
public class HighlightModule(RunService runs, AchievementService achievements, CardFactory cards, HighlightService highlights)
    : NuzlockeModuleBase(runs, achievements, cards)
{
    // ---------------------------------------------------------------- Markieren

    [SlashCommand("highlight", "Einen Fang, verpassten Encounter oder Tod als Highlight markieren")]
    public Task HighlightAsync(
        [Summary("ereignis", "Welcher Moment?"), Autocomplete(typeof(HighlightEventAutocomplete))] string ereignis,
        [Summary("notiz", "Warum ist das ein Highlight? (optional)"), MaxLength(120)] string? notiz = null) =>
        SafeAsync(() => StarAsync(ereignis, notiz, toggle: false));

    /// <summary>⭐-Button unter den Bannern. Zweiter Klick nimmt den eigenen Stern zurück.</summary>
    [ComponentInteraction("nz:hl:*")]
    public Task HighlightButtonAsync(string key) =>
        SafeAsync(() => StarAsync(key, null, toggle: true));

    private async Task StarAsync(string key, string? note, bool toggle)
    {
        var result = await highlights.StarAsync(GuildId, Context.User.Id, key, note, toggle);

        if (!result.Added)
        {
            await RespondAsync(result.Highlight is null
                ? "Stern zurückgenommen – damit ist es kein Highlight mehr."
                : $"Stern zurückgenommen ({result.Highlight.Stars} übrig).", ephemeral: true);
            return;
        }

        var h = result.Highlight!;
        var text = result.Created
            ? $"⭐ **{result.PlayerName}** hat {h.Phrase} zum Highlight gemacht!"
            : $"⭐ **{result.PlayerName}** findet {h.Phrase} auch stark – jetzt **{h.Stars} Sterne**.";
        if (!string.IsNullOrWhiteSpace(note)) text += $"\n> {note.Trim()}";
        text += "\n_Alle Highlights: `/highlights`_";

        await RespondAsync(text);
        await AnnounceAchievementsAsync();
    }

    // ---------------------------------------------------------------- Anzeigen

    [SlashCommand("highlights", "Hall of Fame: alle Highlights, sortiert nach Sternen")]
    public Task HighlightsAsync(
        [Summary("run", "Nur dieser Run"), MinValue(1)] int run = 0,
        [Summary("spieler", "Nur Momente dieses Spielers")] IUser? spieler = null,
        [Summary("art", "Nur Fänge, verpasste Encounter oder Tode")] HighlightArt art = HighlightArt.Alle) =>
        SafeAsync(async () =>
        {
            HighlightKind? kind = art switch
            {
                HighlightArt.Fang => HighlightKind.Fang,
                HighlightArt.Verpasst => HighlightKind.Verpasst,
                HighlightArt.Tod => HighlightKind.Tod,
                _ => null
            };
            await DeferForImageAsync();
            await SendPageAsync(0, run, spieler?.Id ?? 0, kind);
        });

    /// <summary>Blättern: Seite und Filter stecken in der Button-ID ("seite|run|user|art").</summary>
    [ComponentInteraction("nz:hlp:*")]
    public Task PageAsync(string state) =>
        SafeAsync(async () =>
        {
            var parts = state.Split('|');
            var page = int.Parse(parts[0]);
            var run = int.Parse(parts[1]);
            var user = ulong.Parse(parts[2]);
            HighlightKind? kind = parts[3] == "-" ? null : Enum.Parse<HighlightKind>(parts[3]);

            await ((SocketMessageComponent)Context.Interaction).DeferAsync();
            var (card, components) = await BuildPageAsync(page, run, user, kind);
            var (embeds, files) = new[] { card }.Prepare();
            try
            {
                await ModifyOriginalResponseAsync(m =>
                {
                    m.Embeds = embeds;
                    m.Attachments = files;
                    m.Components = components ?? new ComponentBuilder().Build();
                });
            }
            finally
            {
                foreach (var f in files) f.Dispose();
            }
        });

    private async Task SendPageAsync(int page, int run, ulong user, HighlightKind? kind)
    {
        var (card, components) = await BuildPageAsync(page, run, user, kind);
        await SendCardsAsync(false, null, components, card);
    }

    private async Task<(Card Card, MessageComponent? Components)> BuildPageAsync(int page, int run, ulong user, HighlightKind? kind)
    {
        var all = await highlights.ListAsync(GuildId, run > 0 ? run : null, user > 0 ? user : null, kind);
        var pages = CardFactory.HighlightPages(all.Count);
        page = Math.Clamp(page, 0, pages - 1);

        var filter = new List<string> { run > 0 ? $"RUN {run}" : "ALLE RUNS" };
        if (kind is { } k) filter.Add(k.ToString().ToUpperInvariant());
        if (user > 0 && all.FirstOrDefault() is { } first) filter.Add(first.Owner.ToUpperInvariant());

        var card = await Cards.HighlightsAsync(all, page, string.Join("  ", filter));
        if (pages <= 1) return (card, null);

        string State(int p) => $"{p}|{run}|{user}|{(kind?.ToString() ?? "-")}";
        var components = new ComponentBuilder()
            .WithButton("Zurück", $"nz:hlp:{State(page - 1)}", ButtonStyle.Secondary, new Emoji("◀️"), disabled: page == 0)
            .WithButton("Weiter", $"nz:hlp:{State(page + 1)}", ButtonStyle.Secondary, new Emoji("▶️"), disabled: page >= pages - 1)
            .Build();
        return (card, components);
    }
}
