namespace OrbitConvert.Core.Conversion;

/// <summary>Parses page selections such as "1-3, 5, 8-" into zero-based page indexes.</summary>
public static class PageRange
{
    public static IReadOnlyList<int> Parse(string? range, int pageCount)
    {
        if (pageCount <= 0)
        {
            throw new ConversionException("The document has no pages.");
        }
        if (string.IsNullOrWhiteSpace(range) || range.Trim() is "*" or "all")
        {
            return Enumerable.Range(0, pageCount).ToList();
        }

        var pages = new List<int>();
        var seen = new HashSet<int>();
        foreach (var raw in range.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int start, end;
            var dash = raw.IndexOf('-');
            if (dash < 0)
            {
                start = end = ParsePage(raw, raw, pageCount);
            }
            else
            {
                var left = raw[..dash].Trim();
                var right = raw[(dash + 1)..].Trim();
                start = left.Length == 0 ? 1 : ParsePage(left, raw, pageCount);
                end = right.Length == 0 ? pageCount : ParsePage(right, raw, pageCount);
                if (end < start)
                {
                    throw new ConversionException($"Invalid page range '{raw}': the end is before the start.");
                }
            }
            for (var p = start; p <= end; p++)
            {
                if (seen.Add(p - 1))
                {
                    pages.Add(p - 1);
                }
            }
        }

        if (pages.Count == 0)
        {
            throw new ConversionException("The page selection is empty.");
        }
        return pages;
    }

    private static int ParsePage(string value, string token, int pageCount)
    {
        if (!int.TryParse(value, out var page) || page < 1)
        {
            throw new ConversionException($"Invalid page number in '{token}'.");
        }
        if (page > pageCount)
        {
            throw new ConversionException($"Page {page} does not exist; the document has {pageCount} page(s).");
        }
        return page;
    }

    /// <summary>Formats a selection for file names, e.g. "pages-1-3".</summary>
    public static string ToSuffix(string? range) =>
        string.IsNullOrWhiteSpace(range)
            ? "-pages"
            : "-pages-" + new string(range.Where(c => char.IsDigit(c) || c is '-' or ',').ToArray()).Replace(',', '_');
}
