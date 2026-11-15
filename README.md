# Pose Studio

Windows-App zum Posieren einer 3D-Gliederpuppe und zum Export als JPG, z. B. als Posen-Vorlage für KI-Bildgenerierung (ControlNet, Image-to-Image, Referenzbilder).

## Start
`PoseStudio.exe` starten – keine Installation, kein .NET nötig.

## Bedienung
**Ansicht (Mitte)** – der Rahmen entspricht exakt dem späteren Bildausschnitt.
- Klick auf ein Körperteil: Gelenk auswählen (orange markiert)
- Linke Maustaste ziehen: Kamera um die Figur drehen
- Rechte Maustaste ziehen: Kamera verschieben
- Mausrad: Entfernung ändern
- **Strg + ziehen**: ausgewähltes Gelenk beugen (hoch/runter) bzw. seitlich bewegen (links/rechts)
- **Umschalt + ziehen**: ausgewähltes Gelenk um die eigene Achse drehen
- **Alt + ziehen**: Lichtquelle bewegen (links/rechts = Richtung, hoch/runter = Höhe)
- Strg+Z: Rückgängig

**Links – Figur:** Gliederpuppe oder Mensch (männlich/weiblich, mit Gesicht, Fingern, Haaren). Proportionen: Körpergröße, Arm- und Beinlänge, Oberkörper, Schulterbreite, Kopfgröße, Körperbau. Posen bleiben beim Wechsel erhalten.

**Links – Pose:** 38 Vorlagen in 5 Kategorien (Stehen, Gesten, Sitzen & Knien, Liegen, Bewegung) – Auswahl wendet direkt an, Regler je Gelenk, Position der Figur, Spiegeln, Pose speichern/laden (`.pose.json`, inkl. Kamera).

**Rechts – Kamera:** Entfernung, Brennweite (14–200 mm), Blickhöhe, Drehung, Neigung, Schnellansichten (Vorne, 3/4, Seite, Hinten, Oben, Unten, Ganz, Porträt).

**Rechts – Kulisse (3D-Hintergrund):** Fotostudio (Hohlkehle, Farbe wählbar), Draußen (Wiese, Strand, Wüste, Schnee) mit Himmel und Horizont, Innenraum (Wandfarbe, Boden Holz/Fliesen/Beton/Teppich, Raumbreite, Wandabstand, Fenster, Tür), Straße (Gehweg, Fahrbahn, Häuserzeilen). Perspektive passt automatisch, Schatten fallen auch auf Wände und Fassaden. Wände zwischen Kamera und Figur werden ausgeblendet. Optional Horizontlinie (Augenhöhe) – hilfreich auch zum Ausrichten eigener Hintergrundbilder.

**Rechts – Licht & Schatten:** Hauptlicht mit Richtung, Höhe, Helligkeit, Umgebungslicht. Echter **Schattenwurf** auf den Boden (auch über einem Hintergrundbild) und **Eigenschatten** auf der Figur (Arm auf Körper, Kopf auf Brust …), Stärke und Weichheit einstellbar. Schnellwahl Vorne/Seite/Gegenlicht/Mittag, Licht wahlweise an der Kamera mitdrehen. Die gelbe Kugel in der Vorschau zeigt die Lichtquelle (nicht im Export).

**Rechts – Darstellung:** Hintergrundfarbe (Farbwähler, Hex oder Schnellfarben inkl. Greenscreen), Figur-/Hautfarbe, Haarfarbe, **Hintergrundbild** (Füllen/Einpassen/Strecken), Boden/Raster und Schatten getrennt für Vorschau und Export.

**Rechts – Ausgabe:** Alle angehakten Formate werden mit einem Klick exportiert (Standard: 16:9 und 9:16). Seitenverhältnisse 16:9, 9:16, 1:1, 4:3, 3:4, 3:2, 2:3, 5:4, 4:5, 2:1, 1:2, 21:9 und benutzerdefiniert (lange Seite frei wählbar, Standard 1920 px). Dazu feste **SDXL-Auflösungen** für ComfyUI (1024×1024, 1152×896, 896×1152, 1216×832, 832×1216, 1344×768, 768×1344, 1536×640, 640×1536).
**Dateiformat** JPG (Qualität einstellbar) oder PNG; PNG optional mit **transparentem Hintergrund**, wenn kein Hintergrundbild gesetzt ist (Vorschau zeigt dann ein Schachbrett, Schatten bleiben halbtransparent).
Gestrichelte **Hilfslinien** in der Vorschau zeigen den Ausschnitt der anderen angehakten Formate (z. B. 9:16 in der 16:9-Vorschau). Der Export-Button ist unten rechts immer sichtbar.
Dateiname: `<Präfix>_<Datum>_<Uhrzeit>_<Format>.jpg|png`

Einstellungen werden in `%APPDATA%\PoseStudio\settings.json` gespeichert.

## Entwicklung
- .NET 10, WPF, keine externen Pakete
- Paket bauen: `build\Build-Package.ps1` (vorher Version in `Directory.Build.props` hochzählen)
- Selbsttest: `PoseStudio.exe --selftest <Ordner>` rendert alle Figurenstile und Vorlagen als Kontaktbogen (braucht eine Sitzung mit aktivem Display)
- Planung Hintergründe: `docs/Plan-Hintergrund-Generierung.md`
