# umbrella-manager-local

Lokales Terminal-Tool für die Submodul-Pins des Odoo-Umbrella. Es zeigt ein Clone von `Gahrens-Battermann-GmbH-Co-KG/odoo_umbrella` und ändert Pins je Branch ausschließlich per Pull Request. Es läuft nur auf dem Rechner des Nutzers und ruft `git` und `gh` als Prozesse auf.

## Stack

.NET 10 (`net10.0`), Terminal.Gui 2.5.0. Die stabile Terminal.Gui-v2-Linie zielt nur auf `net10.0`, deshalb nicht .NET 8. EXE: `umbrella-manager-local.exe`.

## Befehle

- `dotnet build umbrella-manager-local.sln`
- `dotnet test umbrella-manager-local.sln`
- `dotnet publish UmbrellaManager/UmbrellaManager.csproj -c Release -r win-x64 -p:PublishSingleFile=true`
- Erweiterung: Repository `gh-sub`, Tag `v*`, danach `gh extension install <Organisation>/gh-sub` und Aufruf `gh sub`

## Konventionen

- Conventional Commits. Kein `gb_*`-Modul, keine Odoo-Tags.
- Nie direkt auf Tab-Branches pushen. Immer Arbeitsbranch und PR.
- Kein Force-Push, kein Löschen von Branches, keine Änderungen an Rulesets.

Aufgabe: [docs/aufgabe.md](docs/aufgabe.md). Skills und Agents liegen unter `.cursor/` und werden nicht nach `.claude/` kopiert.
