using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

using UmbrellaManager.Input;

public sealed class HelpPane : View
{
    private readonly SessionController _controller;

    public HelpPane(SessionController controller)
    {
        _controller = controller;
        CanFocus = false;
        Visible = false;
        _controller.Changed += () => SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (!_controller.HelpVisible)
        {
            return true;
        }

        for (var index = 0; index < KeyMap.Help.Count && index < Viewport.Height; index++)
        {
            var color = index == 0 ? Painter.Ink : Painter.Muted;
            Painter.Line(this, index, KeyMap.Help[index], new Attribute(color, Painter.Paper));
        }

        return true;
    }
}
