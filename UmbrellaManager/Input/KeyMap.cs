using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace UmbrellaManager.Input;

public static class KeyMap
{
    public static IReadOnlyList<(string Key, AppCommand Command)> Bindings { get; } =
    [
        ("Shift+Tab", AppCommand.PreviousBranch),
        ("Tab", AppCommand.NextBranch),
        ("Left", AppCommand.FocusTree),
        ("Right", AppCommand.FocusGraph),
        ("Up", AppCommand.MoveUp),
        ("Down", AppCommand.MoveDown),
        ("PageUp", AppCommand.PreviousModule),
        ("PageDown", AppCommand.NextModule),
        ("Space", AppCommand.ToggleFolder),
        ("Enter", AppCommand.StagePin),
        ("A", AppCommand.AddModule),
        ("Delete", AppCommand.RemoveModule),
        ("C", AppCommand.ChooseNewer),
        ("P", AppCommand.CreatePr),
        ("J", AppCommand.ConfirmPush),
        ("N", AppCommand.CancelPush),
        ("S", AppCommand.OpenSetup),
        ("U", AppCommand.UpdateBranch),
        ("R", AppCommand.Reload),
        ("Shift+R", AppCommand.ReloadAll),
        ("Q", AppCommand.Quit),
        ("?", AppCommand.ShowHelp),
        ("O", AppCommand.Logout),
    ];

    public const int FooterHeight = 1;

    public const string HelpHint = "? Hilfe";

    public static IReadOnlyList<string> Help { get; } =
    [
        "Tasten",
        "Tab          nächster Tab",
        "Umschalt+Tab vorheriger Tab",
        "←            Submodule",
        "→            Commits",
        "↑ ↓        in der Liste oder im Graph bewegen",
        "Bild↑       vorheriges Submodul, ohne die Liste zu fokussieren",
        "Bild↓       nächstes Submodul, ohne die Liste zu fokussieren",
        "Leertaste  Ordner auf- und zuklappen",
        "Enter      Pin vormerken, in Production den Staging-Pin",
        "c          neueren Commit auf demselben Branch wählen, nicht in Production",
        "Entf       vorgemerkten Pin entfernen",
        "a          Submodul hinzufügen",
        "p          PR vorbereiten",
        "j          pushen",
        "n          abbrechen",
        "u          Stand holen",
        "s          Einrichtung, Takt der Remote-Prüfung",
        "r          dieses Submodul neu laden",
        "Umschalt+r  Tabs neu laden",
        "o          bei GitHub abmelden",
        "q          beenden",
        "?          diese Übersicht öffnen oder schließen",
    ];

    public static void Attach(Terminal.Gui.ViewBase.View root, Action<AppCommand> execute)
    {
        root.KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                key.Handled = true;
                return;
            }

            var command = Match(key);
            if (command is null)
            {
                return;
            }

            execute(command.Value);
            key.Handled = true;
        };
    }

    public static AppCommand? Match(Terminal.Gui.Input.Key key)
    {
        if (key == Terminal.Gui.Input.Key.Tab.WithShift)
        {
            return AppCommand.PreviousBranch;
        }

        if (key == Terminal.Gui.Input.Key.Tab)
        {
            return AppCommand.NextBranch;
        }

        if (key == Terminal.Gui.Input.Key.CursorLeft)
        {
            return AppCommand.FocusTree;
        }

        if (key == Terminal.Gui.Input.Key.CursorRight)
        {
            return AppCommand.FocusGraph;
        }

        if (key == Terminal.Gui.Input.Key.CursorUp)
        {
            return AppCommand.MoveUp;
        }

        if (key == Terminal.Gui.Input.Key.CursorDown)
        {
            return AppCommand.MoveDown;
        }

        if (key == Terminal.Gui.Input.Key.PageUp)
        {
            return AppCommand.PreviousModule;
        }

        if (key == Terminal.Gui.Input.Key.PageDown)
        {
            return AppCommand.NextModule;
        }

        if (key == Terminal.Gui.Input.Key.Space)
        {
            return AppCommand.ToggleFolder;
        }

        if (key == Terminal.Gui.Input.Key.Enter)
        {
            return AppCommand.StagePin;
        }

        if (key == Terminal.Gui.Input.Key.A)
        {
            return AppCommand.AddModule;
        }

        if (key == Terminal.Gui.Input.Key.Delete)
        {
            return AppCommand.RemoveModule;
        }

        if (key == Terminal.Gui.Input.Key.C)
        {
            return AppCommand.ChooseNewer;
        }

        if (key == Terminal.Gui.Input.Key.P)
        {
            return AppCommand.CreatePr;
        }

        if (key == Terminal.Gui.Input.Key.J)
        {
            return AppCommand.ConfirmPush;
        }

        if (key == Terminal.Gui.Input.Key.N)
        {
            return AppCommand.CancelPush;
        }

        if (key == Terminal.Gui.Input.Key.S)
        {
            return AppCommand.OpenSetup;
        }

        if (key == Terminal.Gui.Input.Key.U)
        {
            return AppCommand.UpdateBranch;
        }

        if (key == Terminal.Gui.Input.Key.R.WithShift)
        {
            return AppCommand.ReloadAll;
        }

        if (key == Terminal.Gui.Input.Key.R)
        {
            return AppCommand.Reload;
        }

        if (key == Terminal.Gui.Input.Key.O)
        {
            return AppCommand.Logout;
        }

        if (key == Terminal.Gui.Input.Key.Q)
        {
            return AppCommand.Quit;
        }

        if (IsHelp(key))
        {
            return AppCommand.ShowHelp;
        }

        return null;
    }

    private static bool IsHelp(Key key)
    {
        if (key.IsCtrl || key.IsAlt)
        {
            return false;
        }

        return key.AsGrapheme == "?"
            || key.AssociatedText == "?"
            || key.ShiftedKeyCode == (KeyCode)'?'
            || key == new Key('?');
    }
}
