namespace UmbrellaManager.Parsing;

public sealed record StagingPins(string? Current, IReadOnlyList<string> Pins);

public static class StagingHistory
{
    public static IReadOnlyList<string> Parse(string raw)
    {
        var pins = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith(":160000 ", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            Add(pins, seen, parts[2]);
            Add(pins, seen, parts[3]);
        }

        return pins;
    }

    public static bool IsPromotable(bool wasStagingPin, string? compareStatus)
    {
        return wasStagingPin && string.Equals(compareStatus, "ahead", StringComparison.Ordinal);
    }

    private static void Add(List<string> pins, HashSet<string> seen, string value)
    {
        if (!IsCommit(value) || !seen.Add(value))
        {
            return;
        }

        pins.Add(value);
    }

    private static bool IsCommit(string value)
    {
        if (value.Length != 40)
        {
            return false;
        }

        var zeros = true;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }

            if (character != '0')
            {
                zeros = false;
            }
        }

        return !zeros;
    }
}
