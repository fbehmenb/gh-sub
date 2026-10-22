using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

using UmbrellaManager.Input;

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
        var hint = KeyMap.HelpHint;
        var width = Viewport.Width;
        var bar = new Attribute(Painter.Ink, Painter.Bar);
        if (width <= hint.Length)
        {
            Painter.Line(this, 0, hint, new Attribute(Painter.Secondary, Painter.Bar));
            return true;
        }

        var room = width - hint.Length - 1;
        var left = Left();
        if (left.Length > room)
        {
            left = left[..room];
        }

        Painter.Line(this, 0, left.PadRight(width), bar);
        Painter.Put(this, width - hint.Length, 0, hint, new Attribute(Painter.Secondary, Painter.Bar), hint.Length);
        return true;
    }

    private string Left()
    {
        var rows = _controller.Rows;
        var position = rows.Count == 0 ? "" : _controller.TreeIndex + 1 + "/" + rows.Count;
        var module = _controller.Selected switch
        {
            { IsFolder: false, Sha: not null } row => row.Name + " @ " + Model.Sha.Short(row.Sha),
            { } row => row.Name,
            _ => "",
        };
        return string.Join("  ·  ", new[] { module, _controller.Message, position }.Where(part => part.Length > 0));
    }
}
