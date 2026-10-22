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
    private int _fileScroll;

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
        y = DrawSwitcher(y);
        if (_controller.Body == PreviewBody.Files)
        {
            DrawFiles(y);
            return true;
        }

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

        var height = Math.Max(1, Viewport.Height - y);
        KeepVisible(ref _scroll, _controller.GraphIndex, height);
        for (var row = 0; row < height; row++)
        {
            var line = y + row;
            if (line >= Viewport.Height)
            {
                break;
            }

            var index = _scroll + row;
            if (index >= _controller.Graph.Count)
            {
                Painter.Line(this, line, "", new Attribute(Painter.Ink, Painter.Paper));
                continue;
            }

            DrawCommit(line, _controller.Graph[index], index == _controller.GraphIndex);
        }

        return true;
    }

    private void DrawCommit(int line, GraphRow commit, bool selected)
    {
        var background = selected
            ? HasFocus ? Painter.Selected : Painter.SelectedQuiet
            : Painter.Paper;
        Painter.Line(this, line, "", new Attribute(Painter.Ink, background));
        var x = 0;
        var lane = 0;
        if (commit.Cells is { Count: > 0 } cells)
        {
            foreach (var cell in cells)
            {
                if (cell.Symbol == '*')
                {
                    lane = Math.Max(0, cell.Lane);
                }

                var color = cell.Symbol == '*' ? TextColor(commit, lane) : Painter.Lane(cell.Lane);
                Painter.Put(this, x, line, cell.Symbol.ToString(), new Attribute(color, background), 1);
                x++;
            }

            x++;
        }

        var names = commit.Names is { Count: > 0 } list ? "(" + string.Join(", ", list) + ") " : "";
        var badge = GraphBuilder.Badge(commit.Mark);
        var text = $"{Sha.Short(commit.Sha)} {names}{commit.Subject}";
        if (badge.Length > 0)
        {
            text += "  " + badge;
        }

        Painter.Put(this, x, line, text, new Attribute(TextColor(commit, lane), background), Math.Max(0, Viewport.Width - x));
    }

    private static Color TextColor(GraphRow commit, int lane)
    {
        return commit.Tone switch
        {
            CommitTone.DarkGreen => Painter.DarkGreen,
            CommitTone.LightGreen => Painter.LightGreen,
            _ => Painter.Lane(lane),
        };
    }

    private int DrawSwitcher(int y)
    {
        if (y >= Viewport.Height)
        {
            return y;
        }

        Painter.Line(this, y, "", new Attribute(Painter.Secondary, Painter.Paper));
        var files = _controller.Body == PreviewBody.Files;
        var x = PutTab(y, 0, " Commits ", !files);
        Painter.Put(this, x, y, "|", new Attribute(Painter.Secondary, Painter.Paper), 1);
        PutTab(y, x + 1, " Baum ", files);
        return y + 1;
    }

    private int PutTab(int y, int x, string text, bool active)
    {
        var foreground = active ? Painter.Ink : Painter.Secondary;
        var background = active ? Painter.Faint : Painter.Paper;
        Painter.Put(this, x, y, text, new Attribute(foreground, background), text.Length);
        return x + text.Length;
    }

    private void DrawFiles(int y)
    {
        if (_controller.FileMessage.Length > 0 && y < Viewport.Height)
        {
            Painter.Line(this, y, _controller.FileMessage, new Attribute(Painter.Muted, Painter.Paper));
            y++;
        }

        var rows = _controller.Files;
        if (rows.Count == 0)
        {
            for (; y < Viewport.Height; y++)
            {
                Painter.Line(this, y, "", new Attribute(Painter.Ink, Painter.Paper));
            }

            return;
        }

        var height = Math.Max(1, Viewport.Height - y);
        KeepVisible(ref _fileScroll, _controller.FileIndex, height);
        for (var row = 0; row < height; row++)
        {
            var line = y + row;
            if (line >= Viewport.Height)
            {
                break;
            }

            var index = _fileScroll + row;
            if (index >= rows.Count)
            {
                Painter.Line(this, line, "", new Attribute(Painter.Ink, Painter.Paper));
                continue;
            }

            var item = rows[index];
            var prefix = item.IsFolder ? (item.Expanded ? "v " : "> ") : "  ";
            var text = new string(' ', item.Depth * 2) + prefix + item.Name;
            var selected = index == _controller.FileIndex;
            var foreground = selected && !HasFocus ? Painter.Secondary : Painter.Ink;
            var background = !selected
                ? Painter.Paper
                : HasFocus ? Painter.Selected : Painter.SelectedQuiet;
            Painter.Line(this, line, text, new Attribute(foreground, background));
        }
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
            return Module(row, "vorgemerkt", Painter.LightGreen, staged);
        }

        if (row.Sha is not null)
        {
            return Module(row, "gepinnt", Painter.DarkGreen, row.Sha);
        }

        return Module(row, "kein Pin", Painter.Secondary, null);
    }

    private static Head[] Module(VisibleRow row, string badge, Color color, string? sha)
    {
        return
        [
            new Head(row.Path, Painter.Muted, null),
            new Head(row.Name, Painter.Ink, null),
            new Head(badge, color, Join(row.Branch, sha is null ? "" : Sha.Short(sha))),
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
