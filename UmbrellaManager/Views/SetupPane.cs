using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

public sealed class SetupPane : View
{
    private readonly SessionController _controller;

    public SetupPane(SessionController controller)
    {
        _controller = controller;
        CanFocus = false;
        Visible = false;
        _controller.Changed += () => SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (!_controller.SetupOpen)
        {
            return true;
        }

        var lines = _controller.SetupLines;
        for (var index = 0; index < lines.Count && index < Viewport.Height; index++)
        {
            var selected = lines[index].StartsWith('>');
            var color = index == 0 || selected ? Painter.Ink : Painter.Muted;
            Painter.Line(this, index, lines[index], new Attribute(color, Painter.Paper));
        }

        return true;
    }
}
