using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

using UmbrellaManager.Model;
using UmbrellaManager.Parsing;

public sealed class CommitGraphPane : View
{
    private readonly SessionController _controller;
    private int _scroll;

    public CommitGraphPane(SessionController controller)
    {
        _controller = controller;
        CanFocus = true;
        TabStop = TabBehavior.NoStop;
        BorderStyle = LineStyle.None;
        _controller.Changed += () => SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var y = DrawHeader();
        if (_controller.Graph.Count == 0)
        {
            if (y < Viewport.Height)
            {
                Painter.Line(this, y, _controller.GraphMessage, new Attribute(Painter.Muted, Painter.Paper));
                y++;
            }

            for (; y < Viewport.Height; y++)
            {
                Painter.Line(this, y, "", new Attribute(Painter.Ink, Painter.Paper));
            }

            return true;
        }

        var count = _controller.Graph.Count;
        var visualCount = count * 2 - 1;
        var height = Math.Max(1, Viewport.Height - y);
        KeepVisible(ref _scroll, _controller.GraphIndex * 2, height);
        for (var row = 0; row < height; row++)
        {
            var line = y + row;
            if (line >= Viewport.Height)
            {
                break;
            }

            var visual = _scroll + row;
            if (visual >= visualCount)
            {
                Painter.Line(this, line, "", new Attribute(Painter.Ink, Painter.Paper));
                continue;
            }

            var index = visual / 2;
            if (visual % 2 == 0)
            {
                DrawCommit(line, _controller.Graph[index], index == _controller.GraphIndex);
            }
            else
            {
                DrawLink(line, _controller.Graph[index + 1]);
            }
        }

        return true;
    }

    private void DrawLink(int line, GraphRow next)
    {
        Painter.Line(this, line, "", new Attribute(Painter.Ink, Painter.Paper));
        var x = 0;
        foreach (var glyph in CommitGraph.PaintLink(next.Cells))
        {
            if (glyph.Symbol != ' ')
            {
                Painter.Put(this, x, line, glyph.Symbol.ToString(), new Attribute(Painter.Lane(glyph.Lane), Painter.Paper), 1);
            }

            x++;
        }
    }

    private void DrawCommit(int line, GraphRow commit, bool selected)
    {
        var background = selected
            ? HasFocus ? Painter.CommitSelected : Painter.CommitSelectedQuiet
            : Painter.Paper;
        var body = MessageColor(commit, selected);
        Painter.Line(this, line, "", new Attribute(body, background));
        var x = 0;
        if (commit.Cells is { Count: > 0 } cells)
        {
            foreach (var glyph in CommitGraph.Paint(cells))
            {
                if (glyph.Symbol != ' ')
                {
                    var color = selected ? Painter.Ink : Painter.Lane(glyph.Lane);
                    Painter.Put(this, x, line, glyph.Symbol.ToString(), new Attribute(color, background), 1);
                }

                x++;
            }

            x++;
        }

        var stamp = GraphBuilder.Stamp(commit.Tone);
        Painter.Put(this, x, line, stamp, new Attribute(body, background), stamp.Length);
        x += stamp.Length;

        var names = commit.Names is { Count: > 0 } list ? "(" + string.Join(", ", list) + ") " : "";
        var badge = GraphBuilder.Badge(commit.Mark);
        var text = $"{Sha.Short(commit.Sha)} {names}{commit.Subject}";
        if (badge.Length > 0)
        {
            text += "  " + badge;
        }

        Painter.Put(this, x, line, text, new Attribute(body, background), Math.Max(0, Viewport.Width - x));
    }

    private static int StarLane(GraphRow commit)
    {
        if (commit.Cells is null)
        {
            return 0;
        }

        foreach (var cell in commit.Cells)
        {
            if (cell.Symbol == '*')
            {
                return Math.Max(0, cell.Lane);
            }
        }

        return 0;
    }

    private static Color MessageColor(GraphRow commit, bool selected)
    {
        return commit.Tone switch
        {
            CommitTone.DarkGreen => Painter.Pin,
            CommitTone.LightGreen => Painter.Pending,
            _ => selected ? Painter.Ink : Painter.Lane(StarLane(commit)),
        };
    }

    private int DrawHeader()
    {
        var lines = Header();
        var count = Viewport.Height <= 1 ? 0 : Math.Min(lines.Length, Viewport.Height - 1);
        for (var index = 0; index < count; index++)
        {
            var line = lines[index];
            Painter.Line(this, index, "", new Attribute(line.Color, Painter.Paper));
            Painter.Put(this, 0, index, line.Text, new Attribute(line.Color, Painter.Paper), line.Text.Length);
            if (line.Tail is null)
            {
                continue;
            }

            var x = line.Text.Length == 0 ? 0 : line.Text.Length + 2;
            Painter.Put(this, x, index, line.Tail, new Attribute(Painter.Secondary, Painter.Paper), Math.Max(0, Viewport.Width - x));
        }

        return count;
    }

    private Head[] Header()
    {
        if (_controller.Selected is not { } row)
        {
            return
            [
                new Head(_controller.Branch ?? "", Painter.Secondary, null),
                new Head("", Painter.Ink, null),
                new Head("", Painter.Secondary, null),
            ];
        }

        if (row.IsFolder && row.Sha is null)
        {
            return
            [
                new Head(row.Path, Painter.Muted, null),
                new Head(row.Name, Painter.Ink, null),
                new Head("Ordner", Painter.Secondary, null),
            ];
        }

        var staged = _controller.StagedPin(row.Path);
        if (staged is not null)
        {
            return Module(row, "vorgemerkt", Painter.Pending, staged);
        }

        if (row.Sha is not null)
        {
            return Module(row, "gepinnt", Painter.Pin, row.Sha);
        }

        return Module(row, "kein Pin", Painter.Secondary, null);
    }

    private static Head[] Module(VisibleRow row, string badge, Color color, string? sha)
    {
        return
        [
            new Head(row.Name, Painter.Ink, null),
            new Head(badge, color, Join(row.Branch, sha is null ? "" : Sha.Short(sha))),
            new Head("", Painter.Ink, null),
        ];
    }

    private static string Join(string? branch, string sha)
    {
        if (string.IsNullOrEmpty(branch))
        {
            return sha;
        }

        return sha.Length == 0 ? branch : branch + "  " + sha;
    }

    private static void KeepVisible(ref int scroll, int index, int height)
    {
        if (height <= 0)
        {
            return;
        }

        if (index < scroll)
        {
            scroll = index;
        }

        if (index >= scroll + height)
        {
            scroll = index - height + 1;
        }
    }

    private readonly record struct Head(string Text, Color Color, string? Tail);
}
