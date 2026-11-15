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
- Strg+Z: Rückgängig

**Links – Figur:** Gliederpuppe oder Mensch (männlich/weiblich, mit Gesicht, Fingern, Haaren). Proportionen: Körpergröße, Arm- und Beinlänge, Oberkörper, Schulterbreite, Kopfgröße, Körperbau. Posen bleiben beim Wechsel erhalten.

**Links – Pose:** Vorlagen (Stehen, T-Pose, Gehen, Laufen, Sitzen, Knien, Winken, Jubeln, Hände in den Hüften, Kontrapost), Regler je Gelenk, Position der Figur, Spiegeln, Pose speichern/laden (`.pose.json`, inkl. Kamera).

**Rechts – Kamera:** Entfernung, Brennweite (14–200 mm), Blickhöhe, Drehung, Neigung, Schnellansichten (Vorne, 3/4, Seite, Hinten, Oben, Unten, Ganz, Porträt).

**Rechts – Darstellung:** Hintergrundfarbe (Farbwähler, Hex oder Schnellfarben inkl. Greenscreen), Figur-/Hautfarbe, Haarfarbe, **Hintergrundbild** (Füllen/Einpassen/Strecken), Boden/Raster und Schatten getrennt für Vorschau und Export.

**Rechts – Ausgabe:** Alle angehakten Formate werden mit einem Klick exportiert (Standard: 16:9 und 9:16). Weitere Formate: 1:1, 4:3, 3:4, 3:2, 2:3, 4:5, 21:9, benutzerdefiniert (Pixel). Lange Seite frei wählbar (Standard 1920 px), JPG-Qualität einstellbar.
Gestrichelte **Hilfslinien** in der Vorschau zeigen den Ausschnitt der anderen angehakten Formate (z. B. 9:16 in der 16:9-Vorschau). Der Export-Button ist unten rechts immer sichtbar.
Dateiname: `<Präfix>_<Datum>_<Uhrzeit>_<Format>.jpg`

Einstellungen werden in `%APPDATA%\PoseStudio\settings.json` gespeichert.

## Entwicklung
- .NET 10, WPF, keine externen Pakete
- Paket bauen: `build\Build-Package.ps1` (vorher Version in `Directory.Build.props` hochzählen)
- Selbsttest: `PoseStudio.exe --selftest <Ordner>` rendert alle Figurenstile und Vorlagen als Kontaktbogen (braucht eine Sitzung mit aktivem Display)
- Planung Hintergründe: `docs/Plan-Hintergrund-Generierung.md`
