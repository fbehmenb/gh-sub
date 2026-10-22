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

    public static readonly Color DarkGreen = new(20, 130, 55, 255);

    public static readonly Color LightGreen = new(0x3D, 0xF2, 0x94, 255);

    public static readonly Color Danger = new(220, 80, 80, 255);

    private static readonly Color[] Lanes =
    [
        new(0x3D, 0xF2, 0x94),
        new(0xE0, 0xAF, 0x68),
        new(0x7A, 0xA2, 0xF7),
        new(0xF7, 0x76, 0x8E),
        new(0xBB, 0x9A, 0xF7),
        new(0x7D, 0xCF, 0xFF),
        new(0xFF, 0x9E, 0x64),
        new(0x4F, 0xD6, 0xBE),
        new(0xE2, 0xE1, 0xED),
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
