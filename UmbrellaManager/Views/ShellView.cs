using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace UmbrellaManager.Views;

using UmbrellaManager.Input;

public sealed class ShellView : Window
{
    public ShellView(SessionController controller, IApplication app)
    {
        Title = "umbrella-manager-local";
        var tabs = new BranchTabBar(controller)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };
        var tree = new SubmodulePane(controller)
        {
            X = 0,
            Y = 1,
            Width = Dim.Percent(40),
            Height = Dim.Fill(1),
        };
        var graph = new CommitGraphPane(controller)
        {
            X = Pos.Right(tree),
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };
        var status = new StatusLine(controller)
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
        };
        Add(tabs);
        Add(tree);
        Add(graph);
        Add(status);
        KeyMap.Attach(this, controller.Execute);
        controller.QuitRequested += () => app.RequestStop();
        controller.Changed += () =>
        {
            if (controller.Focus == FocusPane.Graph)
            {
                if (!graph.HasFocus)
                {
                    graph.SetFocus();
                }
            }
            else if (!tree.HasFocus)
            {
                tree.SetFocus();
            }
        };
        Initialized += (_, _) => tree.SetFocus();
    }
}
