using Microsoft.EntityFrameworkCore;

namespace NuzlockeBot.Data;

public class NuzlockeDbContext(DbContextOptions<NuzlockeDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunParticipant> RunParticipants => Set<RunParticipant>();
    public DbSet<RouteLink> RouteLinks => Set<RouteLink>();
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<CustomRoute> CustomRoutes => Set<CustomRoute>();
    public DbSet<PlayerAchievement> PlayerAchievements => Set<PlayerAchievement>();
    public DbSet<LiveMessage> LiveMessages => Set<LiveMessage>();
    public DbSet<ArenaClear> ArenaClears => Set<ArenaClear>();

    /// <summary>
    /// Legt Tabellen an, die nach dem ersten Start dazugekommen sind.
    /// EnsureCreated() ergänzt bei einer bestehenden Datenbank nichts mehr.
    /// </summary>
    public async Task UpgradeSchemaAsync()
    {
        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CustomRoutes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomRoutes" PRIMARY KEY AUTOINCREMENT,
                "GuildId" INTEGER NOT NULL,
                "Name" TEXT NOT NULL,
                "NameKey" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CustomRoutes_GuildId_NameKey" ON "CustomRoutes" ("GuildId", "NameKey");

            CREATE TABLE IF NOT EXISTS "PlayerAchievements" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_PlayerAchievements" PRIMARY KEY AUTOINCREMENT,
                "PlayerId" INTEGER NOT NULL,
                "Key" TEXT NOT NULL,
                "UnlockedAt" TEXT NOT NULL,
                CONSTRAINT "FK_PlayerAchievements_Players_PlayerId" FOREIGN KEY ("PlayerId") REFERENCES "Players" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlayerAchievements_PlayerId_Key" ON "PlayerAchievements" ("PlayerId", "Key");

            CREATE TABLE IF NOT EXISTS "ArenaClears" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ArenaClears" PRIMARY KEY AUTOINCREMENT,
                "RunId" INTEGER NOT NULL,
                "Nr" INTEGER NOT NULL,
                "ClearedAt" TEXT NOT NULL,
                CONSTRAINT "FK_ArenaClears_Runs_RunId" FOREIGN KEY ("RunId") REFERENCES "Runs" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ArenaClears_RunId_Nr" ON "ArenaClears" ("RunId", "Nr");

            CREATE TABLE IF NOT EXISTS "LiveMessages" (
                "GuildId" INTEGER NOT NULL CONSTRAINT "PK_LiveMessages" PRIMARY KEY,
                "ChannelId" INTEGER NOT NULL,
                "MessageId" INTEGER NOT NULL
            );
            """);

        // Neue Spalten bei bestehender Datenbank ergänzen
        await AddColumnIfMissingAsync("Runs", "Progress", "INTEGER NOT NULL DEFAULT 0");
    }

    private async Task AddColumnIfMissingAsync(string table, string column, string definition)
    {
        await Database.OpenConnectionAsync();
        try
        {
            await using var cmd = Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
            var exists = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
            if (!exists)
            {
                var sql = "ALTER TABLE \"" + table + "\" ADD COLUMN \"" + column + "\" " + definition;   // feste Werte aus dem Code
                await Database.ExecuteSqlRawAsync(sql);
            }
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Player>()
            .HasIndex(p => new { p.GuildId, p.DiscordUserId })
            .IsUnique();

        b.Entity<Run>()
            .HasIndex(r => new { r.GuildId, r.Number })
            .IsUnique();

        b.Entity<RunParticipant>()
            .HasKey(rp => new { rp.RunId, rp.PlayerId });

        // Pro Run darf jede Route nur einen Soul Link haben
        b.Entity<RouteLink>()
            .HasIndex(l => new { l.RunId, l.RouteKey })
            .IsUnique();

        b.Entity<RouteLink>()
            .HasOne(l => l.CausedByPlayer)
            .WithMany()
            .HasForeignKey(l => l.CausedByPlayerId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<ArenaClear>()
            .HasIndex(a => new { a.RunId, a.Nr })
            .IsUnique();

        b.Entity<LiveMessage>().HasKey(m => m.GuildId);
        b.Entity<LiveMessage>().Property(m => m.GuildId).ValueGeneratedNever();

        b.Entity<PlayerAchievement>()
            .HasIndex(a => new { a.PlayerId, a.Key })
            .IsUnique();

        b.Entity<CustomRoute>()
            .HasIndex(r => new { r.GuildId, r.NameKey })
            .IsUnique();

        // Pro Route und Spieler nur ein Encounter (Nuzlocke-Grundregel)
        b.Entity<Encounter>()
            .HasIndex(e => new { e.RouteLinkId, e.PlayerId })
            .IsUnique();
    }
}
