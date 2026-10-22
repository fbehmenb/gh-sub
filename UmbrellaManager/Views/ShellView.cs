using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace UmbrellaManager.Views;

using UmbrellaManager.Input;

public sealed class ShellView : Window
{
    public ShellView(SessionController controller, IApplication app)
    {
        Title = "";
        BorderStyle = LineStyle.None;
        ShadowStyle = ShadowStyles.None;
        TabStop = TabBehavior.NoStop;
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
            Width = Dim.Percent(50),
            Height = Dim.Fill(KeyMap.FooterHeight),
        };
        var graph = new CommitGraphPane(controller)
        {
            X = Pos.Right(tree),
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(KeyMap.FooterHeight),
        };
        var help = new HelpPane(controller)
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(KeyMap.FooterHeight),
            Visible = false,
        };
        var setup = new SetupPane(controller)
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(KeyMap.FooterHeight),
            Visible = false,
        };
        var status = new StatusLine(controller)
        {
            X = 0,
            Y = Pos.AnchorEnd(KeyMap.FooterHeight),
            Width = Dim.Fill(),
            Height = KeyMap.FooterHeight,
        };
        var proposal = new PrProposalPane(controller)
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(KeyMap.FooterHeight),
            Visible = false,
        };
        Add(tabs);
        Add(tree);
        Add(graph);
        Add(proposal);
        Add(help);
        Add(setup);
        Add(status);
        KeyMap.Attach(this, controller.Execute);
        controller.QuitRequested += () => app.RequestStop();
        controller.Changed += () =>
        {
            help.Visible = controller.HelpVisible && !controller.SetupOpen;
            setup.Visible = controller.SetupOpen;
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
