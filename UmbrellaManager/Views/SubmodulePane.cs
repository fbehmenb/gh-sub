using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

using UmbrellaManager.Model;

public sealed class SubmodulePane : View
{
    private readonly SessionController _controller;
    private int _scroll;

    public SubmodulePane(SessionController controller)
    {
        _controller = controller;
        CanFocus = true;
        TabStop = TabBehavior.NoStop;
        BorderStyle = LineStyle.None;
        _controller.Changed += () => SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var secondary = new Attribute(Painter.Secondary, Painter.Paper);
        Painter.Line(this, 0, _controller.Branch ?? "", secondary);
        if (Viewport.Height < 2)
        {
            return true;
        }

        var columns = Columns(width);
        Painter.Line(this, 1, Fit("Name", columns.Name) + Fit("Pin", columns.Pin) + Fit("Branch", columns.Branch), secondary);
        var rows = _controller.Rows;
        var body = Math.Max(0, Viewport.Height - 2);
        if (rows.Count == 0)
        {
            if (body > 0)
            {
                Painter.Line(this, 2, "keine Submodule", new Attribute(Painter.Muted, Painter.Paper));
            }

            for (var y = 3; y < Viewport.Height; y++)
            {
                Painter.Line(this, y, "", new Attribute(Painter.Ink, Painter.Paper));
            }

            return true;
        }

        KeepVisible(_controller.TreeIndex, body);
        for (var row = 0; row < body; row++)
        {
            var index = _scroll + row;
            if (index >= rows.Count)
            {
                Painter.Line(this, row + 2, "", new Attribute(Painter.Ink, Painter.Paper));
                continue;
            }

            var item = rows[index];
            var selected = index == _controller.TreeIndex;
            Painter.Line(this, row + 2, Line(item, columns), AttributeFor(selected, HasFocus));
        }

        return true;
    }

    private string Line(VisibleRow row, (int Name, int Pin, int Branch) columns)
    {
        var prefix = row.IsFolder ? (row.Expanded ? "v " : "> ") : "  ";
        var name = new string(' ', row.Depth * 2) + prefix + row.Name;
        var pin = row.Sha is null ? "" : Sha.Short(row.Sha);
        var branch = _controller.DisplayedBranch(row);
        if (_controller.IsProduction && row.Sha is not null && _controller.PinMarks.TryGetValue(row.Path, out var mark))
        {
            branch = branch.Length == 0 ? mark : branch + " " + mark;
        }

        return Fit(name, columns.Name) + Fit(pin, columns.Pin) + Fit(branch, columns.Branch);
    }

    private static Attribute AttributeFor(bool selected, bool focused)
    {
        if (!selected)
        {
            return new Attribute(Painter.Ink, Painter.Paper);
        }

        return focused
            ? new Attribute(Painter.Ink, Painter.Selected)
            : new Attribute(Painter.Secondary, Painter.SelectedQuiet);
    }

    private static (int Name, int Pin, int Branch) Columns(int width)
    {
        if (width < 20)
        {
            return (width, 0, 0);
        }

        var pin = 9;
        var branch = Math.Min(22, Math.Max(8, width / 5));
        var name = width - pin - branch;
        if (name < 8)
        {
            branch = Math.Max(0, width - pin - 8);
            name = width - pin - branch;
        }

        return (Math.Max(0, name), pin, Math.Max(0, branch));
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        return text.Length > width ? text[..width] : text.PadRight(width);
    }

    private void KeepVisible(int index, int height)
    {
        if (height <= 0)
        {
            return;
        }

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
