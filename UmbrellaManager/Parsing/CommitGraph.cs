namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public static class CommitGraph
{
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
            var cell = next[i];
            cells[i] = cell.Symbol == ' ' || cell.Lane < 0
                ? new GraphCell(' ', -1)
                : new GraphCell('|', cell.Lane);
        }

        return cells;
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
            var direction = from > index ? -1 : 1;
            for (var i = from; i != index; i += direction)
            {
                cells[i] = new GraphCell(symbol, from);
            }

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
                for (var i = index + 1; i <= slot; i++)
                {
                    cells[i] = new GraphCell('\\', slot);
                }
            }
        }

        while (columns.Count > 0 && columns[^1] is null)
        {
            columns.RemoveAt(columns.Count - 1);
        }

        return cells.ToArray();
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
