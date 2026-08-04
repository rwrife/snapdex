using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SnapdexCore.Indexing;
using SnapdexCore.LocalAi;
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
    private readonly IncrementalIndexingService _incrementalIndexingService;
    private readonly string _picturesFolder;

    private readonly LocalAiSettingsStore _localAiSettingsStore = new(AppPaths.LocalAiSettingsPath);
    private readonly OpenAiCompatibleEmbeddingClient _embeddingClient = new();
    private readonly VisualSearchService _visualSearchService;

    private LocalAiSettings _localAiSettings;

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

        _incrementalIndexingService = new IncrementalIndexingService(_databasePath);
        _picturesFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        _visualSearchService = new VisualSearchService(_embeddingClient);

        _localAiSettings = _localAiSettingsStore.Load().Normalize();
        LocalAiEndpointTextBox.Text = _localAiSettings.EndpointUrl;
        LocalAiModelTextBox.Text = _localAiSettings.Model;

        Closed += MainWindow_OnClosed;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.AppDataRoot);

        using (var index = new SqliteImageIndex(_databasePath))
        {
            index.EnsureCreated();
        }

        await InitializeIncrementalIndexingAsync();
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

        if (!IsPicturesFolderAvailable())
        {
            StatusText.Text = "Could not locate the Pictures folder for this user.";
            return;
        }

        _isIndexing = true;
        ToggleUiEnabled(false);
        try
        {
            StatusText.Text = $"Indexing {_picturesFolder} ...";

            var indexed = await Task.Run(() =>
            {
                var indexer = new LibraryIndexer(_databasePath);
                return indexer.IndexFolder(
                    _picturesFolder,
                    image => _thumbnailCache.GetOrCreate(image.Path, image.ModifiedTimeUtc));
            });

            _incrementalIndexingService.StartWatching(new[] { _picturesFolder });

            StatusText.Text =
                $"Indexed {indexed} image(s) from {_picturesFolder}. Live incremental indexing is now active.";
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

    private async void SaveLocalAiSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _localAiSettings = ReadLocalAiSettingsFromInputs().Normalize();
            _localAiSettingsStore.Save(_localAiSettings);
            StatusText.Text = $"Saved Local-AI settings (endpoint={_localAiSettings.EndpointUrl}, model={_localAiSettings.Model}).";

            if (!string.IsNullOrWhiteSpace(QueryTextBox.Text))
            {
                await RefreshResultsAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to save Local-AI settings: {ex.Message}";
        }
    }

    private async void CheckLocalAiHealthButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = ReadLocalAiSettingsFromInputs().Normalize();
        if (!settings.IsConfigured)
        {
            StatusText.Text = "Local-AI settings are incomplete. Provide endpoint URL and model.";
            return;
        }

        StatusText.Text = "Checking Local-AI endpoint...";

        var health = await _embeddingClient.CheckHealthAsync(settings);
        StatusText.Text = health.IsHealthy
            ? $"Local-AI healthy: {health.Message}"
            : $"Local-AI unavailable: {health.Message}";
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
            await Task.Run(() =>
                _incrementalIndexingService.FlushPendingChanges(
                    image => _thumbnailCache.GetOrCreate(image.Path, image.ModifiedTimeUtc)));

            var queryText = QueryTextBox.Text;

            var parsed = _queryParser.Parse(queryText);
            if (!parsed.Success)
            {
                StatusText.Text = parsed.Error ?? "Invalid query.";
                _results.Clear();
                return;
            }

            var translation = _queryTranslator.Translate(parsed.Query!);
            var localAiSettings = GetConfiguredLocalAiSettings();

            var searchResult = await Task.Run(async () =>
            {
                using var index = new SqliteImageIndex(_databasePath);
                index.EnsureCreated();
                return await _visualSearchService.SearchAsync(index, translation, localAiSettings, limit: 20000);
            });

            var rows = searchResult.Records
                .Select(record => new SearchResultRow
                {
                    Record = record,
                    DisplayPath = record.Path,
                    ThumbnailPath = _thumbnailCache.GetOrCreate(record.Path, record.ModifiedTimeUtc)
                })
                .ToList();

            _results.Clear();
            foreach (var row in rows)
            {
                _results.Add(row);
            }

            if (searchResult.UsedVisualRanking)
            {
                StatusText.Text = $"Showing {_results.Count} visual result(s). {searchResult.Notice}";
            }
            else if (parsed.Query!.IsVisualQuery)
            {
                StatusText.Text = $"Showing {_results.Count} metadata result(s). {searchResult.Notice}";
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

    private async Task InitializeIncrementalIndexingAsync()
    {
        if (!IsPicturesFolderAvailable())
        {
            return;
        }

        var hasExistingIndex = await Task.Run(() =>
        {
            using var index = new SqliteImageIndex(_databasePath);
            index.EnsureCreated();
            return index.CountImages() > 0;
        });

        _incrementalIndexingService.StartWatching(new[] { _picturesFolder });

        if (!hasExistingIndex)
        {
            StatusText.Text = "Ready. Incremental watcher is active for the Pictures folder.";
            return;
        }

        StatusText.Text = "Reconciling index with filesystem changes...";

        var sync = await Task.Run(() =>
            _incrementalIndexingService.ReconcileFolders(
                new[] { _picturesFolder },
                image => _thumbnailCache.GetOrCreate(image.Path, image.ModifiedTimeUtc)));

        StatusText.Text =
            $"Reconciled {sync.ScannedCount} image(s): {sync.UpsertedCount} upserted, {sync.DeletedCount} removed. Watching for live changes.";
    }

    private LocalAiSettings? GetConfiguredLocalAiSettings()
    {
        var settings = _localAiSettings.Normalize();
        return settings.IsConfigured ? settings : null;
    }

    private LocalAiSettings ReadLocalAiSettingsFromInputs()
    {
        var endpoint = LocalAiEndpointTextBox.Text?.Trim() ?? string.Empty;
        var model = LocalAiModelTextBox.Text?.Trim() ?? string.Empty;

        return new LocalAiSettings(endpoint, model);
    }

    private bool IsPicturesFolderAvailable()
        => !string.IsNullOrWhiteSpace(_picturesFolder) && Directory.Exists(_picturesFolder);

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _incrementalIndexingService.Dispose();
        _embeddingClient.Dispose();
    }

    private void ToggleUiEnabled(bool enabled)
    {
        QueryTextBox.IsEnabled = enabled;
        ResultsGrid.IsEnabled = enabled;
        LocalAiEndpointTextBox.IsEnabled = enabled;
        LocalAiModelTextBox.IsEnabled = enabled;
        SaveLocalAiSettingsButton.IsEnabled = enabled;
        CheckLocalAiHealthButton.IsEnabled = enabled;
    }
}
