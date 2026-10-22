using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

public sealed class PrProposalPane : View
{
    private readonly SessionController _controller;

    public PrProposalPane(SessionController controller)
    {
        _controller = controller;
        CanFocus = false;
        Visible = false;
        _controller.Changed += () => SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var lines = _controller.PrProposal;
        if (lines is null)
        {
            return true;
        }

        for (var index = 0; index < lines.Count && index < Viewport.Height; index++)
        {
            var color = index == 0 ? Painter.Ink : Painter.Muted;
            Painter.Line(this, index, lines[index], new Attribute(color, Painter.Paper));
        }

        return true;
    }
}
