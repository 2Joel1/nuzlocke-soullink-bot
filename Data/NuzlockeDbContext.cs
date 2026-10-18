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
            """);
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
