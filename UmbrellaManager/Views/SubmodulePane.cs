using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace UmbrellaManager.Views;

public sealed class SubmodulePane : View
{
    private readonly SessionController _controller;
    private int _scroll;

    public SubmodulePane(SessionController controller)
    {
        _controller = controller;
        CanFocus = true;
        Title = "Submodule";
        BorderStyle = LineStyle.Rounded;
        _controller.Changed += () => SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var rows = _controller.Rows;
        if (rows.Count == 0)
        {
            Painter.Line(this, 0, "keine Submodule", new Attribute(Painter.Muted, Painter.Paper));
            return true;
        }

        KeepVisible(_controller.TreeIndex, Viewport.Height);
        for (var y = 0; y < Viewport.Height; y++)
        {
            var index = _scroll + y;
            if (index >= rows.Count)
            {
                Painter.Line(this, y, "", new Attribute(Painter.Ink, Painter.Paper));
                continue;
            }

            var row = rows[index];
            var prefix = row.IsFolder ? (row.Expanded ? "v " : "> ") : "  ";
            var mark = "";
            if (_controller.IsProduction && !row.IsFolder && _controller.PinMarks.TryGetValue(row.Path, out var pin))
            {
                mark = "  " + pin;
            }

            var text = new string(' ', row.Depth * 2) + prefix + row.Name + mark;
            var selected = index == _controller.TreeIndex && HasFocus;
            var attribute = selected
                ? new Attribute(Painter.Ink, Painter.Selected)
                : new Attribute(Painter.Ink, Painter.Paper);
            Painter.Line(this, y, text, attribute);
        }

        return true;
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
