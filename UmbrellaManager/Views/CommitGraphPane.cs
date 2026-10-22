using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

using UmbrellaManager.Model;

public sealed class CommitGraphPane : View
{
    private readonly SessionController _controller;
    private int _scroll;

    public CommitGraphPane(SessionController controller)
    {
        _controller = controller;
        CanFocus = true;
        Title = "Commits";
        BorderStyle = LineStyle.Rounded;
        _controller.Changed += () => SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var y = 0;
        if (_controller.Lanes.Count > 1 && Viewport.Height > 1)
        {
            var legend = "Spuren: " + string.Join("  ", _controller.Lanes.Select((lane, index) => $"{index}:{lane}"));
            Painter.Line(this, y, legend, new Attribute(Painter.Muted, Painter.Paper));
            y++;
        }

        if (_controller.Graph.Count == 0)
        {
            Painter.Line(this, y, _controller.GraphMessage, new Attribute(Painter.Muted, Painter.Paper));
            return true;
        }

        var first = y;
        var height = Math.Max(1, Viewport.Height - first);
        KeepVisible(_controller.GraphIndex, height);
        for (var row = 0; row < height; row++)
        {
            var index = _scroll + row;
            if (first + row >= Viewport.Height)
            {
                break;
            }

            if (index >= _controller.Graph.Count)
            {
                Painter.Line(this, first + row, "", new Attribute(Painter.Ink, Painter.Paper));
                continue;
            }

            var commit = _controller.Graph[index];
            var dots = commit.Dots.Length == 0 ? "" : string.Concat(commit.Dots.Select(on => on ? "* " : "  "));
            var text = $"{Sha.Short(commit.Sha)} {dots}{commit.Subject}";
            var selected = index == _controller.GraphIndex && HasFocus;
            Painter.Line(this, first + row, text, AttributeFor(commit.Tone, selected));
        }

        return true;
    }

    private static Attribute AttributeFor(CommitTone tone, bool selected)
    {
        var background = selected ? Painter.Selected : Painter.Paper;
        var foreground = tone switch
        {
            CommitTone.DarkGreen => Painter.DarkGreen,
            CommitTone.LightGreen => Painter.LightGreen,
            _ => Painter.Muted,
        };
        return new Attribute(foreground, background);
    }

    private void KeepVisible(int index, int height)
    {
        if (index < _scroll)
        {
            _scroll = index;
        }

        if (index >= _scroll + height)
        {
            _scroll = index - height + 1;
        }
    }
}
