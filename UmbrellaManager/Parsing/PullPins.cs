using System.Text.Json;
using System.Text.RegularExpressions;

namespace UmbrellaManager.Parsing;

using UmbrellaManager.Model;

public readonly record struct PullPin(int Number, string Base, string NewSha, bool Open);

public sealed record PullSnapshot(int OpenCount, bool Truncated, IReadOnlyList<PullPin> Pins);

public static partial class PullPins
{
    public const int OpenLimit = 100;

    public const int MergedLimit = 100;

    public static string CountText(int count, bool truncated)
    {
        if (count <= 0)
        {
            return "keine offenen PRs";
        }

        if (count == 1 && !truncated)
        {
            return "1 offener PR";
        }

        var number = truncated ? count + "+" : count.ToString();
        return number + " offene PRs";
    }

    public static PullSnapshot? Read(string openJson, string mergedJson)
    {
        try
        {
            var open = Parse(openJson, open: true);
            var merged = Parse(string.IsNullOrWhiteSpace(mergedJson) ? "[]" : mergedJson, open: false);
            return new PullSnapshot(open.Count, open.Count >= OpenLimit, [..open.Pins, ..merged.Pins]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static (PullState State, string Label) Describe(string? sha, string? tab, IReadOnlyList<PullPin> pins)
    {
        if (string.IsNullOrEmpty(sha) || string.IsNullOrEmpty(tab))
        {
            return (PullState.None, "");
        }

        var waiting = new List<int>();
        var merged = false;
        foreach (var pin in pins)
        {
            if (!pin.Base.Equals(tab, StringComparison.Ordinal) || !Sha.Same(pin.NewSha, sha))
            {
                continue;
            }

            if (pin.Open)
            {
                waiting.Add(pin.Number);
            }
            else
            {
                merged = true;
            }
        }

        if (waiting.Count > 0)
        {
            waiting.Sort();
            return (PullState.Waiting, "wartet in PR #" + string.Join(", #", waiting));
        }

        if (merged)
        {
            return (PullState.Merged, "ist gemerged");
        }

        return (PullState.None, "");
    }

    public static IReadOnlyList<GraphRow> Apply(IReadOnlyList<GraphRow> rows, string? tab, IReadOnlyList<PullPin> pins)
    {
        var changed = false;
        var next = new List<GraphRow>(rows.Count);
        foreach (var row in rows)
        {
            var note = Describe(row.Sha, tab, pins);
            if (row.Pull != note.State || !string.Equals(row.PullLabel, note.Label, StringComparison.Ordinal))
            {
                changed = true;
                next.Add(row with { Pull = note.State, PullLabel = note.Label });
            }
            else
            {
                next.Add(row);
            }
        }

        return changed ? next : rows;
    }

    private static (int Count, List<PullPin> Pins) Parse(string json, bool open)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Pull-Request-Liste ist kein Array.");
        }

        var pins = new List<PullPin>();
        var count = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            count++;
            if (!item.TryGetProperty("number", out var numberProperty) || !numberProperty.TryGetInt32(out var number) || number <= 0)
            {
                continue;
            }

            var state = item.TryGetProperty("state", out var stateProperty) ? stateProperty.GetString() : null;
            var isOpen = string.Equals(state, "OPEN", StringComparison.OrdinalIgnoreCase);
            var isMerged = string.Equals(state, "MERGED", StringComparison.OrdinalIgnoreCase);
            if (open ? !isOpen : !isMerged)
            {
                continue;
            }

            var basis = item.TryGetProperty("baseRefName", out var baseProperty) ? baseProperty.GetString() : null;
            var body = item.TryGetProperty("body", out var bodyProperty) ? bodyProperty.GetString() : null;
            if (string.IsNullOrEmpty(basis) || string.IsNullOrEmpty(body))
            {
                continue;
            }

            var match = NewPinPattern().Match(body);
            if (!match.Success)
            {
                continue;
            }

            pins.Add(new PullPin(number, basis, match.Groups[1].Value, isOpen));
        }

        return (count, pins);
    }

    [GeneratedRegex(@"Neuer Pin:\s*([0-9a-fA-F]{40})", RegexOptions.CultureInvariant)]
    private static partial Regex NewPinPattern();
}
