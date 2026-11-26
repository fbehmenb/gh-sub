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

        var lines = new List<string>(_controller.AccountLines);
        lines.AddRange(KeyMap.Help);
        for (var index = 0; index < lines.Count && index < Viewport.Height; index++)
        {
            var title = lines[index] is "GitHub" or "Tasten";
            var color = title ? Painter.Ink : Painter.Muted;
            Painter.Line(this, index, lines[index], new Attribute(color, Painter.Paper));
        }

        return true;
    }
}
