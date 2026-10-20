using System.Collections.Concurrent;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NuzlockeBot.Data;

namespace NuzlockeBot.Services;

/// <summary>
/// Hält pro Server eine angepinnte Nachricht mit Board und Tabelle des aktuellen Runs aktuell.
/// Hört auf <see cref="RunService.RunChanged"/> und bearbeitet die Nachricht, statt neue zu posten.
/// Mehrere Änderungen kurz hintereinander werden zu einer Aktualisierung zusammengefasst.
/// </summary>
public class LiveStatusService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);

    private readonly DiscordSocketClient _client;
    private readonly RunService _runs;
    private readonly CardFactory _cards;
    private readonly IDbContextFactory<NuzlockeDbContext> _dbFactory;

    private sealed class GuildState
    {
        public bool Dirty;
        public bool Running;
    }

    private readonly ConcurrentDictionary<ulong, GuildState> _states = new();

    public LiveStatusService(DiscordSocketClient client, RunService runs, CardFactory cards,
        IDbContextFactory<NuzlockeDbContext> dbFactory)
    {
        _client = client;
        _runs = runs;
        _cards = cards;
        _dbFactory = dbFactory;

        _runs.RunChanged += RequestUpdate;
    }

    // ---------------------------------------------------------------- Einrichten / Entfernen

    /// <summary>Postet die Live-Nachricht in den Kanal (eine evtl. alte wird gelöscht) und pinnt sie an.</summary>
    public async Task<string?> SetupAsync(ulong guildId, ITextChannel channel)
    {
        await RemoveAsync(guildId);

        var (embeds, files) = await BuildAsync(guildId);
        IUserMessage message;
        try
        {
            message = await channel.SendFilesAsync(files, embeds: embeds);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions || ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new NuzlockeException("Mir fehlen in diesem Kanal Rechte. Ich brauche: **Kanal ansehen**, **Nachrichten senden**, " +
                                        "**Links einbetten**, **Dateien anhängen** und **Nachrichtenverlauf lesen**.");
        }
        finally
        {
            foreach (var file in files) file.Dispose();
        }

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.LiveMessages.Add(new LiveMessage
            {
                GuildId = (long)guildId,
                ChannelId = (long)channel.Id,
                MessageId = (long)message.Id
            });
            await db.SaveChangesAsync();
        }

        // Anpinnen ist optional – braucht das Recht "Nachrichten verwalten" bzw. "Nachrichten anpinnen"
        try
        {
            await message.PinAsync();
            return null;
        }
        catch (Exception)
        {
            return "Ich konnte die Nachricht nicht anpinnen (fehlendes Recht). Du kannst sie einfach selbst anpinnen.";
        }
    }

    /// <summary>Löscht die Live-Nachricht (falls noch vorhanden) und vergisst sie.</summary>
    public async Task<bool> RemoveAsync(ulong guildId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var live = await db.LiveMessages.FindAsync((long)guildId);
        if (live is null) return false;

        var message = await GetMessageAsync(live);
        if (message is not null)
        {
            try { await message.DeleteAsync(); }
            catch (Exception ex) { Console.WriteLine($"[Live] Alte Nachricht nicht löschbar: {ex.Message}"); }
        }

        db.LiveMessages.Remove(live);
        await db.SaveChangesAsync();
        return true;
    }

    // ---------------------------------------------------------------- Aktualisieren

    /// <summary>
    /// Fordert eine Aktualisierung an. Kommen innerhalb von 2 Sekunden weitere Anfragen,
    /// wird nur einmal aktualisiert – das schont das Rate-Limit von Discord.
    /// </summary>
    public void RequestUpdate(ulong guildId)
    {
        var state = _states.GetOrAdd(guildId, _ => new GuildState());
        lock (state)
        {
            state.Dirty = true;
            if (state.Running) return;
            state.Running = true;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(Debounce);
                lock (state)
                {
                    if (!state.Dirty)
                    {
                        state.Running = false;
                        return;
                    }
                    state.Dirty = false;
                }

                try
                {
                    await UpdateAsync(guildId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Live] Aktualisierung fehlgeschlagen: {ex.Message}");
                }
            }
        });
    }

    private async Task UpdateAsync(ulong guildId)
    {
        LiveMessage? live;
        await using (var db = await _dbFactory.CreateDbContextAsync())
            live = await db.LiveMessages.FindAsync((long)guildId);
        if (live is null) return; // keine Live-Nachricht eingerichtet

        if (_client.GetChannel((ulong)live.ChannelId) is not ITextChannel channel)
            return;

        var (embeds, files) = await BuildAsync(guildId);
        try
        {
            var message = await GetMessageAsync(live);
            if (message is not null)
            {
                await message.ModifyAsync(m =>
                {
                    m.Content = "";
                    m.Embeds = embeds;
                    m.Attachments = files;
                });
                return;
            }

            // Nachricht wurde gelöscht → neu posten und merken
            var fresh = await channel.SendFilesAsync(files, embeds: embeds);
            try { await fresh.PinAsync(); } catch { /* optional */ }

            await using var db = await _dbFactory.CreateDbContextAsync();
            var row = await db.LiveMessages.FindAsync((long)guildId);
            if (row is not null)
            {
                row.MessageId = (long)fresh.Id;
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            foreach (var file in files) file.Dispose();
        }
    }

    // ---------------------------------------------------------------- Inhalt

    /// <summary>Board + Tabelle des aktuellen (bzw. letzten) Runs.</summary>
    private async Task<(Embed[] Embeds, List<FileAttachment> Files)> BuildAsync(ulong guildId)
    {
        Run run;
        try
        {
            run = await _runs.GetRunAsync(guildId, null);
        }
        catch (NuzlockeException)
        {
            var empty = new EmbedBuilder()
                .WithTitle("📡 Live-Status")
                .WithDescription("Noch kein Run. Sobald ihr mit `/run start` loslegt, erscheint hier der aktuelle Stand.")
                .WithColor(Formatting.Grey)
                .WithCurrentTimestamp();
            return ([empty.Build()], []);
        }

        var board = await _cards.StatusAsync(run, "live-board.png");
        board.Embed
            .WithTitle($"📡 Live-Status – Run #{run.Number}")
            .WithFooter($"{Formatting.Counts(run)} · aktualisiert")
            .WithCurrentTimestamp();

        var stats = RunService.ComputeStats([run], aliveIn: run);
        var table = await _cards.StatsAsync($"📊 Tabelle – Run #{run.Number}", stats, global: false,
            run.Status == RunStatus.Aktiv ? Tone.Green : Tone.Grey, "live-tabelle.png",
            $"RUN {run.Number}", CardFactory.StatusWord(run.Status));

        return new[] { board, table }.Prepare();
    }

    private async Task<IUserMessage?> GetMessageAsync(LiveMessage live)
    {
        if (_client.GetChannel((ulong)live.ChannelId) is not ITextChannel channel) return null;
        try
        {
            return await channel.GetMessageAsync((ulong)live.MessageId) as IUserMessage;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
