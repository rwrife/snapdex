using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SnapdexCore.Indexing;
using SnapdexCore.Search;

namespace Snapdex.App;

public partial class MainWindow : Window
{
    private readonly string _databasePath = AppPaths.DatabasePath;
    private readonly ThumbnailCache _thumbnailCache = new(AppPaths.ThumbnailCacheDirectory);
    private readonly SearchQueryParser _queryParser = new();
    private readonly SqliteQueryTranslator _queryTranslator = new();
    private readonly DispatcherTimer _queryDebounceTimer;
    private readonly ObservableCollection<SearchResultRow> _results = new();

    private bool _isRefreshing;
    private bool _isIndexing;

    public MainWindow()
    {
        InitializeComponent();

        ResultsGrid.ItemsSource = _results;

        _queryDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _queryDebounceTimer.Tick += QueryDebounceTimer_OnTick;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.AppDataRoot);

        using (var index = new SqliteImageIndex(_databasePath))
        {
            index.EnsureCreated();
        }

        await RefreshResultsAsync();
    }

    private void QueryTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _queryDebounceTimer.Stop();
        _queryDebounceTimer.Start();
    }

    private async void QueryDebounceTimer_OnTick(object? sender, EventArgs e)
    {
        _queryDebounceTimer.Stop();
        await RefreshResultsAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshResultsAsync();
    }

    private async void IndexFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isIndexing)
        {
            return;
        }

        var picturesFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(picturesFolder) || !Directory.Exists(picturesFolder))
        {
            StatusText.Text = "Could not locate the Pictures folder for this user.";
            return;
        }

        _isIndexing = true;
        ToggleUiEnabled(false);
        try
        {
            StatusText.Text = $"Indexing {picturesFolder} ...";

            var indexed = await Task.Run(() =>
            {
                var indexer = new LibraryIndexer(_databasePath);
                return indexer.IndexFolder(
                    picturesFolder,
                    image => _thumbnailCache.GetOrCreate(image.Path, image.ModifiedTimeUtc));
            });

            StatusText.Text = $"Indexed {indexed} image(s) from {picturesFolder}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Index failed: {ex.Message}";
        }
        finally
        {
            _isIndexing = false;
            ToggleUiEnabled(true);
        }

        await RefreshResultsAsync();
    }

    private async Task RefreshResultsAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        ToggleUiEnabled(false);

        try
        {
            var queryText = QueryTextBox.Text;

            var parsed = _queryParser.Parse(queryText);
            if (!parsed.Success)
            {
                StatusText.Text = parsed.Error ?? "Invalid query.";
                _results.Clear();
                return;
            }

            var translation = _queryTranslator.Translate(parsed.Query!);

            var rows = await Task.Run(() =>
            {
                using var index = new SqliteImageIndex(_databasePath);
                index.EnsureCreated();

                var records = index.Search(translation, limit: 20000);
                return records
                    .Select(record => new SearchResultRow
                    {
                        Record = record,
                        DisplayPath = record.Path,
                        ThumbnailPath = _thumbnailCache.GetOrCreate(record.Path, record.ModifiedTimeUtc)
                    })
                    .ToList();
            });

            _results.Clear();
            foreach (var row in rows)
            {
                _results.Add(row);
            }

            if (parsed.Query!.IsVisualQuery)
            {
                StatusText.Text =
                    $"Showing {_results.Count} metadata result(s). Visual ranking is not enabled yet; query text parsed as '{parsed.Query.VisualQueryText}'.";
            }
            else
            {
                StatusText.Text = $"Showing {_results.Count} result(s).";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
            _results.Clear();
        }
        finally
        {
            _isRefreshing = false;
            ToggleUiEnabled(!_isIndexing);
        }
    }

    private void ResultsGrid_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not SearchResultRow selected)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = selected.Record.Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to open image: {ex.Message}";
        }
    }

    private void ToggleUiEnabled(bool enabled)
    {
        QueryTextBox.IsEnabled = enabled;
        ResultsGrid.IsEnabled = enabled;
    }
}
