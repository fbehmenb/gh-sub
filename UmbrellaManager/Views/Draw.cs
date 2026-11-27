using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

static class Painter
{
    public static readonly Color Paper = new(0x12, 0x12, 0x1F, 255);

    public static readonly Color Bar = new(0x16, 0x16, 0x28, 255);

    public static readonly Color Ink = new(0xE2, 0xE1, 0xED, 255);

    public static readonly Color Secondary = new(0x66, 0x6C, 0xA6, 255);

    public static readonly Color Muted = new(0xB0, 0xB3, 0xBF, 255);

    public static readonly Color Selected = new(0x1B, 0x1B, 0x33, 255);

    public static readonly Color SelectedQuiet = new(0x17, 0x17, 0x2A, 255);

    public static readonly Color Faint = new(0x2B, 0x2B, 0x40, 255);

    public static readonly Color Pin = new(0x5D, 0xF2, 0xA0, 255);

    public static readonly Color Pending = new(0xFF, 0x9E, 0x3D, 255);

    public static readonly Color Waiting = new(0x5B, 0xC0, 0xEB, 255);

    public static readonly Color LightGreen = new(0x3D, 0xF2, 0x94, 255);

    public static readonly Color CommitSelected = new(0x2C, 0x4A, 0x9A, 255);

    public static readonly Color CommitSelectedQuiet = new(0x22, 0x36, 0x6E, 255);

    public static readonly Color Danger = new(220, 80, 80, 255);

    private static readonly Color[] Lanes =
    [
        new(0x2F, 0x6B, 0xFF),
        new(0xE2, 0x55, 0xF2),
        new(0x3D, 0xE0, 0xFF),
        new(0xA8, 0x55, 0xF7),
        new(0x81, 0x8C, 0xF8),
        new(0xF5, 0xD0, 0xFE),
        new(0x67, 0xE8, 0xF9),
        new(0xC4, 0xB5, 0xFD),
        new(0x93, 0xC5, 0xFD),
    ];

    public static Color Lane(int lane)
    {
        if (lane < 0)
        {
            return Muted;
        }

        return Lanes[lane % Lanes.Length];
    }

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

    public static void Put(View view, int x, int y, string text, Attribute attribute, int maxWidth)
    {
        if (y < 0 || y >= view.Viewport.Height || x >= view.Viewport.Width || maxWidth <= 0)
        {
            return;
        }

        var room = Math.Min(maxWidth, view.Viewport.Width - Math.Max(0, x));
        if (room <= 0)
        {
            return;
        }

        if (x < 0)
        {
            var skip = -x;
            if (skip >= text.Length)
            {
                return;
            }

            text = text[skip..];
            x = 0;
        }

        if (text.Length > room)
        {
            text = text[..room];
        }

        view.SetAttribute(attribute);
        view.Move(x, y);
        view.AddStr(text);
    }

    public static void Frame(View view, int x, int y, int width, int height, Attribute attribute)
    {
        if (width < 2 || height < 2 || x >= view.Viewport.Width || y >= view.Viewport.Height)
        {
            return;
        }

        var horizontal = new string('─', width - 2);
        Put(view, x, y, "┌" + horizontal + "┐", attribute, width);
        var empty = new string(' ', width - 2);
        for (var row = 1; row < height - 1; row++)
        {
            Put(view, x, y + row, "│" + empty + "│", attribute, width);
        }

        Put(view, x, y + height - 1, "└" + horizontal + "┘", attribute, width);
    }
}
