namespace NuzlockeBot.Data;

/// <summary>
/// Alle 649 Pokémon, die es in Pokémon Schwarz gibt (Gen. 1–5, Nationaldex #1–649), mit deutschen Namen.
/// Index + 1 = Nationaldex-Nummer. Im Randomizer kann jedes davon auftauchen.
/// </summary>
public static class Pokedex
{
    public static readonly string[] Names =
    [
        "Bisasam", "Bisaknosp", "Bisaflor", "Glumanda", "Glutexo", "Glurak", "Schiggy", "Schillok",
        "Turtok", "Raupy", "Safcon", "Smettbo", "Hornliu", "Kokuna", "Bibor", "Taubsi",
        "Tauboga", "Tauboss", "Rattfratz", "Rattikarl", "Habitak", "Ibitak", "Rettan", "Arbok",
        "Pikachu", "Raichu", "Sandan", "Sandamer", "Nidoran♀", "Nidorina", "Nidoqueen", "Nidoran♂",
        "Nidorino", "Nidoking", "Piepi", "Pixi", "Vulpix", "Vulnona", "Pummeluff", "Knuddeluff",
        "Zubat", "Golbat", "Myrapla", "Duflor", "Giflor", "Paras", "Parasek", "Bluzuk",
        "Omot", "Digda", "Digdri", "Mauzi", "Snobilikat", "Enton", "Entoron", "Menki",
        "Rasaff", "Fukano", "Arkani", "Quapsel", "Quaputzi", "Quappo", "Abra", "Kadabra",
        "Simsala", "Machollo", "Maschock", "Machomei", "Knofensa", "Ultrigaria", "Sarzenia", "Tentacha",
        "Tentoxa", "Kleinstein", "Georok", "Geowaz", "Ponita", "Gallopa", "Flegmon", "Lahmus",
        "Magnetilo", "Magneton", "Porenta", "Dodu", "Dodri", "Jurob", "Jugong", "Sleima",
        "Sleimok", "Muschas", "Austos", "Nebulak", "Alpollo", "Gengar", "Onix", "Traumato",
        "Hypno", "Krabby", "Kingler", "Voltobal", "Lektrobal", "Owei", "Kokowei", "Tragosso",
        "Knogga", "Kicklee", "Nockchan", "Schlurp", "Smogon", "Smogmog", "Rihorn", "Rizeros",
        "Chaneira", "Tangela", "Kangama", "Seeper", "Seemon", "Goldini", "Golking", "Sterndu",
        "Starmie", "Pantimos", "Sichlor", "Rossana", "Elektek", "Magmar", "Pinsir", "Tauros",
        "Karpador", "Garados", "Lapras", "Ditto", "Evoli", "Aquana", "Blitza", "Flamara",
        "Porygon", "Amonitas", "Amoroso", "Kabuto", "Kabutops", "Aerodactyl", "Relaxo", "Arktos",
        "Zapdos", "Lavados", "Dratini", "Dragonir", "Dragoran", "Mewtu", "Mew", "Endivie",
        "Lorblatt", "Meganie", "Feurigel", "Igelavar", "Tornupto", "Karnimani", "Tyracroc", "Impergator",
        "Wiesor", "Wiesenior", "Hoothoot", "Noctuh", "Ledyba", "Ledian", "Webarak", "Ariados",
        "Iksbat", "Lampi", "Lanturn", "Pichu", "Pii", "Fluffeluff", "Togepi", "Togetic",
        "Natu", "Xatu", "Voltilamm", "Waaty", "Ampharos", "Blubella", "Marill", "Azumarill",
        "Mogelbaum", "Quaxo", "Hoppspross", "Hubelupf", "Papungha", "Griffel", "Sonnkern", "Sonnflora",
        "Yanma", "Felino", "Morlord", "Psiana", "Nachtara", "Kramurx", "Laschoking", "Traunfugil",
        "Icognito", "Woingenau", "Girafarig", "Tannza", "Forstellka", "Dummisel", "Skorgla", "Stahlos",
        "Snubbull", "Granbull", "Baldorfish", "Scherox", "Pottrott", "Skaraborn", "Sniebel", "Teddiursa",
        "Ursaring", "Schneckmag", "Magcargo", "Quiekel", "Keifel", "Corasonn", "Remoraid", "Octillery",
        "Botogel", "Mantax", "Panzaeron", "Hunduster", "Hundemon", "Seedraking", "Phanpy", "Donphan",
        "Porygon2", "Damhirplex", "Farbeagle", "Rabauz", "Kapoera", "Kussilla", "Elekid", "Magby",
        "Miltank", "Heiteira", "Raikou", "Entei", "Suicune", "Larvitar", "Pupitar", "Despotar",
        "Lugia", "Ho-Oh", "Celebi", "Geckarbor", "Reptain", "Gewaldro", "Flemmli", "Jungglut",
        "Lohgock", "Hydropi", "Moorabbel", "Sumpex", "Fiffyen", "Magnayen", "Zigzachs", "Geradaks",
        "Waumpel", "Schaloko", "Papinella", "Panekon", "Pudox", "Loturzel", "Lombrero", "Kappalores",
        "Samurzel", "Blanas", "Tengulist", "Schwalbini", "Schwalboss", "Wingull", "Pelipper", "Trasla",
        "Kirlia", "Guardevoir", "Gehweiher", "Maskeregen", "Knilz", "Kapilz", "Bummelz", "Muntier",
        "Letarking", "Nincada", "Ninjask", "Ninjatom", "Flurmel", "Krakeelo", "Krawumms", "Makuhita",
        "Hariyama", "Azurill", "Nasgnet", "Eneco", "Enekoro", "Zobiris", "Flunkifer", "Stollunior",
        "Stollrak", "Stolloss", "Meditie", "Meditalis", "Frizelbliz", "Voltenso", "Plusle", "Minun",
        "Volbeat", "Illumise", "Roselia", "Schluppuck", "Schlukwech", "Kanivanha", "Tohaido", "Wailmer",
        "Wailord", "Camaub", "Camerupt", "Qurtel", "Spoink", "Groink", "Pandir", "Knacklion",
        "Vibrava", "Libelldra", "Tuska", "Noktuska", "Wablu", "Altaria", "Sengo", "Vipitis",
        "Lunastein", "Sonnfel", "Schmerbe", "Welsar", "Krebscorps", "Krebutack", "Puppance", "Lepumentas",
        "Liliep", "Wielie", "Anorith", "Armaldo", "Barschwa", "Milotic", "Formeo", "Kecleon",
        "Shuppet", "Banette", "Zwirrlicht", "Zwirrklop", "Tropius", "Palimpalim", "Absol", "Isso",
        "Schneppke", "Firnontor", "Seemops", "Seejong", "Walraisa", "Perlu", "Aalabyss", "Saganabyss",
        "Relicanth", "Liebiskus", "Kindwurm", "Draschel", "Brutalanda", "Tanhel", "Metang", "Metagross",
        "Regirock", "Regice", "Registeel", "Latias", "Latios", "Kyogre", "Groudon", "Rayquaza",
        "Jirachi", "Deoxys", "Chelast", "Chelcarain", "Chelterrar", "Panflam", "Panpyro", "Panferno",
        "Plinfa", "Pliprin", "Impoleon", "Staralili", "Staravia", "Staraptor", "Bidiza", "Bidifas",
        "Zirpurze", "Zirpeise", "Sheinux", "Luxio", "Luxtra", "Knospi", "Roserade", "Koknodon",
        "Rameidon", "Schilterus", "Bollterus", "Burmy", "Burmadame", "Moterpel", "Wadribie", "Honweisel",
        "Pachirisu", "Bamelin", "Bojelin", "Kikugi", "Kinoso", "Schalellos", "Gastrodon", "Ambidiffel",
        "Driftlon", "Drifzepeli", "Haspiror", "Schlapor", "Traunmagil", "Kramshef", "Charmian", "Shnurgarst",
        "Klingplim", "Skunkapuh", "Skuntank", "Bronzel", "Bronzong", "Mobai", "Pantimimi", "Wonneira",
        "Plaudagei", "Kryppuk", "Kaumalat", "Knarksel", "Knakrack", "Mampfaxo", "Riolu", "Lucario",
        "Hippopotas", "Hippoterus", "Pionskora", "Piondragi", "Glibunkel", "Toxiquak", "Venuflibis", "Finneon",
        "Lumineon", "Mantirps", "Shnebedeck", "Rexblisar", "Snibunna", "Magnezone", "Schlurplek", "Rihornior",
        "Tangoloss", "Elevoltek", "Magbrant", "Togekiss", "Yanmega", "Folipurba", "Glaziola", "Skorgro",
        "Mamutel", "Porygon-Z", "Galagladi", "Voluminas", "Zwirrfinst", "Frosdedje", "Rotom", "Selfe",
        "Vesprit", "Tobutz", "Dialga", "Palkia", "Heatran", "Regigigas", "Giratina", "Cresselia",
        "Phione", "Manaphy", "Darkrai", "Shaymin", "Arceus", "Victini", "Serpifeu", "Efoserp",
        "Serpiroyal", "Floink", "Ferkokel", "Flambirex", "Ottaro", "Zwottronin", "Admurai", "Nagelotz",
        "Kukmarda", "Yorkleff", "Terribark", "Bissbark", "Felilou", "Kleoparda", "Vegimak", "Vegichita",
        "Grillmak", "Grillchita", "Sodamak", "Sodachita", "Somniam", "Somnivora", "Dusselgurr", "Navitaub",
        "Fasasnob", "Elezeba", "Zebritz", "Kiesling", "Sedimantur", "Brockoloss", "Fleknoil", "Fletiamo",
        "Rotomurf", "Stalobor", "Ohrdoch", "Praktibalk", "Strepoli", "Meistagrif", "Schallquap", "Mebrana",
        "Branawarz", "Jiutesto", "Karadonis", "Strawickl", "Folikon", "Matrifol", "Toxiped", "Rollum",
        "Cerapendra", "Waumboll", "Elfun", "Lilminip", "Dressella", "Barschuft", "Ganovil", "Rokkaiman",
        "Rabigator", "Flampion", "Flampivian", "Maracamba", "Lithomith", "Castellith", "Zurrokex", "Irokex",
        "Symvolara", "Makabaja", "Echnatoll", "Galapaflos", "Karippas", "Flapteryx", "Aeropteryx", "Unratütox",
        "Deponitox", "Zorua", "Zoroark", "Picochilla", "Chillabell", "Mollimorba", "Hypnomorba", "Morbitesse",
        "Monozyto", "Mitodos", "Zytomega", "Piccolente", "Swaroness", "Gelatini", "Gelatroppo", "Gelatwino",
        "Sesokitz", "Kronjuwild", "Emolga", "Laukaps", "Cavalanzas", "Tarnpignon", "Hutsassa", "Quabbel",
        "Apoquallyp", "Mamolida", "Wattzapf", "Voltula", "Kastadur", "Tentantel", "Klikk", "Kliklak",
        "Klikdiklak", "Zapplardin", "Zapplalek", "Zapplarang", "Pygraulon", "Megalon", "Lichtel", "Laternecto",
        "Skelabra", "Milza", "Sharfax", "Maxax", "Petznief", "Siberio", "Frigometri", "Schnuthelm",
        "Hydragil", "Flunschlik", "Fu", "Shu", "Shardrago", "Golbit", "Golgantes", "Gladiantri",
        "Caesurio", "Bisofank", "Geronimatz", "Washakwil", "Skallyk", "Grypheldis", "Furnifraß", "Fermicula",
        "Kapuno", "Duodino", "Trikephalo", "Ignivor", "Ramoth", "Kobalium", "Terrakium", "Viridium",
        "Boreos", "Voltolos", "Reshiram", "Zekrom", "Demeteros", "Kyurem", "Keldeo", "Meloetta",
        "Genesect",
    ];

    private static readonly Dictionary<string, int> ByName =
        Names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i + 1, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Liefert den korrekt geschriebenen Namen (Eingabe als Name oder Dex-Nummer, z. B. "98" oder "#098"),
    /// oder null, wenn es das Pokémon in Schwarz nicht gibt.
    /// </summary>
    public static string? Find(string input)
    {
        var text = input.Trim().TrimStart('#');
        if (int.TryParse(text, out var number))
            return number is >= 1 and <= 649 ? Names[number - 1] : null;
        return ByName.TryGetValue(text, out var nr) ? Names[nr - 1] : null;
    }

    /// <summary>Die ähnlichsten Namen (für "Meintest du ...?" bei Vertippern).</summary>
    public static IEnumerable<string> Suggest(string input, int count = 3)
    {
        var text = input.Trim().ToLowerInvariant();
        return Names
            .OrderBy(n => Distance(text, n.ToLowerInvariant()))
            .Take(count);
    }

    /// <summary>Levenshtein-Distanz.</summary>
    private static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Legendäre und mysteriöse Pokémon bis Gen. 5 (für den Erfolg "Legendär").</summary>
    public static readonly HashSet<int> Legendaries =
    [
        144, 145, 146, 150, 151,
        243, 244, 245, 249, 250, 251,
        377, 378, 379, 380, 381, 382, 383, 384, 385, 386,
        480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493, 494,
        638, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649
    ];

    public static int? Number(string name) =>
        ByName.TryGetValue(name.Trim(), out var nr) ? nr : null;
}
