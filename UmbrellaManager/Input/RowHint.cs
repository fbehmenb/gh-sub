namespace UmbrellaManager.Input;

using UmbrellaManager.Model;

public readonly record struct RowHintInput(
    bool Help,
    bool Publishing,
    bool Result,
    bool Confirming,
    bool Production,
    FocusPane Focus,
    bool Folder,
    bool Module,
    bool LoadingCommits,
    bool HasCommit,
    CommitTone Tone,
    bool ModuleStaged,
    bool AnyStaged,
    bool Conflict = false,
    bool NewerOnBranch = false,
    bool Setup = false,
    bool Promotable = false,
    bool Checking = false,
    bool Success = false);

public static class RowHint
{
    public static string Describe(RowHintInput row)
    {
        if (row.Conflict)
        {
            return "Jetzt: nur n abbrechen";
        }

        if (row.Setup)
        {
            return "Jetzt: ↑↓ Takt wählen · Enter speichern · s schließt";
        }

        if (row.Help)
        {
            return "Jetzt: ? schließt die Übersicht";
        }

        if (row.Publishing)
        {
            return "Jetzt: Pull Requests werden angelegt";
        }

        if (row.Success)
        {
            return "Jetzt: Erfolgreich · Taste schließt";
        }

        if (row.Result)
        {
            return "Jetzt: beliebige Taste schließt die Meldung";
        }

        if (row.Checking)
        {
            return "Jetzt: Repo-Stand wird abgeglichen";
        }

        if (row.Confirming)
        {
            return "Jetzt: j diese Änderungen pushen · n abbrechen";
        }

        if (row.Production)
        {
            if (row.Focus == FocusPane.Tree && row.Folder)
            {
                return "Jetzt: Leertaste Ordner auf/zu · → Commits";
            }

            if (row.Focus != FocusPane.Graph || !row.HasCommit)
            {
                return row.LoadingCommits && !row.HasCommit
                    ? "Jetzt: Commits werden geladen"
                    : "Jetzt: Enter auf den Staging-Pin";
            }

            var push = row.AnyStaged ? " · p PR vorbereiten" : "";
            if (row.Promotable && row.Tone == CommitTone.LightGreen)
            {
                return "Jetzt: Enter Vormerkung zurück" + push;
            }

            if (row.Promotable)
            {
                return "Jetzt: Enter diesen Staging-Pin vormerken" + push;
            }

            return "Jetzt: Enter auf den Staging-Pin" + push;
        }

        if (row.Focus == FocusPane.Tree)
        {
            if (row.Folder)
            {
                return "Jetzt: Leertaste Ordner auf/zu · → zu den Commits";
            }

            if (row.Module)
            {
                var staged = row.ModuleStaged ? " · Entf Vormerkung entfernen" : "";
                return "Jetzt: → Commits dieses Moduls" + staged + " · a Modul hinzufügen";
            }

            return "Jetzt: ↑↓ Modul wählen";
        }

        if (!row.Module)
        {
            return "Jetzt: zuerst ein Modul wählen";
        }

        if (row.LoadingCommits && !row.HasCommit)
        {
            return "Jetzt: Commits werden geladen";
        }

        if (!row.HasCommit)
        {
            return "Jetzt: kein Commit gewählt";
        }

        var prepare = row.AnyStaged ? " · p PR vorbereiten" : "";
        if (row.Tone == CommitTone.LightGreen)
        {
            return "Jetzt: Entf Vormerkung entfernen" + prepare;
        }

        if (row.Tone == CommitTone.DarkGreen)
        {
            return (row.ModuleStaged ? "Jetzt: Entf Vormerkung entfernen" : "Jetzt: aktueller Pin") + prepare;
        }

        if (row.NewerOnBranch)
        {
            return "Jetzt: c diesen neueren Commit wählen" + prepare;
        }

        return "Jetzt: Enter diesen Commit vormerken" + prepare;
    }
}
