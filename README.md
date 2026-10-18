# Nuzlocke Soul-Link Bot (Pokémon Schwarz)

Discord-Bot zum Dokumentieren einer Soul-Link-Nuzlocke-Randomizer-Challenge.
Jeder spielt seinen eigenen Spielstand, die Pokémon einer Route sind aber über alle Spieler verknüpft:
**Stirbt eins, sterben alle Partner auf dieser Route.**

## Regeln, die der Bot abbildet

| Situation | Was passiert |
|---|---|
| `/fang` | Pro Spieler und Route nur ein Encounter. Auf toten Routen geht nichts mehr. |
| `/verpasst` | Der Soul Link der Route ist **gebrochen** 🟡 – gefangene Partner sind nicht nutzbar. |
| `/tod` | Der ganze Soul Link ist **tot** 💀 – bei allen Spielern. Ursache und Auslöser werden gespeichert. |
| `/run ende` | Run ist abgeschlossen und zählt als verbraucht. Neuer Versuch mit `/run start`. |

## Bedienfeld

`/menu` postet eine Nachricht mit Buttons für alles – am besten in einem eigenen Kanal posten und anpinnen.
Die Buttons funktionieren dauerhaft, auch nach einem Neustart des Bots.

| Button | Ablauf |
|---|---|
| 🎯 Fang | Route aus Liste wählen → Formular mit Pokémon (Name oder Dex-Nr.) und Spitzname |
| 🟡 Verpasst | Route aus Liste wählen → fertig |
| 💀 Tod | Eigenes lebendes Pokémon wählen → Formular mit Ursache |
| 📋 Status · 📊 Tabelle · 🪦 Friedhof · 🏅 Erfolge | Übersicht, nur für dich sichtbar |
| ➕ Beitreten · ▶️ Run starten · ⏹️ Run beenden · 🗺️ Route erstellen | Verwaltung |

Ergebnisse (Fang, Tod, …) landen wie bei den Slash-Commands öffentlich im Kanal. Discord-Auswahllisten
zeigen max. 25 Einträge – für spätere Routen gibt es zusätzlich die Slash-Commands mit Suche.

## Erfolge & Titel

Erfolge werden nach jeder Aktion geprüft und im Kanal verkündet. Einmal freigeschaltet, bleiben sie.

| Erfolg | Bedingung |
|---|---|
| 🎣 Erster Fang · 📦 Hamster · 📕 Pokédex-Forscher | 1 · 25 · 100 Fänge |
| 🐉 Legendär | Legendäres/mysteriöses Pokémon gefangen |
| 🌱 Starter-Sammler | Starter einer beliebigen Generation gefangen (nicht am Start) |
| 🗺️ Entdecker | Fang auf einer eigenen Route |
| 👥 Volles Team | 6 lebende Soul Links gleichzeitig |
| ⚰️ Willkommen im Club · 🪦 Totengräber | 1 · 10 ausgelöste Tode |
| ⚡ Blitzkrieg | Tod auf einer der ersten 3 Routen eines Runs ausgelöst |
| 💥 Wipe-Verursacher | Letzten Tod eines gescheiterten Runs ausgelöst |
| 🏃 Fluchtmagnet | 3 verpasste Encounter in einem Run |
| 🧊 Eiskalt | Run mit mind. 8 Routen beendet, ohne selbst einen Tod auszulösen |
| 🔁 Stammgast · 🧱 Unkaputtbar | 5 · 10 Runs gespielt |
| 🏆 Champion | Run geschafft |

**Titel** gehören immer dem aktuell Führenden: 💀 Pechvogel (meiste Tode), 🎣 Meisterfänger (meiste Fänge),
🙈 Grobmotoriker (meiste verpasst), 🛡️ Schutzengel (wenigste Tode pro Fang, ab 5 Fängen), 🏅 Trophäenjäger (meiste Erfolge).

Eigene Erfolge ergänzen: `AchievementService.All` um eine Zeile erweitern.

## Bilder

Der Bot zeichnet die Bilder selbst (`Services/ImageRenderer.cs`, mit ImageSharp – läuft auch auf dem Raspberry Pi):

| Nachricht | Bild |
|---|---|
| Fang · Verpasst · Tod | Banner mit Route und den Sprites aller Soul-Link-Partner (bei Tod in Grau) |
| Run start · Run ende | Banner, beim Ende zusätzlich das Board des Runs |
| `/status` · `/friedhof` | Board: jede Route eine Zeile, pro Spieler Sprite + Name |
| `/tabelle run` · `/tabelle global` · `/titel` | Große Tabelle in Farbe |
| Erfolg freigeschaltet · `/menu` | Banner |

- Sprites: animierte bzw. statische Schwarz/Weiß-Sprites von PokeAPI auf GitHub, werden nach dem ersten Laden zwischengespeichert.
- Schrift: DejaVu Sans in `Assets/Fonts` (freie Lizenz, liegt bei).
- Kann ein Bild nicht erzeugt werden (z. B. kein Internet), kommt die Nachricht wie früher als reiner Text.
- Farben und Größen stehen oben in `ImageRenderer.cs`.
- ImageSharp ist für private Projekte kostenlos (Six Labors Split License).

## Commands

| Command | Zweck |
|---|---|
| `/spieler beitreten [name]` | Als Mitspieler eintragen (gilt ab dem nächsten Run) |
| `/spieler verlassen` · `/spieler liste` | Austragen · alle Mitspieler anzeigen |
| `/run start` | Neuen Run mit allen eingetragenen Spielern starten |
| `/run ende ergebnis [grund]` | Run beenden (gescheitert / geschafft / abgebrochen) |
| `/run liste` | Alle Runs, Anzahl verbrauchter Runs, häufigste Todesursache, wer die meisten Tode ausgelöst hat |
| `/fang route pokemon [spitzname]` | Fang eintragen |
| `/verpasst route [pokemon]` | Encounter verpasst |
| `/tod route ursache` | Eigenes Pokémon gestorben → ganzer Link tot |
| `/status [run]` | Übersicht aller Routen mit den Pokémon aller Spieler |
| `/friedhof [run]` | Alle toten Soul Links mit Ursache und Datum |
| `/route erstellen name` | Eigene Route anlegen (z. B. Static-Pokémon) – erscheint bei allen, gilt für alle Runs |
| `/route loeschen name` · `/route liste` | Eigene Route entfernen · alle eigenen Routen anzeigen |
| `/tabelle run [nummer]` | Tabelle pro Spieler: Lebend, Gefangen, Tode, Verpasst für einen Run |
| `/tabelle global` | Dieselbe Tabelle über alle Runs (plus Anzahl Runs pro Spieler) |
| `/menu` | Bedienfeld mit Buttons posten |
| `/erfolge [spieler]` | Freigeschaltete und gesperrte Erfolge |
| `/titel` | Aktuelle Titelträger und Erfolge aller Spieler |
| `/korrektur fang-loeschen route` | Eigenen Encounter löschen (Vertipper) |
| `/korrektur tod-zuruecknehmen route` | Versehentlichen Tod rückgängig machen |

**Routen:** Autocomplete zeigt zuerst Routen, auf denen deine Partner schon etwas haben, dann eigene
Routen, dann die Standardliste aus `Data/Routes.cs`. Unbekannte Routen werden abgelehnt (schützt vor
Vertippern) – für Statics usw. vorher `/route erstellen`.

**Pokémon:** Auswahl aus allen 649 Pokémon, die es in Schwarz gibt (Gen. 1–5, `Data/Pokedex.cs`).
Suche per Name oder Dex-Nummer. Bei Vertippern schlägt der Bot ähnliche Namen vor.

**Tabelle:** „Tode“ zählt die Soul Links, deren Tod der Spieler ausgelöst hat. „Lebend“ sind die aktuell
nutzbaren Pokémon (in `/tabelle global` die des laufenden Runs).

Bei `/tod` siehst du nur deine eigenen lebenden Pokémon.

## Einrichtung

### 1. Bot im Discord Developer Portal anlegen
1. <https://discord.com/developers/applications> → **New Application**
2. Reiter **Bot** → **Reset Token** → Token kopieren (geheim halten!)
3. Reiter **OAuth2 → URL Generator**: Scopes `bot` und `applications.commands`, Permission `Send Messages`
4. Generierten Link öffnen und den Bot auf euren Server einladen

Privilegierte Intents werden **nicht** benötigt.

### 2. Starten

Benötigt das .NET 10 SDK (für .NET 8: in `NuzlockeBot.csproj` `net8.0` und die EF-Version `8.*` eintragen).

```powershell
# Windows (PowerShell)
$env:DISCORD_TOKEN = "dein-token"
$env:DISCORD_GUILD_ID = "123456789012345678"   # optional, Server-ID
dotnet run
```

```bash
# Linux / Raspberry Pi
export DISCORD_TOKEN="dein-token"
export DISCORD_GUILD_ID="123456789012345678"
dotnet run
```

| Variable | Pflicht | Bedeutung |
|---|---|---|
| `DISCORD_TOKEN` | ja | Bot-Token |
| `DISCORD_GUILD_ID` | nein | Registriert die Commands nur auf diesem Server – **sofort** sichtbar. Ohne: global. |
| `NUZLOCKE_DB` | nein | Pfad zur SQLite-Datei (Standard: `nuzlocke.db`) |

Server-ID: In Discord unter *Einstellungen → Erweitert* den Entwicklermodus aktivieren, dann Rechtsklick auf den Server → *Server-ID kopieren*.

### 3. Dauerhaft laufen lassen (z. B. Raspberry Pi)

```bash
dotnet publish -c Release -r linux-arm64 --self-contained -o ./publish
```

`/etc/systemd/system/nuzlockebot.service`:

```ini
[Unit]
Description=Nuzlocke Discord Bot
After=network-online.target

[Service]
WorkingDirectory=/home/pi/nuzlockebot
ExecStart=/home/pi/nuzlockebot/NuzlockeBot
Environment=DISCORD_TOKEN=dein-token
Environment=DISCORD_GUILD_ID=123456789012345678
Restart=always

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now nuzlockebot
```

## Projektaufbau

```
Program.cs                    Start, DI, Command-Registrierung, Fehlerbehandlung
Data/Entities.cs              Player, Run, RunParticipant, RouteLink (Soul Link), Encounter
Data/NuzlockeDbContext.cs     EF Core + SQLite, eindeutige Indizes für die Nuzlocke-Regeln
Data/Routes.cs                Routenliste für das Autocomplete
Data/Pokedex.cs               Alle 649 Pokémon aus Schwarz (deutsche Namen)
Services/RunService.cs        Komplette Spiellogik (Regeln, Soul-Link-Tod, Korrekturen)
Services/AchievementService.cs Erfolge und Titel
Services/Formatting.cs        Embeds, Texte und Bild-URLs
Services/ImageRenderer.cs     Zeichnet Banner, Boards und Tabellen
Services/CardFactory.cs       Baut aus Embed + Bild die fertigen Nachrichten
Assets/Fonts/                 Schriftart für die Bilder
Modules/*.cs                  Slash-Commands
```

Die Datenbank wird beim ersten Start per `EnsureCreated()` angelegt. Wenn du später das Datenmodell
änderst, entweder die `nuzlocke.db` löschen oder auf EF-Migrations umsteigen
(`dotnet ef migrations add Init`, dann in `Program.cs` `EnsureCreatedAsync()` durch `MigrateAsync()` ersetzen).
