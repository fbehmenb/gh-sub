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
        var proposal = new PrProposalPane(controller)
        {
            X = 0,
            Y = Pos.AnchorEnd(8),
            Width = Dim.Fill(),
            Height = 7,
            Visible = false,
        };
        Add(tabs);
        Add(tree);
        Add(graph);
        Add(proposal);
        Add(status);
        KeyMap.Attach(this, controller.Execute);
        controller.QuitRequested += () => app.RequestStop();
        controller.Changed += () =>
        {
            var count = controller.PrProposal?.Count ?? 0;
            proposal.Visible = count > 0;
            var height = Math.Clamp(count, 1, 16);
            proposal.Height = height;
            proposal.Y = Pos.AnchorEnd(height + 1);
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
