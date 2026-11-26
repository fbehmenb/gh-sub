namespace UmbrellaManager.Views;

public static class LineWrap
{
    public static IReadOnlyList<string> Split(string text, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
        {
            return [];
        }

        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            if (text.Length - start <= width)
            {
                lines.Add(text[start..]);
                break;
            }

            var window = text.LastIndexOf(' ', start + width - 1, width);
            if (window <= start)
            {
                lines.Add(text.Substring(start, width));
                start += width;
                continue;
            }

            lines.Add(text[start..window]);
            start = window + 1;
        }

        return lines;
    }
}
