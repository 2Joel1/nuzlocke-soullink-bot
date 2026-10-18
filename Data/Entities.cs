namespace NuzlockeBot.Data;

public enum RunStatus
{
    Aktiv,
    Gescheitert,
    Geschafft,
    Abgebrochen
}

/// <summary>Zustand eines Soul Links (= alle Pokémon einer Route über alle Spieler).</summary>
public enum LinkStatus
{
    /// <summary>Alle gefangenen Partner sind nutzbar.</summary>
    Lebendig,
    /// <summary>Mindestens ein Spieler hat den Encounter verpasst – die Partner sind nicht nutzbar.</summary>
    Gebrochen,
    /// <summary>Ein Pokémon ist gestorben – damit sind alle Partner tot.</summary>
    Tot
}

public enum EncounterStatus
{
    Gefangen,
    Verpasst
}

/// <summary>Ein Discord-Nutzer, der bei der Challenge mitspielt (pro Server).</summary>
public class Player
{
    public int Id { get; set; }
    public long GuildId { get; set; }
    public long DiscordUserId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Nur aktive Spieler werden beim nächsten /run start übernommen.</summary>
    public bool Active { get; set; } = true;
}

public class Run
{
    public int Id { get; set; }
    public long GuildId { get; set; }
    /// <summary>Fortlaufende Nummer pro Server (Run #1, #2, ...).</summary>
    public int Number { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Aktiv;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }

    public List<RunParticipant> Participants { get; set; } = [];
    public List<RouteLink> Links { get; set; } = [];
}

public class RunParticipant
{
    public int RunId { get; set; }
    public Run Run { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
}

/// <summary>
/// Soul Link einer Route innerhalb eines Runs. Fasst die Encounter aller Spieler
/// auf dieser Route zusammen. Stirbt eines, ist der ganze Link tot.
/// </summary>
public class RouteLink
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public Run Run { get; set; } = null!;

    /// <summary>Anzeigename, z. B. "Route 3".</summary>
    public string Route { get; set; } = "";
    /// <summary>Normalisierter Schlüssel für Vergleiche (klein, ohne doppelte Leerzeichen).</summary>
    public string RouteKey { get; set; } = "";

    public LinkStatus Status { get; set; } = LinkStatus.Lebendig;
    public DateTime? DiedAt { get; set; }
    public string? DeathCause { get; set; }
    public int? CausedByPlayerId { get; set; }
    public Player? CausedByPlayer { get; set; }

    public List<Encounter> Encounters { get; set; } = [];
}

/// <summary>Eigene Route/Ort pro Server (z. B. Static-Pokémon), gilt für alle Runs.</summary>
public class CustomRoute
{
    public int Id { get; set; }
    public long GuildId { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Die Live-Statusnachricht eines Servers (eine pro Server).</summary>
public class LiveMessage
{
    public long GuildId { get; set; }
    public long ChannelId { get; set; }
    public long MessageId { get; set; }
}

/// <summary>Freigeschalteter Erfolg eines Spielers (einmalig, mit Datum).</summary>
public class PlayerAchievement
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public string Key { get; set; } = "";
    public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;
}

public class Encounter
{
    public int Id { get; set; }
    public int RouteLinkId { get; set; }
    public RouteLink RouteLink { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public string Species { get; set; } = "";
    public string? Nickname { get; set; }
    public EncounterStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
