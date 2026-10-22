using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

public sealed class StatusLine : View
{
    private readonly SessionController _controller;

    public StatusLine(SessionController controller)
    {
        _controller = controller;
        CanFocus = false;
        Height = 1;
        _controller.Changed += () => SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var module = _controller.Selected is { IsFolder: false } row
            ? row.Name + " @ " + Model.Sha.Short(row.Sha) + "  ·  "
            : "";
        var text = module + _controller.Message + "  |  ←→ Tab  Tab Fokus  ↑↓ Leertaste Enter a Entf c r q";
        Painter.Line(this, 0, text, new Attribute(Painter.Ink, Painter.Paper));
        return true;
    }
}
