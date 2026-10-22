# Aufgabe: umbrella-manager-local (lokales Terminal-Tool für Pins und Submodule im Umbrella)

## Name und Beschreibung

- Name: **umbrella-manager-local** (Umbrella-Repo verwalten, läuft lokal, Terminal-Oberfläche).
- Repo `umbrella-manager-local` in der Org `Gahrens-Battermann-GmbH-Co-KG`, EXE `umbrella-manager-local.exe`.
- Läuft ausschließlich lokal auf dem Rechner des Nutzers, kein Server, kein Hosting.
- Repo-Beschreibung (GitHub-Feld): „Lokales Terminal-Tool: Submodul-Pins des Odoo-Umbrella je Branch ansehen, ändern, hinzufügen und entfernen, immer per PR.“
- Commits im Repo: Conventional Commits (kein `gb_*`-Modul, keine Odoo-Tags).

## Ziel

umbrella-manager-local ist ein kleines lokales Konsolen-Tool (.NET 10, Terminal.Gui v2), das ein Clone des Umbrella-Repos `Gahrens-Battermann-GmbH-Co-KG/odoo_umbrella` anzeigt, Submodul-Pins (Gitlinks) je Umbrella-Branch per Pull Request ändert und Submodule hinzufügt oder entfernt. Rein tastaturbedienbar, schlank, schnell fertig. Kein eigener Git-Code: das Tool ruft `git` und `gh` als Prozesse auf.

Abweichung vom ursprünglichen Rahmen .NET 8: die stabile Terminal.Gui-v2-Linie (2.0.0 bis 2.5.0) zielt nur auf `net10.0`. Das Projekt ist deshalb `net10.0`, Terminal.Gui ist auf 2.5.0 gepinnt.

## Rahmen

- .NET 10, Terminal.Gui v2, gepinnt auf 2.5.0.
- `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true` liefert eine EXE.
- Start: `umbrella-manager-local [pfad-zum-umbrella-clone]`, Default = aktuelles Verzeichnis.
- Voraussetzungen beim Start prüfen und klar melden: `git` im PATH, `gh` im PATH, `gh auth status` ok, Verzeichnis ist ein Git-Repo. `.gitmodules` muss nicht auf dem ausgecheckten Branch liegen.
- Läuft in Windows Terminal (Unicode, TrueColor). Kein Fallback für alte conhost.

## Oberfläche

1. **Tabs oben:** ein Tab je Branch des Umbrella-Repos, der eine `.gitmodules` enthält (`git fetch` aller Heads, danach `git cat-file -e origin/<branch>:.gitmodules`). Reihenfolge: `dev/*`, `staging/*`, `production/*`, danach alle übrigen alphabetisch. Annahme: Branches mit dem Muster `chore/pin-*` (die vom Tool selbst angelegten PR-Branches) werden standardmäßig ausgeblendet, Muster konfigurierbar.
2. **Links: Submodul-Struktur** des gewählten Tab-Branches. Quelle: `.gitmodules` plus `git ls-tree -r <branch>` (Einträge mit Mode 160000). Ordner werden als aufklappbare Knoten gezeigt, Submodule rutschen darunter ein (z. B. `gb_modules/` mit den `gb_*`-Modulen, daneben `ratocon_modules`). Nur Struktur, keine Dateien.
3. **Rechts: Commit-Graph** des gewählten Submoduls, live von GitHub:
   - Spur 0 = Standard-Branch des Moduls (`branch =` aus `.gitmodules`, sonst Default-Branch des Repos, z. B. `19.0`), weitere Spuren = andere Branches, auf die ein Pin zeigt oder die jünger sind (Partner-Feature-Branches).
   - Je Commit: Kurz-SHA, Betreff, Spur-Punkt. Neueste oben, letzte 30 je Branch.
   - Farben: aktuell gepinnter Commit **dunkelgrün**, vorgemerkter neuer Pin **hellgrün**, alle anderen grau. Der Pin darf auf jedem Branch liegen.
4. **Statusleiste unten:** vorgemerkte Änderungen, Tastenhilfe.

## Bedienung

- `Tab` nächster Tab, `Umschalt+Tab` vorheriger Tab, `←` Submodule, `→` Commits, `↑`/`↓` navigieren, `Leertaste` Ordner auf/zu, `Enter` Pin vormerken (nochmal auf den aktuellen Pin = Vormerkung zurücknehmen), `a` Submodul hinzufügen, `Entf` Submodul zum Entfernen vormerken, `c` Commit + PR anlegen, `r` neu laden, `q` beenden.
- Vor `c` ein Bestätigungsdialog mit der Liste aller PRs, die entstehen würden.

## Regeln

- **Production übernimmt nur Staging-Pins.** Freies Pinnen, Hinzufügen und Entfernen sind im Production-Tab gesperrt. Rechts zeigt der Graph den Pin von `production/<suffix>` und den von `staging/<suffix>`. `Enter` merkt einen Commit vor, der als Gitlink auf diesem Staging-Branch lag und gegenüber dem Production-Pin `ahead` ist (`gh api repos/<org>/<repo>/compare/<production-sha>...<kandidat>`). Liegt die Auswahl nicht auf so einem Commit, springt `Enter` auf den aktuellen Staging-Pin. `p` legt daraus den Pin-PR nur für dieses Submodul gegen `production/<suffix>` an, ein PR je Modul, Titel wie die Pin-Vorlage. Der Body nennt den Staging-Branch als Quelle. `c` wählt dort nichts. Die Spalte mit Haken oder „nicht 19.0" bleibt zur Info und blockiert die Auswahl nicht.
- **Nie direkt auf Tab-Branches pushen.** Immer Arbeitsbranch + PR.
- **Ein PR je Tab und je Modul.** Vorgemerkte Pins mehrerer Module im selben Tab ergeben mehrere PRs, nicht einen.
- Kein Force-Push, kein Löschen von Branches, keine Änderungen an Rulesets.

## PR-Erzeugung (ohne Working Tree anzufassen)

Je vorgemerktem Pin:

```text
GIT_INDEX_FILE=<temp>  git read-tree origin/<tab-branch>
GIT_INDEX_FILE=<temp>  git update-index --add --cacheinfo 160000,<sha>,<submodul-pfad>
tree=$(GIT_INDEX_FILE=<temp> git write-tree)
commit=$(git commit-tree $tree -p origin/<tab-branch> -m "<titel>")
git push origin $commit:refs/heads/chore/pin-<modul>-<sha7>-<tab>
gh pr create --base <tab-branch> --head chore/pin-<modul>-<sha7>-<tab> --title "<titel>" --body "<body>"
```

- Vorher `git fetch` des Tab-Branches. Hat sich der Gitlink eines vorgemerkten Pfads geändert, wird dieser Pin nicht gepusht. Ist der Tip nur weitergelaufen, setzt der Commit auf `origin/<tab-branch>` auf.
- Titel-Vorlage in der Konfiguration, Default im Odoo-Format: `[REL] <modul>: <version>`. `[REL]` ist der Odoo-Tag für ein Release. Die Version kommt aus `__manifest__.py` des gepinnten Commits. Fehlt sie, steht die Kurz-SHA. Die Stufe steckt im Zielbranch des PRs.
- Body: alter Pin → neuer Pin, Branch des Moduls, Link auf den Commit. Ein Production-Pin nennt zusätzlich den Staging-Branch als Quelle.
- Ergebnis je PR anzeigen (URL), Fehler je PR einzeln melden, restliche PRs weiterlaufen lassen.

## Submodule hinzufügen und entfernen

Beides nur in Tabs, die kein `production/*` sind, und wie Pins immer per PR (ein PR je Tab und Modul, gleiche Technik mit temporärem Index).

**Hinzufügen (`a`)**, Dialog in Schritten:

1. Repo wählen: `gh repo list <org> --limit 200` mit Tipp-Filter; bereits eingebundene Repos ausblenden.
2. Zielordner wählen aus der vorhandenen Struktur. Vorschlag: `gb_*` nach `gb_modules/`, `ratocon_*`/`syscoon_*` an die Wurzel. Der Pfad endet auf den Repo-Namen.
3. Branch des Moduls wählen, Default `19.0`.
4. Start-Pin wählen (Commit-Graph wie sonst), Default Spitze des Branches.
- Prüfung: Das Modul darf selbst keine Submodule enthalten (keine Submodule in Submodulen); `gh api repos/<org>/<repo>/git/trees/<sha>?recursive=1` auf Mode 160000 prüfen.
- Änderung im Index: `.gitmodules` aus `git show <tab>:.gitmodules` lesen, Abschnitt anhängen (`path`, `url` als HTTPS-URL, `branch`), per `git hash-object -w --stdin` als Blob speichern, dann `update-index --cacheinfo 100644,<blob>,.gitmodules` und `update-index --add --cacheinfo 160000,<sha>,<pfad>`.
- Titel-Default: `feat(<modul>): Submodul hinzufügen (<branch>@<sha7>)`.

**Entfernen (`Entf`)**, destruktiv:

- Bestätigung nur durch Eintippen des Modulnamens. Dialogtext: Der Eintrag verschwindet in diesem Branch; ist das Odoo-Modul in einer Datenbank installiert, vorher dort deinstallieren, sonst bricht der Build oder die Datenbank. Kollegen müssen lokal `git submodule deinit` ausführen, der Ordner bleibt bei ihnen sonst liegen.
- Änderung im Index: `update-index --force-remove <pfad>` und den `.gitmodules`-Abschnitt streichen (Blob wie oben).
- Titel-Default: `chore(<modul>): Submodul entfernen`.
- Vormerken ist rückgängig machbar (nochmal `Entf`), erst `c` löst den PR aus.

Beide Titel-Vorlagen sind wie die Pin-Vorlage konfigurierbar.

## Fehlerbehandlung

- `gh` liefert 404 für private Repos, wenn die SSO-Autorisierung der Org fehlt: eigene, verständliche Meldung statt Rohfehler.
- Rate Limit und Netzfehler: Anzeige „offline/zwischengespeichert", kein Absturz.
- Ergebnisse von `gh api` pro Sitzung zwischenspeichern, `r` leert den Cache.

## Akzeptanzkriterien

1. Start auf einem Umbrella-Clone zeigt Tabs für alle Remote-Branches und die Submodul-Struktur mit Ordnern.
2. Im Graph ist der aktuelle Pin dunkelgrün, ein vorgemerkter Pin hellgrün.
3. Zwei Pins in zwei Modulen im selben Tab erzeugen zwei PRs gegen diesen Tab-Branch.
4. Der Production-Tab merkt nur Staging-Pins vor, die neuer als Production sind, und `p` legt daraus einen Pin-PR für dieses Submodul an. Freies Pinnen, Hinzufügen und Entfernen bleiben gesperrt.
5. Der Working Tree des Clones bleibt nach dem Lauf unverändert (`git status` sauber).
6. Ohne `gh`-Login oder ohne Repo startet das Tool mit klarer Meldung statt Stacktrace.
7. Ein Test gegen ein Wegwerf-Umbrella mit zwei Dummy-Submodulen läuft grün.
8. Hinzufügen eines Repos erzeugt einen PR, in dem genau `.gitmodules` und ein neuer Gitlink am gewählten Pfad geändert sind (`git diff --stat` im PR prüfen).
9. Entfernen erfordert die Namenseingabe und erzeugt einen PR, der Gitlink und `.gitmodules`-Abschnitt entfernt.
10. Ein Repo mit eigenen Submodulen wird beim Hinzufügen abgelehnt.

## Nicht im Umfang

Rulesets, Release/Tags, Web-Oberfläche, automatische Promotion, Dateiansicht innerhalb der Submodule.

## Vorgehen

1. Phase 0 (Grundstruktur) umsetzen, committen, Nutzer prüft.
2. Plan für Phase 1 kurz vorschlagen und abstimmen.
3. Lesende Teile zuerst (Tabs, Struktur, Graph), dann Vormerken, dann PR-Erzeugung, dann Hinzufügen und Entfernen.
4. Erst an einem Wegwerf-Umbrella testen, dann am echten.
