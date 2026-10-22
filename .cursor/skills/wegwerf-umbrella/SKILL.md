---
name: wegwerf-umbrella
description: Baut ein Wegwerf-Umbrella mit zwei Dummy-Submodulen auf und wieder ab. Nutzen für den Akzeptanztest der Pins, des Hinzufügens und des Entfernens, bevor das echte Umbrella angefasst wird.
---

# Wegwerf-Umbrella

## Zweck

Ein temporäres Umbrella mit zwei Dummy-Submodulen, gegen das die Akzeptanzkriterien laufen. Danach wird es wieder abgebaut.

## Schritte

1. Zwei leere Dummy-Repos und ein Umbrella-Repo außerhalb des Arbeitsbaums anlegen, je mit Remote-Branch. Kein Force-Push, keine Rulesets.
2. Beide Dummys als Submodule eintragen (`.gitmodules` und Gitlink, Mode 160000). Eines davon zusätzlich mit einem eigenen Submodule, damit die Ablehnung beim Hinzufügen prüfbar ist.
3. Branches `dev/*`, `staging/*` und `production/*` anlegen. `production/*` nicht direkt beschreiben.
4. Das Tool gegen dieses Clone laufen lassen: Tabs und Struktur, Pin-Farben, zwei PRs für zwei Module, Promotion-PR, sauberer `git status`, Hinzufügen, Entfernen mit Namenseingabe, Ablehnung eines Repos mit eigenen Submodulen.
5. PRs und Branches des Wegwerf-Remotes nur löschen, wenn der Nutzer das abbauen ausdrücklich freigibt. Lokale Clone-Verzeichnisse danach entfernen.

## Prüfung

- Die zehn Akzeptanzkriterien aus [docs/aufgabe.md](../../../docs/aufgabe.md) sind gegen das Wegwerf-Umbrella durchgegangen.
- `git status` im Wegwerf-Clone ist danach sauber.
- Das echte Umbrella `odoo_umbrella` wurde nicht verändert.
