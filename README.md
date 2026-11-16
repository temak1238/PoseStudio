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
- **Strg + ziehen**: ausgewähltes Gelenk beugen (hoch/runter) bzw. seitlich bewegen (links/rechts) – bei **Hand oder Fuß** folgt der ganze Arm bzw. das ganze Bein (IK: Ellbogen/Knie behalten ihre Richtung, Arme weichen dem Rumpf aus, die Hand behält ihre Ausrichtung)
- **Umschalt + ziehen**: ausgewähltes Gelenk um die eigene Achse drehen – an **Arm oder Bein** (Hand, Unter-/Oberarm, Fuß, Knie, Hüfte): Ellbogen bzw. Knie um die Achse Schulter–Hand schwenken, die Hand bleibt stehen
- **Strg + ziehen am Becken**: ganze Figur auf dem Boden verschieben (folgt der Maus) · **Umschalt + ziehen am Becken**: Figur drehen
- **Alt + ziehen**: Lichtquelle bewegen (links/rechts = Richtung, hoch/runter = Höhe)
- Strg+Z: Rückgängig

**Links – Figuren in der Szene:** Mehrere Figuren mit eigener Pose, Position, Figurtyp, Farben und Objekt. Hinzufügen, Duplizieren, Entfernen; aktive Figur über die Liste. In der Ansicht lässt sich nur die aktive Figur anklicken und bewegen (optional: „Figur per Klick in der Ansicht wählen“). Figuren werfen auch Schatten aufeinander. Die Szene wird in den Einstellungen und in der Pose-Datei gespeichert.

**Links – Figur:** Gliederpuppe oder Mensch (männlich/weiblich, mit Gesicht, Fingern, Haaren; Rumpf, Arme und Beine als durchgehende Formen mit weichen Gelenkübergängen). Proportionen: Körpergröße, Arm- und Beinlänge, Oberkörper, Schulterbreite, Kopfgröße, Körperbau. Posen bleiben beim Wechsel erhalten.

**Links – Objekt in der Hand:** Schwert, Zauberstab, Stock, langer Stab, Speer – mit rechter, linker oder beiden Händen. Die Arme folgen dem Objekt automatisch (inverse Kinematik), die Hände schließen sich um den Griff. Lage per Regler oder direkt in der Ansicht: Objekt anklicken, **Strg + ziehen** verschiebt, **Umschalt + ziehen** dreht. Das Objekt hängt am Oberkörper und bewegt sich mit.

**Pose-Bibliothek:** „Bibliothek…“ zeigt alle Vorlagen als Kacheln mit Vorschaubild (Filter nach Kategorie, Suche); **Doppelklick** übernimmt die Pose und schließt die Bibliothek, „Ausgewählte Pose anwenden“ übernimmt sie bei offener Bibliothek. „Als Vorlage speichern…“ legt die Pose der aktiven Figur (inkl. Objekt) mit Bild in `%APPDATA%\PoseStudio\Vorlagen` ab – sie erscheint auch in der Vorlagenliste unter „Eigene“.

**Referenzfoto & Pose aus Foto** (links, Gruppe „Referenzfoto (aktive Figur)“): „Foto laden…“, „Pose übernehmen“, „Entfernen“, Deckkraft. Jede Figur hat ihr eigenes Referenzfoto; beim Wechsel der Figur wird ihres angezeigt. „Pose übernehmen“ erkennt die Pose einer Person im Foto (bzw. im geladenen Referenzbild) und überträgt sie auf die aktive Figur. Das erkannte Skelett wird über dem Referenzbild eingezeichnet. Grundlage: MediaPipe BlazePose (33 Punkte inkl. 3D) aus dem OpenCV Model Zoo (Apache 2.0) über ONNX Runtime, lokal ohne Internet. Eine Person je Foto; Hände/Finger und Kopfdrehung nur grob – danach mit Strg+Ziehen nachkorrigieren. Die Pose wird so übernommen, wie die aktuelle Kamera schaut.

**Referenzbild:** Foto halbtransparent über der Ansicht (Deckkraft einstellbar), um eine Pose nachzustellen – wird nicht exportiert.

**Links – Pose:** 38 Vorlagen in 5 Kategorien (Stehen, Gesten, Sitzen & Knien, Liegen, Bewegung) – Auswahl wendet direkt an, Regler je Gelenk, Position der Figur, Spiegeln, Pose speichern/laden (`.pose.json`, inkl. Kamera).

**Rechts – Kamera:** Entfernung, Brennweite (14–200 mm), Blickhöhe, Drehung, Neigung, Schnellansichten (Vorne, 3/4, Seite, Hinten, Oben, Unten, Ganz, Porträt).

**Rechts – Kulisse (3D-Hintergrund):** Fotostudio (Hohlkehle, Farbe wählbar), Draußen (Wiese, Strand, Wüste, Schnee) mit Himmel und Horizont, Innenraum (Wandfarbe, Boden Holz/Fliesen/Beton/Teppich, Raumbreite, Wandabstand, Fenster, Tür), Straße (Gehweg, Fahrbahn, Häuserzeilen). Perspektive passt automatisch, Schatten fallen auch auf Wände und Fassaden. Wände zwischen Kamera und Figur werden ausgeblendet. Optional Horizontlinie (Augenhöhe) – hilfreich auch zum Ausrichten eigener Hintergrundbilder.

**Rechts – Licht & Schatten:** Hauptlicht mit Richtung, Höhe, Helligkeit, Umgebungslicht. Echter **Schattenwurf** auf den Boden (auch über einem Hintergrundbild) und **Eigenschatten** auf der Figur (Arm auf Körper, Kopf auf Brust …), Stärke und Weichheit einstellbar. Schnellwahl Vorne/Seite/Gegenlicht/Mittag, Licht wahlweise an der Kamera mitdrehen. Die gelbe Kugel in der Vorschau zeigt die Lichtquelle (nicht im Export).

**Rechts – Darstellung:** Hintergrundfarbe (Farbwähler, Hex oder Schnellfarben inkl. Greenscreen), Figur-/Hautfarbe, Haarfarbe, **Hintergrundbild** (Füllen/Einpassen/Strecken), Boden/Raster und Schatten getrennt für Vorschau und Export.

**Rechts – Ausgabe:** Alle angehakten Formate werden mit einem Klick exportiert (Standard: 16:9 und 9:16). Seitenverhältnisse 16:9, 9:16, 1:1, 4:3, 3:4, 3:2, 2:3, 5:4, 4:5, 2:1, 1:2, 21:9 und benutzerdefiniert (lange Seite frei wählbar, Standard 1920 px). Dazu feste **SDXL-Auflösungen** für ComfyUI (1024×1024, 1152×896, 896×1152, 1216×832, 832×1216, 1344×768, 768×1344, 1536×640, 640×1536).
**Dateiformat** JPG (Qualität einstellbar) oder PNG; PNG optional mit **transparentem Hintergrund**, wenn kein Hintergrundbild gesetzt ist (Vorschau zeigt dann ein Schachbrett, Schatten bleiben halbtransparent).
Gestrichelte **Hilfslinien** in der Vorschau zeigen den Ausschnitt der anderen angehakten Formate (z. B. 9:16 in der 16:9-Vorschau). Der Export-Button ist unten rechts immer sichtbar.
**ControlNet für ComfyUI:** zusätzlich (oder ausschließlich) PNGs im gleichen Ausschnitt: *3D-Pose* (Figuren einfarbig glänzend auf Schwarz, jede Figur eigene Farbe – für toyxyz/Anima_ControlNet-LLLite `anima_3dpose`) und *Regionen-Karte* (flache Farbfläche je Figur – für `anima_regional`). Ohne Haare, Objekte, Boden und Kulisse.
Dateiname: `<Präfix>_<Datum>_<Uhrzeit>_<Format>.jpg|png`

Einstellungen werden in `%APPDATA%\PoseStudio\settings.json` gespeichert.

## Entwicklung
- .NET 10, WPF; einziges Paket: Microsoft.ML.OnnxRuntime (MIT) für die Posenerkennung. Modelle in `src/PoseStudio/Models` (siehe README-Modelle.txt)
- Paket bauen: `build\Build-Package.ps1` (vorher Version in `Directory.Build.props` hochzählen)
- Selbsttest: `PoseStudio.exe --selftest <Ordner>` rendert alle Figurenstile und Vorlagen als Kontaktbogen (braucht eine Sitzung mit aktivem Display)
- Planung Hintergründe: `docs/Plan-Hintergrund-Generierung.md`
