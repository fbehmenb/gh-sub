namespace UmbrellaManager.Input;

public static class KeyMap
{
    public static IReadOnlyList<(string Key, AppCommand Command)> Bindings { get; } =
    [
        ("Left", AppCommand.PreviousBranch),
        ("Right", AppCommand.NextBranch),
        ("Tab", AppCommand.FocusNext),
        ("Up", AppCommand.MoveUp),
        ("Down", AppCommand.MoveDown),
        ("Space", AppCommand.ToggleFolder),
        ("Enter", AppCommand.StagePin),
        ("A", AppCommand.AddModule),
        ("Delete", AppCommand.RemoveModule),
        ("C", AppCommand.CreatePr),
        ("R", AppCommand.Reload),
        ("Q", AppCommand.Quit),
    ];

    public static void Attach(Terminal.Gui.ViewBase.View root, Action<AppCommand> execute)
    {
        root.KeyDown += (_, key) =>
        {
            var command = Match(key);
            if (command is null)
            {
                return;
            }

            execute(command.Value);
            key.Handled = true;
        };
    }

    private static AppCommand? Match(Terminal.Gui.Input.Key key)
    {
        if (key == Terminal.Gui.Input.Key.CursorLeft)
        {
            return AppCommand.PreviousBranch;
        }

        if (key == Terminal.Gui.Input.Key.CursorRight)
        {
            return AppCommand.NextBranch;
        }

        if (key == Terminal.Gui.Input.Key.Tab)
        {
            return AppCommand.FocusNext;
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
            return AppCommand.CreatePr;
        }

        if (key == Terminal.Gui.Input.Key.R)
        {
            return AppCommand.Reload;
        }

        if (key == Terminal.Gui.Input.Key.Q)
        {
            return AppCommand.Quit;
        }

        return null;
    }
}
