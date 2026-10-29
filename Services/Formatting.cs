using System.Text;
using Discord;
using NuzlockeBot.Data;

namespace NuzlockeBot.Services;

/// <summary>Einheitliche Texte und Embeds für alle Commands.</summary>
public static class Formatting
{
    public static readonly Color Green = new(0x3B, 0xA5, 0x5C);
    public static readonly Color Yellow = new(0xF0, 0xB2, 0x32);
    public static readonly Color Red = new(0xD8, 0x3C, 0x3E);
    public static readonly Color Grey = new(0x80, 0x84, 0x8E);
    public static readonly Color Gold = new(0xE6, 0xC2, 0x29);
    public static readonly Color Blue = new(0x48, 0x80, 0xE0);

    private const int DescriptionLimit = 4000; // Discord erlaubt 4096

    // ---------------------------------------------------------------- Bilder (PokeAPI-Sprites auf GitHub)

    private const string SpriteBase = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites";

    /// <summary>Animiertes Sprite aus Schwarz/Weiß, oder null bei unbekanntem Pokémon ("?").</summary>
    public static string? SpriteUrl(string? species) =>
        species is not null && Pokedex.Number(species) is { } nr
            ? $"{SpriteBase}/pokemon/versions/generation-v/black-white/animated/{nr}.gif"
            : null;

    /// <summary>Großes offizielles Artwork (für Erfolge wie "Legendär").</summary>
    public static string? ArtworkUrl(string? species) =>
        species is not null && Pokedex.Number(species) is { } nr
            ? $"{SpriteBase}/pokemon/other/official-artwork/{nr}.png"
            : null;

    /// <summary>Item-Sprite, z. B. "poke-ball", "master-ball", "rare-candy".</summary>
    public static string ItemUrl(string item) => $"{SpriteBase}/items/{item}.png";

    public static string? PokewikiUrl(string? species) =>
        species is not null && Pokedex.Number(species) is not null
            ? $"https://www.pokewiki.de/{Uri.EscapeDataString(species)}"
            : null;

    public static string Icon(LinkStatus status) => status switch
    {
        LinkStatus.Lebendig => "🟢",
        LinkStatus.Gebrochen => "🟡",
        _ => "💀"
    };

    public static string RunStatusText(RunStatus status) => status switch
    {
        RunStatus.Aktiv => "🟢 aktiv",
        RunStatus.Gescheitert => "💀 gescheitert",
        RunStatus.Geschafft => "🏆 geschafft",
        _ => "⏹️ abgebrochen"
    };

    public static string PokemonText(Encounter e) =>
        string.IsNullOrWhiteSpace(e.Nickname) ? e.Species : $"{e.Nickname} ({e.Species})";

    public static string EncounterText(Encounter? e)
    {
        if (e is null) return "_offen_";
        if (e.Status == EncounterStatus.Verpasst) return "~~verpasst~~";
        return PokemonText(e);
    }

    /// <summary>"Joel: Bob (Krabby) · Max: _offen_ · ..." für alle Teilnehmer des Runs.</summary>
    public static string PartnerLine(Run run, RouteLink link) =>
        string.Join(" · ", run.Participants
            .OrderBy(p => p.PlayerId)
            .Select(p => $"{p.Player.Name}: {EncounterText(link.Encounters.FirstOrDefault(e => e.PlayerId == p.PlayerId))}"));

    public static string LinkLine(Run run, RouteLink link)
    {
        var line = $"{Icon(link.Status)} **{link.Route}** — {PartnerLine(run, link)}";
        if (link.Status == LinkStatus.Tot && !string.IsNullOrWhiteSpace(link.DeathCause))
            line += $"\n  ↳ {link.DeathCause}" + (link.CausedByPlayer is null ? "" : $" ({link.CausedByPlayer.Name})");
        return line;
    }

    public static string Timestamp(DateTime utc, char style = 'd')
    {
        var unix = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        return $"<t:{unix}:{style}>";
    }

    public static string Truncate(string text, int max = DescriptionLimit) =>
        text.Length <= max ? text : text[..(max - 12)] + "\n… (gekürzt)";

    public static string Counts(Run run)
    {
        var alive = run.Links.Count(l => l.Status == LinkStatus.Lebendig);
        var broken = run.Links.Count(l => l.Status == LinkStatus.Gebrochen);
        var dead = run.Links.Count(l => l.Status == LinkStatus.Tot);
        return $"🟢 {alive} lebendig · 🟡 {broken} gebrochen · 💀 {dead} tot";
    }

    /// <summary>Monospace-Tabelle als Codeblock (Discord kann keine echten Tabellen).</summary>
    public static string Table(string[] headers, IReadOnlyList<string[]> rows)
    {
        var widths = headers.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        var sb = new StringBuilder("```\n");

        void Line(string[] cells) =>
            sb.AppendLine(string.Join("  ", cells.Select((c, i) => i == 0 ? c.PadRight(widths[i]) : c.PadLeft(widths[i]))).TrimEnd());

        Line(headers);
        sb.AppendLine(string.Join("  ", widths.Select(w => new string('─', w))));
        foreach (var row in rows) Line(row);

        return sb.Append("```").ToString();
    }

    public static Embed StatsEmbed(string title, IReadOnlyList<PlayerStats> stats, bool global, Color color) =>
        StatsEmbedBuilder(title, stats, global, color).Build();

    public static EmbedBuilder StatsEmbedBuilder(string title, IReadOnlyList<PlayerStats> stats, bool global, Color color)
    {
        string Name(string n) => n.Length <= 14 ? n : n[..13] + "…";

        var headers = global
            ? new[] { "Spieler", "Runs", "Lebend", "Gefangen", "Tode", "Verpasst", "Wipes" }
            : new[] { "Spieler", "Lebend", "Gefangen", "Tode", "Verpasst" };

        var rows = stats.Select(s => global
                ? new[] { Name(s.Name), $"{s.Runs}", $"{s.Alive}", $"{s.Caught}", $"{s.Deaths}", $"{s.Missed}", $"{s.Wipes}" }
                : new[] { Name(s.Name), $"{s.Alive}", $"{s.Caught}", $"{s.Deaths}", $"{s.Missed}" })
            .ToList();

        if (stats.Count > 1)
        {
            rows.Add(global
                ? new[] { "Gesamt", "", $"{stats.Sum(s => s.Alive)}", $"{stats.Sum(s => s.Caught)}", $"{stats.Sum(s => s.Deaths)}", $"{stats.Sum(s => s.Missed)}", $"{stats.Sum(s => s.Wipes)}" }
                : new[] { "Gesamt", $"{stats.Sum(s => s.Alive)}", $"{stats.Sum(s => s.Caught)}", $"{stats.Sum(s => s.Deaths)}", $"{stats.Sum(s => s.Missed)}" });
        }

        return new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(Truncate(Table(headers, rows)))
            .WithColor(color)
            .WithFooter((global ? "Lebend = im laufenden Run · Wipes = gescheiterte Runs, deren letzten Tod der Spieler ausgelöst hat · " : "") +
                        "Tode = Soul Links, deren Tod der Spieler ausgelöst hat");
    }

    // ---------------------------------------------------------------- Embeds

    public static Embed RunOverview(Run run) => RunOverviewBuilder(run).Build();

    public static EmbedBuilder RunOverviewBuilder(Run run)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Status:** {RunStatusText(run.Status)} · gestartet {Timestamp(run.StartedAt)}"
                      + (run.EndedAt is { } end ? $" · beendet {Timestamp(end)}" : ""));
        if (!string.IsNullOrWhiteSpace(run.EndReason))
            sb.AppendLine($"**Grund:** {run.EndReason}");
        sb.AppendLine($"**Spieler:** {string.Join(", ", run.Participants.OrderBy(p => p.PlayerId).Select(p => p.Player.Name))}");
        sb.AppendLine();

        if (run.Links.Count == 0)
            sb.AppendLine("_Noch keine Encounter._");

        // Lebende zuerst, dann gebrochene, dann tote – jeweils in Fangreihenfolge
        foreach (var link in run.Links.OrderBy(l => l.Status).ThenBy(l => l.Id))
            sb.AppendLine(LinkLine(run, link));

        return new EmbedBuilder()
            .WithTitle($"Run #{run.Number}")
            .WithThumbnailUrl(ItemUrl("poke-ball"))
            .WithDescription(Truncate(sb.ToString()))
            .WithColor(run.Status switch
            {
                RunStatus.Aktiv => Green,
                RunStatus.Geschafft => Gold,
                RunStatus.Gescheitert => Red,
                _ => Grey
            })
            .WithFooter(Counts(run));
    }

    /// <param name="species">Pokémon, dessen Sprite als Bild gezeigt wird (meist das des Aufrufers).</param>
    public static Embed LinkEmbed(string title, Run run, RouteLink link, Color color, string? extra = null, string? species = null) =>
        LinkEmbedBuilder(title, run, link, color, extra, species).Build();

    public static EmbedBuilder LinkEmbedBuilder(string title, Run run, RouteLink link, Color color, string? extra = null, string? species = null)
    {
        var sb = new StringBuilder();
        foreach (var p in run.Participants.OrderBy(p => p.PlayerId))
        {
            var e = link.Encounters.FirstOrDefault(x => x.PlayerId == p.PlayerId);
            var text = EncounterText(e);
            if (link.Status == LinkStatus.Tot && e?.Status == EncounterStatus.Gefangen)
                text = $"~~{text}~~ 💀";
            sb.AppendLine($"**{p.Player.Name}:** {text}");
        }

        if (!string.IsNullOrWhiteSpace(extra))
            sb.AppendLine().AppendLine(extra);

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(Truncate(sb.ToString()))
            .WithColor(color)
            .WithFooter($"Run #{run.Number} · {Counts(run)}");

        if (SpriteUrl(species) is { } sprite) embed.WithThumbnailUrl(sprite);
        if (PokewikiUrl(species) is { } wiki) embed.WithUrl(wiki);

        return embed;
    }

    public static Embed GraveyardEmbed(Run run) => GraveyardEmbedBuilder(run).Build();

    public static EmbedBuilder GraveyardEmbedBuilder(Run run)
    {
        var dead = run.Links.Where(l => l.Status == LinkStatus.Tot).OrderBy(l => l.DiedAt).ToList();

        var sb = new StringBuilder();
        if (dead.Count == 0) sb.Append("_Noch niemand gestorben. Noch._");
        foreach (var link in dead)
        {
            sb.AppendLine($"💀 **{link.Route}** — {PartnerLine(run, link)}");
            sb.AppendLine($"  ↳ {link.DeathCause}" +
                          (link.CausedByPlayer is null ? "" : $" ({link.CausedByPlayer.Name})") +
                          (link.DiedAt is { } d ? $" · {Timestamp(d)}" : ""));
        }

        // Bild: das zuletzt gestorbene Pokémon des Auslösers
        var last = dead.LastOrDefault();
        var lastSpecies = last?.Encounters
            .FirstOrDefault(e => e.PlayerId == last.CausedByPlayerId && e.Status == EncounterStatus.Gefangen)?.Species;

        var embed = new EmbedBuilder()
            .WithTitle($"🪦 Friedhof – Run #{run.Number}")
            .WithDescription(Truncate(sb.ToString()))
            .WithColor(Red)
            .WithFooter($"{dead.Count} Soul Links gestorben");
        if (SpriteUrl(lastSpecies) is { } sprite) embed.WithThumbnailUrl(sprite);

        return embed;
    }

    // ---------------------------------------------------------------- Erfolge & Titel

    public static Embed AchievementAnnouncement(IReadOnlyList<UnlockedAchievement> unlocked) =>
        AchievementAnnouncementBuilder(unlocked).Build();

    public static EmbedBuilder AchievementAnnouncementBuilder(IReadOnlyList<UnlockedAchievement> unlocked)
    {
        var sb = new StringBuilder();
        foreach (var u in unlocked)
            sb.AppendLine($"{u.Achievement.Emoji} **{u.Player.Name}** – **{u.Achievement.Name}**\n  ↳ {u.Achievement.Description}");

        var embed = new EmbedBuilder()
            .WithTitle(unlocked.Count == 1 ? "🏅 Erfolg freigeschaltet!" : $"🏅 {unlocked.Count} Erfolge freigeschaltet!")
            .WithDescription(Truncate(sb.ToString()))
            .WithColor(Gold);

        // Großes Artwork, wenn der Erfolg zu einem Pokémon gehört (z. B. Legendär), sonst ein Item
        var species = unlocked.Select(u => u.Species).FirstOrDefault(x => x is not null);
        if (ArtworkUrl(species) is { } art) embed.WithThumbnailUrl(art);
        else embed.WithThumbnailUrl(ItemUrl("rare-candy"));

        return embed;
    }

    public static Embed PlayerAchievementsEmbed(
        Player player, string? avatarUrl,
        IReadOnlyList<(Achievement Achievement, DateTime UnlockedAt)> unlocked, IReadOnlyList<Achievement> locked)
    {
        var sb = new StringBuilder();
        foreach (var (a, at) in unlocked)
            sb.AppendLine($"{a.Emoji} **{a.Name}** – {a.Description} · {Timestamp(at)}");
        if (unlocked.Count > 0 && locked.Count > 0) sb.AppendLine();
        foreach (var a in locked)
            sb.AppendLine($"🔒 ~~{a.Name}~~ – {a.Description}");

        var embed = new EmbedBuilder()
            .WithTitle($"🏅 Erfolge von {player.Name} – {unlocked.Count}/{unlocked.Count + locked.Count}")
            .WithDescription(Truncate(sb.ToString()))
            .WithColor(Gold);
        if (avatarUrl is not null) embed.WithThumbnailUrl(avatarUrl);
        return embed.Build();
    }

    public static Embed TitlesEmbed(IReadOnlyList<PlayerTitle> titles, IReadOnlyList<(string Name, int Count)> counts, int total) =>
        TitlesEmbedBuilder(titles, counts, total).Build();

    public static EmbedBuilder TitlesEmbedBuilder(IReadOnlyList<PlayerTitle> titles, IReadOnlyList<(string Name, int Count)> counts,
        int total, bool includeTable = true)
    {
        var sb = new StringBuilder();
        foreach (var t in titles)
            sb.AppendLine($"{t.Emoji} **{t.Name}**: {t.Holders}" + (t.Value.Length > 0 ? $" ({t.Value})" : "") + $"\n  ↳ _{t.Description}_");

        var table = Table(["Spieler", "Erfolge"],
            counts.Select(c => new[] { c.Name.Length <= 14 ? c.Name : c.Name[..13] + "…", $"{c.Count}/{total}" }).ToList());

        return new EmbedBuilder()
            .WithTitle("👑 Titel & Erfolge")
            .WithDescription(Truncate(includeTable ? sb.ToString() + "\n" + table : sb.ToString()))
            .WithThumbnailUrl(ItemUrl("master-ball"))
            .WithColor(Gold)
            .WithFooter("Titel wandern zum aktuell Führenden · eigene Erfolge: /erfolge");
    }

    public static Embed MenuEmbed() => MenuEmbedBuilder().Build();

    public static EmbedBuilder MenuEmbedBuilder() =>
        new EmbedBuilder()
            .WithTitle("🎮 Nuzlocke Soul Link – Bedienfeld")
            .WithThumbnailUrl(ItemUrl("master-ball"))
            .WithColor(Green)
            .WithDescription(
                "**Im Run**\n" +
                "🎯 **Fang** – Route wählen, Pokémon eintragen\n" +
                "🟡 **Verpasst** – Encounter auf einer Route verpasst\n" +
                "💀 **Tod** – eigenes Pokémon gestorben, alle Partner sterben mit\n" +
                "✨ **Entwicklung** – eigenes Pokémon hat sich entwickelt\n\n" +
                "**Übersicht**\n" +
                "📋 **Status** – alle Routen und Pokémon des Runs\n" +
                "📊 **Tabelle** – Lebend/Gefangen/Tode/Verpasst pro Spieler\n" +
                "🪦 **Friedhof** – alle toten Soul Links\n" +
                "🏅 **Erfolge** – deine Erfolge und die aktuellen Titelträger\n" +
                "🪪 **Profil** – dein Trainerpass über alle Runs\n" +
                "📈 **Levelcap** – höchstes Level bis zur nächsten Arena\n\n" +
                "**Verwaltung**\n" +
                "➕ **Beitreten** · ▶️ **Run starten** · ⏹️ **Run beenden** · 🗺️ **Route erstellen**\n\n" +
                "_Alles geht auch per Slash-Command (`/fang`, `/tod`, `/tabelle global`, …). " +
                "Die Slash-Commands haben die volle Vorschlagsliste._");
}
