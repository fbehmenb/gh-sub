using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

static class Painter
{
    public static readonly Color Paper = new(18, 18, 18, 255);

    public static readonly Color Ink = new(220, 220, 220, 255);

    public static readonly Color Muted = new(150, 150, 150, 255);

    public static readonly Color Selected = new(48, 48, 72, 255);

    public static readonly Color DarkGreen = new(20, 130, 55, 255);

    public static readonly Color LightGreen = new(150, 230, 160, 255);

    public static void Line(View view, int y, string text, Attribute attribute)
    {
        var width = view.Viewport.Width;
        if (y < 0 || y >= view.Viewport.Height || width <= 0)
        {
            return;
        }

        if (text.Length > width)
        {
            text = text[..width];
        }
        else if (text.Length < width)
        {
            text = text.PadRight(width);
        }

        view.SetAttribute(attribute);
        view.Move(0, y);
        view.AddStr(text);
    }
}
