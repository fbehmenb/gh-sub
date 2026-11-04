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
        ("Tab+R", AppCommand.ReloadAll),
        ("Q", AppCommand.Quit),
        ("?", AppCommand.ShowHelp),
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
        "Tab+r      Umbrella und Submodule neu laden",
        "q          beenden",
        "?          diese Übersicht öffnen oder schließen",
    ];

    public static void Attach(
        Terminal.Gui.ViewBase.View root,
        Action<AppCommand> execute,
        Func<TimeSpan, Func<bool>, object?>? schedule = null,
        Action<object?>? cancel = null)
    {
        var tab = new TabHold();
        object? token = null;

        void SyncTimer()
        {
            if (token is not null)
            {
                cancel?.Invoke(token);
                token = null;
            }

            if (tab.Due is not TimeSpan delay || schedule is null)
            {
                return;
            }

            token = schedule(delay, () =>
            {
                token = null;
                var expired = tab.Expire();
                if (expired.Command is AppCommand command)
                {
                    execute(command);
                }

                return false;
            });
        }

        root.KeyDown += (_, key) =>
        {
            var step = tab.Press(key);
            if (step.Reschedule)
            {
                SyncTimer();
            }

            if (step.Command is AppCommand command)
            {
                execute(command);
            }

            if (step.Handled)
            {
                key.Handled = true;
                return;
            }

            var matched = Match(key);
            if (matched is null)
            {
                return;
            }

            execute(matched.Value);
            key.Handled = true;
        };

        root.KeyUp += (_, key) =>
        {
            var step = tab.Release(key);
            if (!step.Reschedule && step.Command is null && !step.Handled)
            {
                return;
            }

            if (step.Reschedule)
            {
                SyncTimer();
            }

            if (step.Command is AppCommand command)
            {
                execute(command);
            }

            if (step.Handled)
            {
                key.Handled = true;
            }
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

        if (key == Terminal.Gui.Input.Key.R)
        {
            return AppCommand.Reload;
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

public readonly record struct Chord(bool Handled, AppCommand? Command, bool MatchAfter, bool Reschedule)
{
    public static Chord Ignore { get; } = new(false, null, true, false);

    public static Chord Swallow { get; } = new(true, null, false, false);

    public static Chord Run(AppCommand command) => new(true, command, false, true);

    public static Chord Then(AppCommand command) => new(false, command, true, true);

    public static Chord Arm { get; } = new(true, null, false, true);
}

public sealed class TabHold
{
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(180);

    public static readonly TimeSpan Fallback = TimeSpan.FromMilliseconds(700);

    private enum Phase
    {
        Idle,
        Held,
        Grace,
    }

    private Phase _phase = Phase.Idle;

    public TimeSpan? Due { get; private set; }

    public bool Active => _phase != Phase.Idle;

    public Chord Press(Key key)
    {
        if (key == Key.Esc)
        {
            var armed = Active;
            _phase = Phase.Idle;
            Due = null;
            return armed ? Chord.Swallow with { Reschedule = true } : Chord.Swallow;
        }

        if (Active && key == Key.R && !key.IsCtrl && !key.IsAlt && !key.IsShift)
        {
            _phase = Phase.Idle;
            Due = null;
            return Chord.Run(AppCommand.ReloadAll);
        }

        if (Active && key != Key.Tab)
        {
            _phase = Phase.Idle;
            Due = null;
            return Chord.Then(AppCommand.NextBranch);
        }

        if (key == Key.Tab)
        {
            _phase = Phase.Held;
            Due = Fallback;
            return Chord.Arm;
        }

        return Chord.Ignore;
    }

    public Chord Release(Key key)
    {
        if (_phase != Phase.Held || key != Key.Tab)
        {
            return Chord.Ignore;
        }

        _phase = Phase.Grace;
        Due = Grace;
        return Chord.Arm;
    }

    public Chord Expire()
    {
        if (!Active)
        {
            return Chord.Ignore;
        }

        _phase = Phase.Idle;
        Due = null;
        return Chord.Run(AppCommand.NextBranch);
    }
}
