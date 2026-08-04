using SnapdexCore.Search;

namespace SnapdexCore.Tests;

public class QueryLanguageTests
{
    private readonly SearchQueryParser _parser = new();
    private readonly SqliteQueryTranslator _translator = new();

    [Fact]
    public void Parse_SupportsMetadataFilters_Ranges_QuotedPhrases_AndFilenameText()
    {
        var result = _parser.Parse("camera:\"Canon EOS R6\" lens:\"RF 24-70\" iso:>3200 f:<2.8 folder:\"Trips Iceland\" date:2025-10-01..2025-10-31 sunset");

        Assert.True(result.Success, result.Error);
        var query = Assert.IsType<SearchQuery>(result.Query);

        Assert.False(query.IsVisualQuery);
        Assert.Equal(VisualQueryKind.None, query.VisualQueryKind);
        Assert.Null(query.VisualQueryText);
        Assert.Null(query.VisualSimilarPath);
        Assert.Equal(7, query.Filters.Count);

        Assert.Equal(new TextFilter(QueryField.Camera, "Canon EOS R6"), query.Filters[0]);
        Assert.Equal(new TextFilter(QueryField.Lens, "RF 24-70"), query.Filters[1]);
        Assert.Equal(new NumericFilter(QueryField.Iso, NumericComparisonOperator.GreaterThan, 3200), query.Filters[2]);
        Assert.Equal(new NumericFilter(QueryField.Aperture, NumericComparisonOperator.LessThan, 2.8), query.Filters[3]);
        Assert.Equal(new TextFilter(QueryField.Folder, "Trips Iceland"), query.Filters[4]);
        Assert.Equal(new DateRangeFilter(new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31)), query.Filters[5]);
        Assert.Equal(new TextFilter(QueryField.Filename, "sunset"), query.Filters[6]);
    }

    [Fact]
    public void Parse_VisualPrefix_IsParsedAndFlagged()
    {
        var result = _parser.Parse("~ \"golden retriever in snow\" camera:Canon");

        Assert.True(result.Success, result.Error);
        var query = Assert.IsType<SearchQuery>(result.Query);

        Assert.True(query.IsVisualQuery);
        Assert.Equal(VisualQueryKind.Text, query.VisualQueryKind);
        Assert.Equal("golden retriever in snow", query.VisualQueryText);
        Assert.Null(query.VisualSimilarPath);
        Assert.Single(query.Filters);
        Assert.Equal(new TextFilter(QueryField.Camera, "Canon"), query.Filters[0]);
    }

    [Fact]
    public void Parse_SimilarPath_IsParsedAndFlagged()
    {
        var result = _parser.Parse("similar:\"C:\\Users\\you\\Pictures\\IMG_2043.jpg\" camera:Canon");

        Assert.True(result.Success, result.Error);
        var query = Assert.IsType<SearchQuery>(result.Query);

        Assert.True(query.IsVisualQuery);
        Assert.Equal(VisualQueryKind.SimilarImage, query.VisualQueryKind);
        Assert.Null(query.VisualQueryText);
        Assert.Equal("C:\\Users\\you\\Pictures\\IMG_2043.jpg", query.VisualSimilarPath);
        Assert.Single(query.Filters);
        Assert.Equal(new TextFilter(QueryField.Camera, "Canon"), query.Filters[0]);
    }

    [Fact]
    public void Parse_CannotCombineVisualTextAndSimilarPath()
    {
        var result = _parser.Parse("~ \"sunset\" similar:C:\\img.jpg");

        Assert.False(result.Success);
        Assert.Contains("Cannot combine", result.Error);
    }

    [Fact]
    public void Parse_InvalidQuery_ReturnsClearError_InsteadOfThrowing()
    {
        var result = _parser.Parse("iso:abc");

        Assert.False(result.Success);
        Assert.Null(result.Query);
        Assert.Contains("Invalid integer value", result.Error);
    }

    [Fact]
    public void Parse_UnknownFilter_ReturnsClearError()
    {
        var result = _parser.Parse("shutter:<0.01");

        Assert.False(result.Success);
        Assert.Null(result.Query);
        Assert.Contains("Unknown filter key", result.Error);
    }

    [Fact]
    public void Parse_VisualPrefix_RequiresQuotedPhrase()
    {
        var result = _parser.Parse("~ sunset");

        Assert.False(result.Success);
        Assert.Null(result.Query);
        Assert.Contains("must be quoted", result.Error);
    }

    [Fact]
    public void Translate_BuildsExpectedSql_ForTextAndNumericFilters()
    {
        var parse = _parser.Parse("camera:\"Canon EOS R6\" lens:\"RF 24-70\" iso:>3200 f:<=2.8 folder:Trips beach");
        Assert.True(parse.Success, parse.Error);

        var translation = _translator.Translate(parse.Query!);

        Assert.Contains("camera_make", translation.Sql);
        Assert.Contains("camera_model", translation.Sql);
        Assert.Contains("lens_model", translation.Sql);
        Assert.Contains("iso >", translation.Sql);
        Assert.Contains("aperture <=", translation.Sql);
        Assert.Contains("path LIKE", translation.Sql);
        Assert.Contains("filename LIKE", translation.Sql);

        Assert.Equal("%Canon EOS R6%", Assert.IsType<string>(translation.Parameters["$p0"]));
        Assert.Equal("%RF 24-70%", Assert.IsType<string>(translation.Parameters["$p1"]));
        Assert.Equal(3200, Convert.ToInt32(translation.Parameters["$p2"]));
        Assert.Equal(2.8, Convert.ToDouble(translation.Parameters["$p3"]), 6);
        Assert.Equal("%Trips%", Assert.IsType<string>(translation.Parameters["$p4"]));
        Assert.Equal("%beach%", Assert.IsType<string>(translation.Parameters["$p5"]));
    }

    [Fact]
    public void Translate_BuildsExpectedSql_ForDateRangeFilter()
    {
        var parse = _parser.Parse("date:2025-10-01..2025-10-31");
        Assert.True(parse.Success, parse.Error);

        var translation = _translator.Translate(parse.Query!);

        Assert.Contains("captured_at >=", translation.Sql);
        Assert.Contains("captured_at <", translation.Sql);
        Assert.Equal("2025-10-01T00:00:00.0000000+00:00", translation.Parameters["$p0"]);
        Assert.Equal("2025-11-01T00:00:00.0000000+00:00", translation.Parameters["$p1"]);
    }

    [Fact]
    public void Translate_PreservesVisualQueryFlags()
    {
        var parse = _parser.Parse("~ \"whiteboard notes\" lens:Sigma");
        Assert.True(parse.Success, parse.Error);

        var translation = _translator.Translate(parse.Query!);

        Assert.True(translation.IsVisualQuery);
        Assert.Equal(VisualQueryKind.Text, translation.VisualQueryKind);
        Assert.Equal("whiteboard notes", translation.VisualQueryText);
        Assert.Null(translation.VisualSimilarPath);
    }

    [Fact]
    public void Translate_PreservesVisualSimilarPath()
    {
        var parse = _parser.Parse("similar:C:\\pics\\a.jpg lens:Sigma");
        Assert.True(parse.Success, parse.Error);

        var translation = _translator.Translate(parse.Query!);

        Assert.True(translation.IsVisualQuery);
        Assert.Equal(VisualQueryKind.SimilarImage, translation.VisualQueryKind);
        Assert.Equal("C:\\pics\\a.jpg", translation.VisualSimilarPath);
        Assert.Null(translation.VisualQueryText);
    }
}
