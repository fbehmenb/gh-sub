using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

using UmbrellaManager.Services;

public sealed class PrProposalPane : View
{
    private readonly SessionController _controller;
    private int _frame;

    public PrProposalPane(SessionController controller)
    {
        _controller = controller;
        CanFocus = false;
        Visible = false;
        _controller.Changed += () =>
        {
            Visible = _controller.PrProposal is { Count: > 0 };
            SetNeedsDraw();
        };
    }

    public void Pulse()
    {
        if (!_controller.CheckingRemote && !_controller.SuccessOpen)
        {
            return;
        }

        _frame++;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var lines = _controller.PrProposal;
        if (lines is null || lines.Count == 0 || Viewport.Width < 8 || Viewport.Height < 4)
        {
            return true;
        }

        for (var y = 0; y < Viewport.Height; y++)
        {
            Painter.Line(this, y, "", new Attribute(Painter.Muted, Painter.Paper));
        }

        var checking = _controller.CheckingRemote;
        var success = _controller.SuccessOpen;
        var showBar = checking || success || _controller.PushRunning || _controller.PushDone > 0;
        var longest = 0;
        foreach (var line in lines)
        {
            if (line.Length > longest)
            {
                longest = line.Length;
            }
        }

        var width = Math.Min(Viewport.Width, Math.Max(44, longest + 4));
        var extra = showBar ? 1 : 0;
        var height = Math.Min(Viewport.Height, Math.Max(4, lines.Count + 2 + extra));
        var left = Math.Max(0, (Viewport.Width - width) / 2);
        var top = Math.Max(0, (Viewport.Height - height) / 2);
        var ink = new Attribute(Painter.Ink, Painter.Bar);
        var muted = new Attribute(Painter.Muted, Painter.Bar);
        var ok = new Attribute(Painter.LightGreen, Painter.Bar);
        var danger = new Attribute(Painter.Danger, Painter.Bar);
        Painter.Frame(this, left, top, width, height, new Attribute(Painter.Faint, Painter.Bar));

        var rows = Math.Max(0, height - 2 - extra);
        var shown = Fit(lines, rows);
        var textWidth = Math.Max(1, width - 4);
        for (var index = 0; index < shown.Count; index++)
        {
            var line = shown[index];
            var color = line.Contains("Konflikt", StringComparison.Ordinal)
                ? danger
                : line.StartsWith("Erfolgreich", StringComparison.Ordinal)
                    || line.Contains("Taste schließt", StringComparison.Ordinal)
                    || line.StartsWith("j ", StringComparison.Ordinal)
                    || line.StartsWith("beliebige", StringComparison.Ordinal)
                    ? ok
                    : line.StartsWith(' ')
                        ? muted
                        : ink;
            Painter.Put(this, left + 2, top + 1 + index, line, color, textWidth);
        }

        if (showBar)
        {
            var bar = success
                ? LoadMeter.Fill(_controller.SuccessFraction, textWidth)
                : checking
                    ? LoadMeter.Running(_frame, textWidth)
                    : PinPublisher.ProgressBar(_controller.PushDone, _controller.PushTotal, textWidth);
            Painter.Put(this, left + 2, top + height - 2, bar, ok, textWidth);
        }

        return true;
    }

    private static List<string> Fit(IReadOnlyList<string> lines, int rows)
    {
        if (rows <= 0)
        {
            return [];
        }

        if (lines.Count <= rows)
        {
            return [.. lines];
        }

        if (rows == 1)
        {
            return [lines[^1]];
        }

        var shown = new List<string>(rows);
        shown.AddRange(lines.Take(rows - 1));
        shown.Add(lines[^1]);
        return shown;
    }
}
