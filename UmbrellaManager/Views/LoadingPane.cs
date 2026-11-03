using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

public static class LoadMeter
{
    public static string Bar(int done, int total, int frame, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        if (total <= 0)
        {
            return Running(frame, width);
        }

        done = Math.Clamp(done, 0, total);
        var label = " " + done + "/" + total;
        if (label.Length >= width)
        {
            return Running(frame, width);
        }

        var barWidth = width - label.Length;
        var filled = (int)Math.Round(barWidth * (done / (double)total));
        filled = Math.Clamp(filled, 0, barWidth);
        var bar = filled >= barWidth
            ? new string('█', barWidth)
            : new string('█', filled) + Running(frame, barWidth - filled);
        return bar + label;
    }

    public static string Running(int frame, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        var block = Math.Clamp(width / 4, 1, width);
        var chars = new char[width];
        Array.Fill(chars, '░');
        var origin = Math.Abs(frame) % width;
        for (var i = 0; i < block; i++)
        {
            chars[(origin + i) % width] = '█';
        }

        return new string(chars);
    }

    public static string Fill(double fraction, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        var filled = (int)Math.Round(width * Math.Clamp(fraction, 0, 1));
        filled = Math.Clamp(filled, 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }
}

public sealed class LoadingPane : View
{
    private readonly SessionController _controller;
    private int _frame;

    public LoadingPane(SessionController controller)
    {
        _controller = controller;
        CanFocus = false;
        Visible = false;
        _controller.Changed += () => SetNeedsDraw();
    }

    public void Pulse()
    {
        if (!_controller.Loading || _controller.LoadingFailed)
        {
            return;
        }

        _frame++;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (!_controller.Loading)
        {
            return true;
        }

        var paper = new Attribute(Painter.Muted, Painter.Paper);
        for (var y = 0; y < Viewport.Height; y++)
        {
            Painter.Line(this, y, "", paper);
        }

        if (Viewport.Width < 12 || Viewport.Height < 5)
        {
            Painter.Line(this, 0, _controller.LoadingMessage, new Attribute(Painter.Ink, Painter.Paper));
            return true;
        }

        var width = Math.Min(Viewport.Width, 56);
        var height = Math.Min(Viewport.Height, 7);
        var left = Math.Max(0, (Viewport.Width - width) / 2);
        var top = Math.Max(0, (Viewport.Height - height) / 2);
        var ink = new Attribute(Painter.Ink, Painter.Bar);
        var muted = new Attribute(Painter.Muted, Painter.Bar);
        var barColor = new Attribute(_controller.LoadingFailed ? Painter.Danger : Painter.LightGreen, Painter.Bar);
        Painter.Frame(this, left, top, width, height, new Attribute(Painter.Faint, Painter.Bar));
        var textWidth = Math.Max(1, width - 4);
        Painter.Put(this, left + 2, top + 1, "Laden", ink, textWidth);
        Painter.Put(this, left + 2, top + 2, _controller.LoadingMessage, ink, textWidth);
        if (!_controller.LoadingFailed && height >= 6)
        {
            var bar = LoadMeter.Bar(_controller.LoadingDone, _controller.LoadingTotal, _frame, textWidth);
            Painter.Put(this, left + 2, top + 4, bar, barColor, textWidth);
        }

        Painter.Put(this, left + 2, top + height - 2, "q beendet", muted, textWidth);
        return true;
    }
}
