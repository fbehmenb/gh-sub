# umbrella-manager-local

Lokales Terminal-Tool: Submodul-Pins des Odoo-Umbrella je Branch ansehen, ändern, hinzufügen und entfernen, immer per PR.

Das Tool läuft ausschließlich auf dem Rechner des Nutzers. Es gibt keinen Server und kein Hosting. Die EXE heißt `umbrella-manager-local.exe`.

Die stabile Terminal.Gui-v2-Linie zielt nur auf `net10.0`. Deshalb ist das Projekt `net10.0` und nicht .NET 8. Terminal.Gui ist auf 2.5.0 gepinnt.

## Voraussetzungen

- .NET SDK 10.0.402 (siehe `global.json`, neuere 10.0-Patches sind zulässig)
- `git` im PATH
- `gh` im PATH und `gh auth status` erfolgreich
- Windows Terminal (Unicode, TrueColor)
- Beim ersten Start fragt das Tool nach der GitHub-URL des Umbrella-Repos und nach dem lokalen Ordner. Liegt dort noch kein Clone, wird er angelegt. Die Angaben stehen danach in `%LOCALAPPDATA%\umbrella-manager-local\setup.json` und werden nicht erneut abgefragt.
- Ein späterer Aufruf mit Pfad nutzt diesen Ordner nur für diesen Start.

## Start

```text
umbrella-manager-local [pfad-zum-umbrella-clone]
```

Ohne Argument und ohne gespeicherte Einrichtung beginnt die Abfrage. Danach gilt der gespeicherte Ordner.

## Befehle

```text
dotnet build umbrella-manager-local.sln
dotnet test umbrella-manager-local.sln
dotnet publish UmbrellaManager/UmbrellaManager.csproj -c Release -r win-x64 -p:PublishSingleFile=true
```
