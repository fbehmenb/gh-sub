namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public static class CommitGraph
{
    public readonly record struct Glyph(char Symbol, int Lane);

    public static IReadOnlyList<GraphRow> Apply(IReadOnlyList<GraphRow> rows)
    {
        var columns = new List<string?>();
        var result = new List<GraphRow>(rows.Count);
        foreach (var row in rows)
        {
            var cells = Step(columns, row);
            result.Add(row with { Cells = cells });
        }

        return result;
    }

    public static IReadOnlyList<GraphCell> Link(IReadOnlyList<GraphCell>? next)
    {
        if (next is null || next.Count == 0)
        {
            return [];
        }

        var cells = new GraphCell[next.Count];
        for (var i = 0; i < next.Count; i++)
        {
            cells[i] = Continues(next, i)
                ? new GraphCell('|', next[i].Lane)
                : new GraphCell(' ', -1);
        }

        return cells;
    }

    public static IReadOnlyList<Glyph> Paint(IReadOnlyList<GraphCell>? cells)
    {
        if (cells is null || cells.Count == 0)
        {
            return [];
        }

        var star = Star(cells);
        var right = star < 0 ? -1 : Edge(cells, star, 1);
        var left = star < 0 ? -1 : Edge(cells, star, -1);
        var rightLane = right >= 0 ? cells[right].Lane : -1;
        var leftLane = left >= 0 ? cells[left].Lane : -1;
        var glyphs = new List<Glyph>(cells.Count * 2);
        for (var i = 0; i < cells.Count; i++)
        {
            var onRight = right >= 0 && i > star && i <= right;
            var onLeft = left >= 0 && i < star && i >= left;
            var stem = Stem(cells[i].Symbol, i, star, right, left, onRight, onLeft);
            glyphs.Add(new Glyph(stem, StemLane(cells[i].Lane, stem, onLeft, leftLane, rightLane)));
            var bridge = (right >= 0 && i >= star && i < right) || (left >= 0 && i >= left && i < star);
            var bridgeLane = bridge ? (i < star ? leftLane : rightLane) : -1;
            glyphs.Add(new Glyph(bridge ? '─' : ' ', bridgeLane));
        }

        return glyphs;
    }

    public static IReadOnlyList<Glyph> PaintLink(IReadOnlyList<GraphCell>? cells)
    {
        var glyphs = new List<Glyph>();
        foreach (var cell in Link(cells))
        {
            glyphs.Add(new Glyph(cell.Symbol == '|' ? '│' : ' ', cell.Lane));
            glyphs.Add(new Glyph(' ', -1));
        }

        return glyphs;
    }

    private static GraphCell[] Step(List<string?> columns, GraphRow row)
    {
        var parents = Distinct(row.Parents);
        var index = IndexOf(columns, row.Sha);
        if (index < 0)
        {
            index = columns.FindIndex(static sha => sha is null);
            if (index < 0)
            {
                index = columns.Count;
                columns.Add(row.Sha);
            }
            else
            {
                columns[index] = row.Sha;
            }
        }

        var merges = new List<int>();
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i] is not null && Sha.Same(columns[i], row.Sha))
            {
                merges.Add(i);
            }
        }

        var cells = new List<GraphCell>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            if (i == index)
            {
                cells.Add(new GraphCell('*', index));
            }
            else if (columns[i] is not null)
            {
                cells.Add(new GraphCell('|', i));
            }
            else
            {
                cells.Add(new GraphCell(' ', -1));
            }
        }

        foreach (var from in merges)
        {
            if (from == index)
            {
                continue;
            }

            var symbol = from > index ? '/' : '\\';
            Connect(cells, from, index, symbol, from);
            columns[from] = null;
        }

        columns[index] = parents.Count > 0 ? parents[0] : null;
        for (var parent = 1; parent < parents.Count; parent++)
        {
            var slot = -1;
            for (var i = index + 1; i < columns.Count; i++)
            {
                if (columns[i] is null)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                columns.Add(parents[parent]);
                cells.Add(new GraphCell('\\', cells.Count));
            }
            else
            {
                columns[slot] = parents[parent];
                Connect(cells, slot, index, '\\', slot);
            }
        }

        while (columns.Count > 0 && columns[^1] is null)
        {
            columns.RemoveAt(columns.Count - 1);
        }

        return cells.ToArray();
    }

    private static void Connect(List<GraphCell> cells, int start, int end, char corner, int lane)
    {
        var direction = start < end ? 1 : -1;
        for (var i = start; i != end; i += direction)
        {
            if (i == start)
            {
                cells[i] = new GraphCell(corner, lane);
                continue;
            }

            if (cells[i].Symbol == '*')
            {
                continue;
            }

            cells[i] = cells[i].Symbol is '|' or '+'
                ? new GraphCell('+', cells[i].Lane)
                : new GraphCell('-', lane);
        }
    }

    private static bool Continues(IReadOnlyList<GraphCell> cells, int index)
    {
        var symbol = cells[index].Symbol;
        if (symbol is '*' or '|' or '+')
        {
            return true;
        }

        if (symbol is not ('/' or '\\'))
        {
            return false;
        }

        var star = Star(cells);
        if (star < 0)
        {
            return false;
        }

        if (index == Edge(cells, star, 1))
        {
            return symbol == '/';
        }

        if (index == Edge(cells, star, -1))
        {
            return symbol == '\\';
        }

        return false;
    }

    private static char Stem(char symbol, int index, int star, int right, int left, bool onRight, bool onLeft)
    {
        if (index == star || symbol == '*')
        {
            return '●';
        }

        if (index == right)
        {
            return symbol == '/' ? '╯' : '╮';
        }

        if (index == left)
        {
            return symbol == '/' ? '╭' : '╰';
        }

        if (onRight || onLeft)
        {
            return symbol is '|' or '+' ? '┼' : '─';
        }

        return symbol is '|' or '+' ? '│' : ' ';
    }

    private static int StemLane(int lane, char stem, bool onLeft, int leftLane, int rightLane)
    {
        if (stem is '●' or '│' or '┼' or ' ')
        {
            return lane;
        }

        return onLeft ? leftLane : rightLane;
    }

    private static int Edge(IReadOnlyList<GraphCell> cells, int star, int direction)
    {
        var edge = -1;
        for (var i = star + direction; i >= 0 && i < cells.Count; i += direction)
        {
            switch (cells[i].Symbol)
            {
                case '\\' or '/' or '+' or '-':
                    edge = i;
                    break;
                case '|':
                    break;
                default:
                    return edge;
            }
        }

        return edge;
    }

    private static int Star(IReadOnlyList<GraphCell> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (cells[i].Symbol == '*')
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> Distinct(IReadOnlyList<string>? parents)
    {
        var list = new List<string>();
        if (parents is null)
        {
            return list;
        }

        foreach (var parent in parents)
        {
            if (string.IsNullOrEmpty(parent) || list.Any(sha => Sha.Same(sha, parent)))
            {
                continue;
            }

            list.Add(parent);
        }

        return list;
    }

    private static int IndexOf(List<string?> columns, string sha)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i] is not null && Sha.Same(columns[i], sha))
            {
                return i;
            }
        }

        return -1;
    }
}
