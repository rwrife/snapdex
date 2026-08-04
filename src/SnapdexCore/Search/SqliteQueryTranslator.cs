using System.Globalization;
using System.Text;

namespace SnapdexCore.Search;

public sealed class SqliteQueryTranslator
{
    public SqliteQueryTranslation Translate(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>();

        foreach (var filter in query.Filters)
        {
            switch (filter)
            {
                case TextFilter text:
                    AddTextFilter(text, conditions, parameters);
                    break;
                case NumericFilter numeric:
                    AddNumericFilter(numeric, conditions, parameters);
                    break;
                case DateRangeFilter dateRange:
                    AddDateRangeFilter(dateRange, conditions, parameters);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported query filter type: {filter.GetType().Name}");
            }
        }

        var sql = new StringBuilder();
        sql.Append(
            "SELECT path, filename, size, mtime, indexed_at, camera_make, camera_model, lens_model, iso, aperture, shutter_seconds, focal_length_mm, captured_at, gps_latitude, gps_longitude FROM images");

        if (conditions.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", conditions));
        }

        sql.Append(" ORDER BY captured_at DESC, filename ASC;");

        return new SqliteQueryTranslation(
            sql.ToString(),
            parameters,
            query.IsVisualQuery,
            query.VisualQueryKind,
            query.VisualQueryText,
            query.VisualSimilarPath);
    }

    private static void AddTextFilter(TextFilter filter, List<string> conditions, Dictionary<string, object?> parameters)
    {
        var parameterName = NextParameter(parameters);
        parameters[parameterName] = $"%{EscapeLikePattern(filter.Value)}%";

        switch (filter.Field)
        {
            case QueryField.Camera:
                conditions.Add($"((camera_make LIKE {parameterName} ESCAPE '\\' COLLATE NOCASE) OR (camera_model LIKE {parameterName} ESCAPE '\\' COLLATE NOCASE))");
                return;
            case QueryField.Lens:
                conditions.Add($"(lens_model LIKE {parameterName} ESCAPE '\\' COLLATE NOCASE)");
                return;
            case QueryField.Folder:
                conditions.Add($"(path LIKE {parameterName} ESCAPE '\\' COLLATE NOCASE)");
                return;
            case QueryField.Filename:
                conditions.Add($"(filename LIKE {parameterName} ESCAPE '\\' COLLATE NOCASE)");
                return;
            default:
                throw new InvalidOperationException($"Unsupported text filter field: {filter.Field}");
        }
    }

    private static void AddNumericFilter(NumericFilter filter, List<string> conditions, Dictionary<string, object?> parameters)
    {
        var parameterName = NextParameter(parameters);
        parameters[parameterName] = filter.Field switch
        {
            QueryField.Iso => Convert.ToInt32(filter.Value, CultureInfo.InvariantCulture),
            _ => filter.Value
        };

        var column = filter.Field switch
        {
            QueryField.Iso => "iso",
            QueryField.Aperture => "aperture",
            _ => throw new InvalidOperationException($"Unsupported numeric filter field: {filter.Field}")
        };

        var @operator = filter.Operator switch
        {
            NumericComparisonOperator.Equal => "=",
            NumericComparisonOperator.GreaterThan => ">",
            NumericComparisonOperator.GreaterThanOrEqual => ">=",
            NumericComparisonOperator.LessThan => "<",
            NumericComparisonOperator.LessThanOrEqual => "<=",
            _ => throw new InvalidOperationException($"Unsupported numeric operator: {filter.Operator}")
        };

        conditions.Add($"({column} {@operator} {parameterName})");
    }

    private static void AddDateRangeFilter(DateRangeFilter filter, List<string> conditions, Dictionary<string, object?> parameters)
    {
        var start = ToUtcIsoString(filter.StartDate, TimeOnly.MinValue);
        var endExclusive = ToUtcIsoString(filter.EndDate.AddDays(1), TimeOnly.MinValue);

        var startParam = NextParameter(parameters);
        parameters[startParam] = start;

        var endParam = NextParameter(parameters);
        parameters[endParam] = endExclusive;

        conditions.Add($"(captured_at >= {startParam} AND captured_at < {endParam})");
    }

    private static string ToUtcIsoString(DateOnly date, TimeOnly time)
    {
        var dateTime = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);
        return new DateTimeOffset(dateTime).ToString("O", CultureInfo.InvariantCulture);
    }

    private static string NextParameter(Dictionary<string, object?> parameters) => $"$p{parameters.Count}";

    private static string EscapeLikePattern(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
