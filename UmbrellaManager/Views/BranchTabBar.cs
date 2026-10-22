using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

public sealed class BranchTabBar : View
{
    private readonly SessionController _controller;
    private int _scroll;

    public BranchTabBar(SessionController controller)
    {
        _controller = controller;
        CanFocus = false;
        Height = 1;
        _controller.Changed += () => SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var labels = _controller.Branches.Select((branch, index) =>
        {
            var text = index == _controller.BranchIndex ? $"[{branch}]" : branch;
            return " " + text + " ";
        }).ToArray();
        if (labels.Length == 0)
        {
            Painter.Line(this, 0, "keine Branches", new Attribute(Painter.Muted, Painter.Paper));
            return true;
        }

        var selectedWidth = labels[_controller.BranchIndex].Length;
        var used = 0;
        for (var i = 0; i < _controller.BranchIndex; i++)
        {
            used += labels[i].Length;
        }

        if (used < _scroll)
        {
            _scroll = used;
        }

        if (used + selectedWidth > _scroll + width)
        {
            _scroll = used + selectedWidth - width;
        }

        var line = "";
        var skipped = 0;
        foreach (var label in labels)
        {
            if (skipped + label.Length <= _scroll)
            {
                skipped += label.Length;
                continue;
            }

            var start = Math.Max(0, _scroll - skipped);
            line += label[start..];
            skipped += label.Length;
            if (line.Length >= width)
            {
                break;
            }
        }

        Painter.Line(this, 0, line, new Attribute(Painter.Ink, Painter.Paper));
        return true;
    }
}
