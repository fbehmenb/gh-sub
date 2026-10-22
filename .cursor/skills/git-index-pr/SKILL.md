---
name: git-index-pr
description: Erzeugt einen Pull Request über einen temporären Git-Index, ohne den Working Tree zu ändern. Nutzen beim Vormerken von Pins, beim Hinzufügen oder Entfernen eines Submoduls und bei der PR-Erzeugung.
---

# PR über temporären Index

## Zweck

Einen Commit und einen PR gegen den Tab-Branch erzeugen, ohne den Working Tree des Umbrella-Clones anzufassen. Ein PR je Tab und je Modul.

## Schritte

1. `git fetch origin`. Wenn `origin/<tab-branch>` seit dem Laden weitergelaufen ist, abbrechen und neu laden verlangen.
2. Production-Tabs (`production/*`) nicht über diesen Weg beschreiben. Dort nur der Promotion-PR.
3. Temporäre Indexdatei anlegen und danach löschen. `GIT_INDEX_FILE` nur für diese Befehle setzen.
4. Je vorgemerktem Pin:

```text
GIT_INDEX_FILE=<temp>  git read-tree origin/<tab-branch>
GIT_INDEX_FILE=<temp>  git update-index --add --cacheinfo 160000,<sha>,<submodul-pfad>
tree=$(GIT_INDEX_FILE=<temp> git write-tree)
commit=$(git commit-tree $tree -p origin/<tab-branch> -m "<titel>")
git push origin $commit:refs/heads/chore/pin-<modul>-<sha7>-<tab>
gh pr create --base <tab-branch> --head chore/pin-<modul>-<sha7>-<tab> --title "<titel>" --body "<body>"
```

5. Titel-Default: `chore(<modul>): pin auf <sha7> (<modul-branch>)`. Body: alter Pin, neuer Pin, Branch des Moduls, Link auf den Commit.
6. Hinzufügen zusätzlich: `.gitmodules` als Blob (`git hash-object -w --stdin`), `update-index --cacheinfo 100644,<blob>,.gitmodules` und den Gitlink. Titel-Default: `feat(<modul>): Submodul hinzufügen (<branch>@<sha7>)`.
7. Entfernen zusätzlich: `update-index --force-remove <pfad>` und den Abschnitt aus `.gitmodules` streichen. Titel-Default: `chore(<modul>): Submodul entfernen`.
8. Fehler je PR melden. Die übrigen PRs weiterlaufen lassen.

Aufrufe nur über die eine Prozessklasse, Argumente als Liste. Kein Force-Push, kein Löschen von Branches.

## Prüfung

- `git status` im Clone ist sauber.
- `git diff --stat` im PR zeigt nur die erwarteten Pfade: beim Pin den Gitlink, beim Hinzufügen `.gitmodules` und den Gitlink, beim Entfernen beides als Löschung.
- Zwei Module im selben Tab ergeben zwei PRs gegen denselben Tab-Branch.
