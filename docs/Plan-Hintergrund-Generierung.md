# Plan: Hintergründe für Pose Studio

Stand: 2026-11-15 · Status: Stufe 0 (0.2.0) und Stufe 1 (0.6.0) umgesetzt; Stufe 2 wartet auf die ControlNet-Auswahl

## Ziel
Neben der Figur einen passenden **Basis-Hintergrund** liefern, damit die KI (img2img, ControlNet, Referenzbild)
nicht nur die Pose, sondern auch Raum, Horizont und Perspektive übernimmt.

## Kernproblem: Perspektive
Ein beliebiges Hintergrundbild hat seine eigene Kamera (Höhe, Neigung, Brennweite). Passt sie nicht zur
Kamera in Pose Studio, „schwebt“ die Figur oder steht schief im Raum. Deshalb ist der Weg wichtig:

| Weg | Perspektive passt automatisch? |
|---|---|
| Eigenes 2D-Bild laden (Stufe 0) | nein – manuell ausrichten |
| Prozeduraler 3D-Hintergrund (Stufe 1) | **ja** – gleiche Kamera wie die Figur |
| KI-Generierung mit Tiefenkarte des 3D-Hintergrunds (Stufe 3A) | **ja**, weitgehend |
| KI-Generierung nur per Text (Stufe 3B) | nein – Horizont-Hilfslinie hilft |

## Stufe 0 – 2D-Hintergrundbild ✅ (0.2.0)
Bild laden, Füllen/Einpassen/Strecken, wird in Vorschau und Export verwendet. Schatten bleibt zuschaltbar.

## Stufe 1 – Prozedurale 3D-Basis-Hintergründe (offline, kostenlos) ✅ (0.6.0)
Aufwand: **gering bis mittel** (ca. eine Arbeitssitzung).
Echte 3D-Kulissen in derselben Szene wie die Figur, dadurch stimmen Horizont, Fluchtpunkte und Kamerabewegung immer.

Vorlagen (jeweils mit Farb-/Größenparametern):
- **Studio-Hohlkehle** (Boden geht weich in die Rückwand über, Farbverlauf) – Fotostudio-Look
- **Horizont** – Himmelverlauf + Bodenebene (Gras, Sand, Asphalt, Wasser als Farbe/Textur)
- **Innenraum** – Boden + 2–3 Wände, optional Fenster/Tür-Öffnungen, Bodenbelag (Holz, Fliese, Teppich)
- **Straße/Platz** – Fahrbahn, Gehweg, einfache Häuserblöcke links/rechts
- ~~Bühne/Podest~~ – noch offen

Dazu: Horizontlinie als Hilfslinie in der Vorschau (zeigt Augenhöhe der Kamera).

## Stufe 2 – Zusatzausgaben für ControlNet & Co.
Aufwand: **mittel**. Lohnt sich, wenn du Stable Diffusion / Flux mit ControlNet nutzt.
- **Tiefenkarte** (Graustufen, nah = hell) von Figur + Kulisse → ControlNet *Depth*
- **OpenPose-Skelett** (farbige Linien auf Schwarz) → ControlNet *OpenPose*, Gelenkpositionen sind exakt bekannt
- **Maske** der Figur (Schwarz/Weiß-PNG) → Inpainting / Freistellen
- **Kantenbild** (Linienzeichnung) → ControlNet *Canny/Lineart*

Alle im selben Ausschnitt und Format wie das JPG, ein Klick exportiert alle Varianten.

## Stufe 3 – KI-generierte Hintergründe per Text-Prompt
Aufwand: **mittel**, abhängig vom Dienst. Ablauf in der App:
1. Prompt eingeben, z. B. „Waldlichtung am Abend, Gegenlicht“
2. Format/Größe kommt aus dem gewählten Exportformat
3. Optional: Tiefenkarte der Stufe-1-Kulisse mitsenden → Perspektive passt
4. Ergebnis wird als Hintergrundbild übernommen und im Zielordner abgelegt (Verlauf zum Zurückspringen)

| Option | Kosten | Voraussetzung | Perspektive per Tiefenkarte |
|---|---|---|---|
| **A: Lokal** – ComfyUI / Forge / Automatic1111 über deren API | kostenlos | eigene GPU (≥ 8 GB VRAM), installiertes Tool | ja |
| **B: Cloud** – z. B. OpenAI Images, Stability AI, Black Forest Labs (Flux) | ca. 1–8 Cent pro Bild | API-Schlüssel (sicher in der Windows-Anmeldeinformationsverwaltung) | je nach Anbieter |
| Midjourney | – | keine offizielle API → nicht einbindbar | – |

## Empfohlene Reihenfolge
1. **Stufe 1** (prozedurale Kulissen + Horizontlinie) – größter Nutzen pro Aufwand, keine Kosten, immer perspektivisch korrekt
2. **Stufe 2**, wenn ControlNet genutzt wird
3. **Stufe 3** mit dem KI-Dienst, den du ohnehin nutzt

## Offene Fragen
- Mit welcher KI erzeugst du die Bilder (ChatGPT, Midjourney, Stable Diffusion/ComfyUI, Flux, …)?
- Lokale GPU vorhanden, oder lieber Cloud-Dienst?
- Welche Hintergründe brauchst du am häufigsten (Studio, draußen, Innenraum, Straße)?
