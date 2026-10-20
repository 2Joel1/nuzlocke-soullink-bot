namespace NuzlockeBot.Data;

/// <summary>Ein Pflichtkampf der Story mit dem Level des stärksten Pokémon (= Levelcap).</summary>
/// <param name="Trainer">Anzeigename, z. B. "Arenaleiterin Aloe".</param>
/// <param name="Short">Kurzname für Bilder, z. B. "ALOE".</param>
/// <param name="Badge">Ordenname, bei Liga/Team Plasma null.</param>
public record BossFight(int Nr, string Trainer, string Short, string Place, string Type, string? Badge, int Level);

/// <summary>
/// Levelcaps in Pokémon Schwarz: höchstes Level im Team des jeweiligen Gegners (Originalspiel).
/// Im Randomizer sind die Pokémon andere, die Level bleiben aber normalerweise gleich.
/// Quellen: bisafans.de (Arenaleiter &amp; Top Vier), pokemondb.net (Black/White Gym Leaders &amp; Elite Four).
/// </summary>
public static class LevelCaps
{
    public static readonly IReadOnlyList<BossFight> Schwarz =
    [
        new(1, "Arenaleiter Benny/Maik/Colin", "BENNY/MAIK/COLIN", "Orion City", "Pflanze/Feuer/Wasser", "Triorden", 14),
        new(2, "Arenaleiterin Aloe", "ALOE", "Septerna City", "Normal", "Grundorden", 20),
        new(3, "Arenaleiter Artie", "ARTIE", "Stratos City", "Käfer", "Käferorden", 23),
        new(4, "Arenaleiterin Kamilla", "KAMILLA", "Rayono City", "Elektro", "Voltorden", 27),
        new(5, "Arenaleiter Turner", "TURNER", "Marea City", "Boden", "Seismoorden", 31),
        new(6, "Arenaleiterin Géraldine", "GÉRALDINE", "Panaero City", "Flug", "Jetorden", 35),
        new(7, "Arenaleiter Sandro", "SANDRO", "Nevaio City", "Eis", "Eiszapfenorden", 39),
        new(8, "Arenaleiter Lysander", "LYSANDER", "Twindrake City", "Drache", "Legendenorden", 43),
        new(9, "Top Vier", "TOP VIER", "Pokémon-Liga", "Geist, Unlicht, Psycho, Kampf", null, 50),
        new(10, "N", "N", "N's Schloss", "Gemischt", null, 52),
        new(11, "G-Cis", "G-CIS", "N's Schloss", "Gemischt", null, 54),
    ];

    /// <summary>Nächster Kampf bei einem Fortschritt (Anzahl geschaffter Kämpfe), null wenn alles geschafft ist.</summary>
    public static BossFight? Next(int progress) =>
        progress >= 0 && progress < Schwarz.Count ? Schwarz[progress] : null;
}
