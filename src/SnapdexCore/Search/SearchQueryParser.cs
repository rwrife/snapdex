using System.Globalization;
using System.Text;

namespace SnapdexCore.Search;

public sealed class SearchQueryParser
{
    public SearchQueryParseResult Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return SearchQueryParseResult.Ok(new SearchQuery(false, null, Array.Empty<QueryFilter>()));
        }

        var tokenization = Tokenize(input);
        if (!tokenization.Success)
        {
            return SearchQueryParseResult.Fail(tokenization.Error!);
        }

        var tokens = tokenization.Tokens!;
        var filters = new List<QueryFilter>();

        var parseVisual = ParseVisualPrefix(tokens);
        if (!parseVisual.Success)
        {
            return SearchQueryParseResult.Fail(parseVisual.Error!);
        }

        var isVisual = parseVisual.IsVisualQuery;
        var visualText = parseVisual.VisualQueryText;
        var startIndex = parseVisual.NextTokenIndex;

        for (var i = startIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var colonIndex = token.IndexOf(':');

            if (colonIndex <= 0)
            {
                var filenameText = ParseTextValue(token);
                if (!filenameText.Success)
                {
                    return SearchQueryParseResult.Fail(filenameText.Error!);
                }

                filters.Add(new TextFilter(QueryField.Filename, filenameText.Value!));
                continue;
            }

            var key = token[..colonIndex].Trim().ToLowerInvariant();
            var rawValue = token[(colonIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return SearchQueryParseResult.Fail($"Missing value for filter '{key}:'.");
            }

            switch (key)
            {
                case "camera":
                {
                    var parsed = ParseTextValue(rawValue);
                    if (!parsed.Success)
                    {
                        return SearchQueryParseResult.Fail(parsed.Error!);
                    }

                    filters.Add(new TextFilter(QueryField.Camera, parsed.Value!));
                    break;
                }
                case "lens":
                {
                    var parsed = ParseTextValue(rawValue);
                    if (!parsed.Success)
                    {
                        return SearchQueryParseResult.Fail(parsed.Error!);
                    }

                    filters.Add(new TextFilter(QueryField.Lens, parsed.Value!));
                    break;
                }
                case "folder":
                {
                    var parsed = ParseTextValue(rawValue);
                    if (!parsed.Success)
                    {
                        return SearchQueryParseResult.Fail(parsed.Error!);
                    }

                    filters.Add(new TextFilter(QueryField.Folder, parsed.Value!));
                    break;
                }
                case "iso":
                {
                    var parsed = ParseNumericValue(rawValue, key, requireInteger: true);
                    if (!parsed.Success)
                    {
                        return SearchQueryParseResult.Fail(parsed.Error!);
                    }

                    filters.Add(new NumericFilter(QueryField.Iso, parsed.Operator!.Value, parsed.Value!.Value));
                    break;
                }
                case "f":
                {
                    var parsed = ParseNumericValue(rawValue, key, requireInteger: false);
                    if (!parsed.Success)
                    {
                        return SearchQueryParseResult.Fail(parsed.Error!);
                    }

                    filters.Add(new NumericFilter(QueryField.Aperture, parsed.Operator!.Value, parsed.Value!.Value));
                    break;
                }
                case "date":
                {
                    var parsed = ParseDateRange(rawValue);
                    if (!parsed.Success)
                    {
                        return SearchQueryParseResult.Fail(parsed.Error!);
                    }

                    filters.Add(new DateRangeFilter(parsed.StartDate!.Value, parsed.EndDate!.Value));
                    break;
                }
                default:
                    return SearchQueryParseResult.Fail($"Unknown filter key '{key}'. Supported keys: camera, lens, iso, f, folder, date.");
            }
        }

        return SearchQueryParseResult.Ok(new SearchQuery(isVisual, visualText, filters));
    }

    private static TokenizationResult Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                current.Append(ch);
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (inQuotes)
        {
            return TokenizationResult.Fail("Unterminated quoted phrase in query.");
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return TokenizationResult.Ok(tokens);
    }

    private static VisualPrefixResult ParseVisualPrefix(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return VisualPrefixResult.Ok(false, null, 0);
        }

        if (tokens[0] == "~")
        {
            if (tokens.Count < 2)
            {
                return VisualPrefixResult.Fail("Visual query prefix '~' must be followed by a quoted phrase, e.g. ~ \"sunset\".");
            }

            var parsed = ParseTextValue(tokens[1], requireQuoted: true);
            if (!parsed.Success)
            {
                return VisualPrefixResult.Fail(parsed.Error!);
            }

            return VisualPrefixResult.Ok(true, parsed.Value, 2);
        }

        if (!tokens[0].StartsWith('~'))
        {
            return VisualPrefixResult.Ok(false, null, 0);
        }

        var inlineVisual = tokens[0][1..].TrimStart();
        if (string.IsNullOrWhiteSpace(inlineVisual))
        {
            return VisualPrefixResult.Fail("Visual query prefix '~' must be followed by a quoted phrase, e.g. ~ \"sunset\".");
        }

        var parsedInline = ParseTextValue(inlineVisual, requireQuoted: true);
        if (!parsedInline.Success)
        {
            return VisualPrefixResult.Fail(parsedInline.Error!);
        }

        return VisualPrefixResult.Ok(true, parsedInline.Value, 1);
    }

    private static TextParseResult ParseTextValue(string token, bool requireQuoted = false)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return TextParseResult.Fail("Missing text value.");
        }

        var trimmed = token.Trim();
        var starts = trimmed.StartsWith('"');
        var ends = trimmed.EndsWith('"');

        if (starts != ends)
        {
            return TextParseResult.Fail("Quoted text value is missing an opening or closing quote.");
        }

        if (requireQuoted && (!starts || !ends))
        {
            return TextParseResult.Fail("Visual query text must be quoted, e.g. ~ \"sunset on beach\".");
        }

        if (starts && ends)
        {
            var value = trimmed[1..^1];
            if (string.IsNullOrWhiteSpace(value))
            {
                return TextParseResult.Fail("Quoted text value cannot be empty.");
            }

            return TextParseResult.Ok(value);
        }

        return TextParseResult.Ok(trimmed);
    }

    private static NumericParseResult ParseNumericValue(string rawValue, string key, bool requireInteger)
    {
        var trimmed = rawValue.Trim();

        var op = NumericComparisonOperator.Equal;
        var valuePart = trimmed;

        if (trimmed.StartsWith(">="))
        {
            op = NumericComparisonOperator.GreaterThanOrEqual;
            valuePart = trimmed[2..];
        }
        else if (trimmed.StartsWith("<="))
        {
            op = NumericComparisonOperator.LessThanOrEqual;
            valuePart = trimmed[2..];
        }
        else if (trimmed.StartsWith('>'))
        {
            op = NumericComparisonOperator.GreaterThan;
            valuePart = trimmed[1..];
        }
        else if (trimmed.StartsWith('<'))
        {
            op = NumericComparisonOperator.LessThan;
            valuePart = trimmed[1..];
        }
        else if (trimmed.StartsWith('='))
        {
            op = NumericComparisonOperator.Equal;
            valuePart = trimmed[1..];
        }

        valuePart = valuePart.Trim();
        if (string.IsNullOrWhiteSpace(valuePart))
        {
            return NumericParseResult.Fail($"Missing numeric value for '{key}' filter.");
        }

        if (requireInteger)
        {
            if (!int.TryParse(valuePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                return NumericParseResult.Fail($"Invalid integer value '{valuePart}' for '{key}' filter.");
            }

            return NumericParseResult.Ok(op, intValue);
        }

        if (!double.TryParse(valuePart, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return NumericParseResult.Fail($"Invalid numeric value '{valuePart}' for '{key}' filter.");
        }

        return NumericParseResult.Ok(op, doubleValue);
    }

    private static DateParseResult ParseDateRange(string rawValue)
    {
        var trimmed = rawValue.Trim();
        var parts = trimmed.Split("..", StringSplitOptions.None);

        if (parts.Length == 1)
        {
            if (!TryParseDate(parts[0], out var date))
            {
                return DateParseResult.Fail($"Invalid date '{parts[0]}'. Expected format YYYY-MM-DD.");
            }

            return DateParseResult.Ok(date, date);
        }

        if (parts.Length == 2)
        {
            if (!TryParseDate(parts[0], out var start))
            {
                return DateParseResult.Fail($"Invalid start date '{parts[0]}'. Expected format YYYY-MM-DD.");
            }

            if (!TryParseDate(parts[1], out var end))
            {
                return DateParseResult.Fail($"Invalid end date '{parts[1]}'. Expected format YYYY-MM-DD.");
            }

            if (end < start)
            {
                return DateParseResult.Fail("Date range end must be on or after start date.");
            }

            return DateParseResult.Ok(start, end);
        }

        return DateParseResult.Fail($"Invalid date expression '{rawValue}'. Use YYYY-MM-DD or YYYY-MM-DD..YYYY-MM-DD.");
    }

    private static bool TryParseDate(string value, out DateOnly date)
        => DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private sealed record TokenizationResult(bool Success, List<string>? Tokens, string? Error)
    {
        public static TokenizationResult Ok(List<string> tokens) => new(true, tokens, null);

        public static TokenizationResult Fail(string error) => new(false, null, error);
    }

    private sealed record VisualPrefixResult(bool Success, bool IsVisualQuery, string? VisualQueryText, int NextTokenIndex, string? Error)
    {
        public static VisualPrefixResult Ok(bool isVisualQuery, string? visualQueryText, int nextTokenIndex)
            => new(true, isVisualQuery, visualQueryText, nextTokenIndex, null);

        public static VisualPrefixResult Fail(string error)
            => new(false, false, null, 0, error);
    }

    private sealed record TextParseResult(bool Success, string? Value, string? Error)
    {
        public static TextParseResult Ok(string value) => new(true, value, null);

        public static TextParseResult Fail(string error) => new(false, null, error);
    }

    private sealed record NumericParseResult(bool Success, NumericComparisonOperator? Operator, double? Value, string? Error)
    {
        public static NumericParseResult Ok(NumericComparisonOperator @operator, double value)
            => new(true, @operator, value, null);

        public static NumericParseResult Fail(string error)
            => new(false, null, null, error);
    }

    private sealed record DateParseResult(bool Success, DateOnly? StartDate, DateOnly? EndDate, string? Error)
    {
        public static DateParseResult Ok(DateOnly startDate, DateOnly endDate)
            => new(true, startDate, endDate, null);

        public static DateParseResult Fail(string error)
            => new(false, null, null, error);
    }
}
