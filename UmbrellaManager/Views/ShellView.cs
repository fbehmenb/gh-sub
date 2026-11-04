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
        var loading = new LoadingPane(controller)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
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
        Add(loading);
        KeyMap.Attach(this, controller.Execute);
        controller.QuitRequested += () => app.RequestStop();
        object? pulse = null;
        void EnsurePulse()
        {
            if (pulse is not null || !app.Initialized)
            {
                return;
            }

            if (!Moving())
            {
                return;
            }

            pulse = app.AddTimeout(TimeSpan.FromMilliseconds(120), () =>
            {
                if (!Moving())
                {
                    pulse = null;
                    return false;
                }

                if (controller.Loading && !controller.LoadingFailed)
                {
                    loading.Pulse();
                }

                if (controller.CheckingRemote || controller.SuccessOpen)
                {
                    proposal.Pulse();
                }

                return true;
            });
        }

        bool Moving()
        {
            if (controller.CheckingRemote || controller.SuccessOpen)
            {
                return true;
            }

            return controller.Loading && !controller.LoadingFailed;
        }

        void Reveal()
        {
            loading.Visible = false;
            app.ClearScreenNextIteration = true;
            tabs.SetNeedsDraw();
            tree.SetNeedsDraw();
            graph.SetNeedsDraw();
            status.SetNeedsDraw();
            SetNeedsDraw();
        }

        controller.Changed += () =>
        {
            if (controller.Loading)
            {
                loading.Visible = true;
                help.Visible = false;
                setup.Visible = false;
                proposal.Visible = false;
                EnsurePulse();
                return;
            }

            var covered = loading.Visible;
            loading.Visible = false;
            if (covered)
            {
                Reveal();
                app.AddTimeout(TimeSpan.FromMilliseconds(30), () =>
                {
                    Reveal();
                    return false;
                });
            }

            help.Visible = controller.HelpVisible && !controller.SetupOpen;
            setup.Visible = controller.SetupOpen;
            if (controller.CheckingRemote || controller.SuccessOpen)
            {
                EnsurePulse();
            }
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
        Initialized += (_, _) =>
        {
            tree.SetFocus();
            if (controller.Loading)
            {
                loading.Visible = true;
                EnsurePulse();
            }
        };
    }
}
