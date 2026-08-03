namespace SnapdexCore.Search;

public enum QueryField
{
    Camera,
    Lens,
    Iso,
    Aperture,
    Folder,
    Filename
}

public enum NumericComparisonOperator
{
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public enum VisualQueryKind
{
    None,
    Text,
    SimilarImage
}

public abstract record QueryFilter;

public sealed record TextFilter(QueryField Field, string Value) : QueryFilter;

public sealed record NumericFilter(QueryField Field, NumericComparisonOperator Operator, double Value) : QueryFilter;

public sealed record DateRangeFilter(DateOnly StartDate, DateOnly EndDate) : QueryFilter;

public sealed record SearchQuery(
    bool IsVisualQuery,
    VisualQueryKind VisualQueryKind,
    string? VisualQueryText,
    string? VisualSimilarPath,
    IReadOnlyList<QueryFilter> Filters);

public sealed record SearchQueryParseResult(
    bool Success,
    SearchQuery? Query,
    string? Error)
{
    public static SearchQueryParseResult Ok(SearchQuery query) => new(true, query, null);

    public static SearchQueryParseResult Fail(string error) => new(false, null, error);
}

public sealed record SqliteQueryTranslation(
    string Sql,
    IReadOnlyDictionary<string, object?> Parameters,
    bool IsVisualQuery,
    VisualQueryKind VisualQueryKind,
    string? VisualQueryText,
    string? VisualSimilarPath);
