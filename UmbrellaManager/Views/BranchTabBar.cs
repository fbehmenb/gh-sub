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
        const string hint = "Tab";
        var content = Math.Max(0, width - hint.Length - 1);
        Painter.Line(this, 0, "", new Attribute(Painter.Secondary, Painter.Paper));
        if (width <= 0)
        {
            return true;
        }

        var tabs = _controller.Branches.Select(branch =>
        {
            var stale = _controller.IsStale(branch);
            var count = _controller.PendingCount(branch);
            var name = stale ? branch + " ↑" : branch;
            if (count > 0)
            {
                name += " (" + count + ")";
            }

            return (Text: name, Stale: stale);
        }).ToArray();
        if (tabs.Length == 0)
        {
            Painter.Put(this, 0, 0, "keine Branches", new Attribute(Painter.Secondary, Painter.Paper), content);
            DrawHint(width, hint);
            return true;
        }

        var longest = 0;
        foreach (var tab in tabs)
        {
            if (tab.Text.Length > longest)
            {
                longest = tab.Text.Length;
            }
        }

        var slot = longest + 2;
        if (content > 0 && slot > content)
        {
            slot = content;
        }

        var selected = Math.Clamp(_controller.BranchIndex, 0, tabs.Length - 1);
        var origin = selected * (slot + 1);
        if (origin < _scroll)
        {
            _scroll = origin;
        }

        if (content > 0 && origin + slot > _scroll + content)
        {
            _scroll = origin + slot - content;
        }

        if (_scroll < 0)
        {
            _scroll = 0;
        }

        for (var index = 0; index < tabs.Length; index++)
        {
            var start = index * (slot + 1);
            DrawCell(start, Fit(tabs[index].Text, slot), index == selected, tabs[index].Stale, content);
            if (index < tabs.Length - 1)
            {
                DrawSeparator(start + slot, content);
            }
        }

        DrawHint(width, hint);
        return true;
    }

    private void DrawCell(int start, string text, bool active, bool stale, int content)
    {
        var viewStart = start - _scroll;
        if (viewStart >= content || text.Length == 0)
        {
            return;
        }

        var skip = viewStart < 0 ? -viewStart : 0;
        if (skip >= text.Length)
        {
            return;
        }

        var piece = text[skip..];
        var x = Math.Max(0, viewStart);
        var room = content - x;
        if (room <= 0)
        {
            return;
        }

        if (piece.Length > room)
        {
            piece = piece[..room];
        }

        var foreground = stale ? Painter.Danger : active ? Painter.Ink : Painter.Secondary;
        var background = active ? Painter.Faint : Painter.Paper;
        Painter.Put(this, x, 0, piece, new Attribute(foreground, background), piece.Length);
    }

    private void DrawSeparator(int start, int content)
    {
        var x = start - _scroll;
        if (x < 0 || x >= content)
        {
            return;
        }

        Painter.Put(this, x, 0, "|", new Attribute(Painter.Secondary, Painter.Paper), 1);
    }

    private void DrawHint(int width, string hint)
    {
        if (width <= hint.Length)
        {
            return;
        }

        Painter.Put(this, width - hint.Length, 0, hint, new Attribute(Painter.Secondary, Painter.Paper), hint.Length);
    }

    private static string Fit(string text, int slot)
    {
        if (slot <= 0)
        {
            return "";
        }

        if (text.Length >= slot)
        {
            return text[..slot];
        }

        var pad = slot - text.Length;
        var left = pad / 2;
        return new string(' ', left) + text + new string(' ', pad - left);
    }
}
