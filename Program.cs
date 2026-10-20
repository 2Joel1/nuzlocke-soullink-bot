using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NuzlockeBot.Data;
using NuzlockeBot.Services;

// --- Konfiguration über Umgebungsvariablen ---
var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN")
    ?? throw new InvalidOperationException("Umgebungsvariable DISCORD_TOKEN fehlt.");
ulong? devGuildId = ulong.TryParse(Environment.GetEnvironmentVariable("DISCORD_GUILD_ID"), out var gid) ? gid : null;
var dbPath = Environment.GetEnvironmentVariable("NUZLOCKE_DB") ?? "nuzlocke.db";

// --- Dependency Injection ---
var services = new ServiceCollection()
    .AddSingleton(new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds })
    .AddSingleton<DiscordSocketClient>()
    .AddSingleton(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>()))
    .AddDbContextFactory<NuzlockeDbContext>(o => o.UseSqlite($"Data Source={dbPath}"))
    .AddSingleton<RunService>()
    .AddSingleton<AchievementService>()
    .AddSingleton<ImageRenderer>()
    .AddSingleton<CardFactory>()
    .AddSingleton<LiveStatusService>()
    .AddSingleton<HighlightService>()
    .BuildServiceProvider();

// --- Datenbank anlegen (falls noch nicht vorhanden) ---
await using (var db = await services.GetRequiredService<IDbContextFactory<NuzlockeDbContext>>().CreateDbContextAsync())
{
    await db.Database.EnsureCreatedAsync();
    await db.UpgradeSchemaAsync(); // neue Tabellen bei bestehender nuzlocke.db nachziehen
}

var client = services.GetRequiredService<DiscordSocketClient>();
services.GetRequiredService<LiveStatusService>(); // erzeugen, damit er sich für Run-Änderungen anmeldet
var interactions = services.GetRequiredService<InteractionService>();

client.Log += LogAsync;
interactions.Log += LogAsync;

// Module einmalig laden (Ready kann bei Reconnects mehrfach feuern)
await interactions.AddModulesAsync(typeof(Program).Assembly, services);

client.Ready += async () =>
{
    if (devGuildId is { } guildId)
        await interactions.RegisterCommandsToGuildAsync(guildId); // sofort sichtbar
    else
        await interactions.RegisterCommandsGloballyAsync();
};

client.InteractionCreated += async interaction =>
{
    var ctx = new SocketInteractionContext(client, interaction);
    await interactions.ExecuteCommandAsync(ctx, services);
};

// Unerwartete Fehler (Bugs, DB-Probleme) sauber an den Nutzer melden – für Commands, Buttons und Formulare
interactions.SlashCommandExecuted += (_, ctx, result) => ReportErrorAsync(ctx, result);
interactions.ComponentCommandExecuted += (_, ctx, result) => ReportErrorAsync(ctx, result);
interactions.ModalCommandExecuted += (_, ctx, result) => ReportErrorAsync(ctx, result);

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();
await Task.Delay(Timeout.Infinite);

static async Task ReportErrorAsync(IInteractionContext ctx, IResult result)
{
    if (result.IsSuccess) return;

    Console.WriteLine($"[Fehler] {result.Error}: {result.ErrorReason}");
    var text = $"❌ Da ist etwas schiefgelaufen: {result.ErrorReason}";
    if (ctx.Interaction.HasResponded)
        await ctx.Interaction.FollowupAsync(text, ephemeral: true);
    else
        await ctx.Interaction.RespondAsync(text, ephemeral: true);
}

static Task LogAsync(LogMessage msg)
{
    Console.WriteLine(msg.ToString());
    return Task.CompletedTask;
}
