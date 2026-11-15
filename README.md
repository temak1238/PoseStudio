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

**Links – Pose:** Vorlagen (Stehen, T-Pose, Gehen, Laufen, Sitzen, Knien, Winken, Jubeln, Hände in den Hüften, Kontrapost), Regler je Gelenk, Position der Figur, Spiegeln, Pose speichern/laden (`.pose.json`, inkl. Kamera).

**Rechts – Kamera:** Entfernung, Brennweite (14–200 mm), Blickhöhe, Drehung, Neigung, Schnellansichten (Vorne, 3/4, Seite, Hinten, Oben, Unten, Ganz, Porträt).

**Rechts – Darstellung:** Hintergrundfarbe (Farbwähler, Hex oder Schnellfarben inkl. Greenscreen), Figurfarbe, Boden/Raster und Schatten getrennt für Vorschau und Export.

**Rechts – Ausgabe:** Alle angehakten Formate werden mit einem Klick exportiert (Standard: 16:9 und 9:16). Weitere Formate: 1:1, 4:3, 3:4, 3:2, 2:3, 4:5, 21:9, benutzerdefiniert (Pixel). Lange Seite frei wählbar (Standard 1920 px), JPG-Qualität einstellbar.
Dateiname: `<Präfix>_<Datum>_<Uhrzeit>_<Format>.jpg`

Einstellungen werden in `%APPDATA%\PoseStudio\settings.json` gespeichert.

## Entwicklung
- .NET 10, WPF, keine externen Pakete
- Paket bauen: `build\Build-Package.ps1` (vorher Version in `Directory.Build.props` hochzählen)
- Selbsttest: `PoseStudio.exe --selftest <Ordner>` rendert alle Vorlagen als Kontaktbogen
