---
name: neue-view
description: Legt eine neue Terminal.Gui-View samt zentraler Tastenbelegung und Test an. Nutzen, wenn eine View unter Views hinzukommt oder die Tastaturbelegung erweitert wird.
---

# Neue View

## Zweck

Eine Terminal.Gui-v2-View anlegen, die Zustand nur anzeigt. Tasten bleiben an einer Stelle definiert.

## Schritte

1. View unter `UmbrellaManager/Views/` anlegen. Kein statischer Zustand, keine `git`- oder `gh`-Aufrufe in der View.
2. Neue Taste nur in der zentralen Belegung eintragen, nicht als eigenen Handler in der View. Bestehende Tasten nicht doppelt belegen: `←`/`→`, `Tab`, `Umschalt+Tab`, `Umschalt+r`, `↑`/`↓`, `Bild↑`/`Bild↓`, Leertaste, Enter, `a`, Entf, `c`, `r`, `q`. `Esc` beendet die Anwendung nicht.
3. Die View aus dem vorhandenen Modell füllen. Production-Tabs bleiben schreibgeschützt.
4. Einen Test anlegen, der die Belegung oder den angezeigten Zustand prüft, ohne die Konsole zu öffnen.

## Prüfung

- `dotnet test umbrella-manager-local.sln` ist grün.
- Die View-Datei enthält keinen Prozessaufruf und kein `static`-Feld für Sitzungszustand.
- Die neue Taste steht nur in der zentralen Belegung.
