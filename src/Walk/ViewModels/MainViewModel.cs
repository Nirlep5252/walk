using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Walk.Models;
using Walk.Services;

namespace Walk.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int GridColumnCount = 3;
    private readonly QueryRouter _router;
    private readonly int _maxResults;
    private CancellationTokenSource? _cts;
    private int _searchVersion;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ResultsViewMode _resultsViewMode = ResultsViewMode.List;

    public ObservableCollection<SearchResult> Results { get; } = [];
    public ObservableCollection<GridResultRowViewModel> GridRows { get; } = [];
    public ObservableCollection<SearchAction> VisibleActions { get; } = [];
    public bool IsListView => ResultsViewMode == ResultsViewMode.List;
    public bool IsGridView => ResultsViewMode == ResultsViewMode.Grid;

    public SearchResult? SelectedResult =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count
            ? Results[SelectedIndex]
            : null;

    public MainViewModel(QueryRouter router, int maxResults = 0)
    {
        _router = router;
        _maxResults = maxResults;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        RefreshGridSelectionState();
        RefreshSelectionState();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchAsync(value);
    }

    partial void OnResultsViewModeChanged(ResultsViewMode value)
    {
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsGridView));
        EnsureSelectionInVisibleRange();
    }

    private async Task SearchAsync(string query)
    {
        CancelPendingSearch();
        var searchVersion = Interlocked.Increment(ref _searchVersion);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (string.IsNullOrWhiteSpace(query))
        {
            Results.Clear();
            GridRows.Clear();
            SelectedIndex = -1;
            IsSearching = false;
            RefreshSelectionState();
            return;
        }

        try
        {
            await Task.Delay(80, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        if (searchVersion == _searchVersion)
        {
            ApplyResults([]);
            IsSearching = true;
        }

        try
        {
            await _router.RouteIncrementalAsync(
                query,
                results => ApplyResultsAsync(searchVersion, results, token),
                token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (searchVersion == _searchVersion)
                IsSearching = false;
        }
    }

    [RelayCommand]
    private void ExecuteSelected()
    {
        TryExecuteSelectedAction(static action => true);
    }

    [RelayCommand]
    private void ExecuteAsAdmin()
    {
        TryExecuteSelectedAction(action => action.Label.Contains("Admin", StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void SetListView()
    {
        ResultsViewMode = ResultsViewMode.List;
    }

    [RelayCommand]
    private void SetGridView()
    {
        ResultsViewMode = ResultsViewMode.Grid;
    }

    [RelayCommand]
    private void ToggleResultsView()
    {
        ResultsViewMode = ResultsViewMode == ResultsViewMode.List
            ? ResultsViewMode.Grid
            : ResultsViewMode.List;
    }

    [RelayCommand]
    private void SelectGridResult(int index)
    {
        if (index >= 0 && index < Results.Count)
            SelectedIndex = index;
    }

    public void Show()
    {
        CancelPendingSearch();
        Interlocked.Increment(ref _searchVersion);
        SearchText = "";
        Results.Clear();
        GridRows.Clear();
        SelectedIndex = -1;
        IsSearching = false;
        RefreshSelectionState();
        IsVisible = true;
    }

    public void Hide()
    {
        CancelPendingSearch();
        Interlocked.Increment(ref _searchVersion);
        IsSearching = false;
        IsVisible = false;
    }

    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }

    private void CancelPendingSearch()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public bool TryExecuteSelectedAction(string keyGesture)
    {
        if (string.IsNullOrWhiteSpace(keyGesture))
            return false;

        return TryExecuteSelectedAction(action =>
            string.Equals(action.KeyGesture, keyGesture, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryExecuteSelectedAction(Func<SearchAction, bool> predicate)
    {
        var action = SelectedResult?.Actions.FirstOrDefault(predicate);
        if (action is null)
            return false;

        var executed = TryExecuteAction(action);
        if (executed && action.ClosesLauncher)
            Hide();

        return true;
    }

    public void MoveSelection(int offset)
    {
        if (Results.Count == 0 || offset == 0)
            return;

        var targetIndex = SelectedIndex;
        if (targetIndex < 0)
            targetIndex = 0;
        else
            targetIndex += offset;

        SelectedIndex = Math.Clamp(targetIndex, 0, Results.Count - 1);
    }

    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(SelectedResult));

        var actions = SelectedResult?.Actions
            .Where(action => !string.IsNullOrWhiteSpace(action.KeyGesture))
            .ToList() ?? [];

        for (int i = 0; i < actions.Count; i++)
        {
            if (i < VisibleActions.Count)
                VisibleActions[i] = actions[i];
            else
                VisibleActions.Add(actions[i]);
        }

        while (VisibleActions.Count > actions.Count)
            VisibleActions.RemoveAt(VisibleActions.Count - 1);
    }

    private static bool TryExecuteAction(SearchAction action)
    {
        try
        {
            action.Execute();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Task ApplyResultsAsync(
        int searchVersion,
        IReadOnlyList<SearchResult> results,
        CancellationToken token)
    {
        if (token.IsCancellationRequested || searchVersion != _searchVersion)
            return Task.CompletedTask;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyResults(results);
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(
            () =>
            {
                if (!token.IsCancellationRequested && searchVersion == _searchVersion)
                    ApplyResults(results);
            }).Task;
    }

    private void ApplyResults(IReadOnlyList<SearchResult> results)
    {
        var newResults = _maxResults > 0
            ? results.Take(_maxResults).ToList()
            : results.ToList();

        for (int i = 0; i < newResults.Count; i++)
        {
            if (i < Results.Count)
            {
                if (!ReferenceEquals(Results[i], newResults[i]))
                    Results[i] = newResults[i];
            }
            else
            {
                Results.Add(newResults[i]);
            }
        }

        while (Results.Count > newResults.Count)
            Results.RemoveAt(Results.Count - 1);

        SyncGridRows(newResults);

        if (Results.Count == 0)
        {
            SelectedIndex = -1;
        }
        else if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
        {
            SelectedIndex = 0;
        }

        RefreshSelectionState();
    }

    private void SyncGridRows(IReadOnlyList<SearchResult> sourceResults)
    {
        var rowCount = (sourceResults.Count + GridColumnCount - 1) / GridColumnCount;

        while (GridRows.Count < rowCount)
            GridRows.Add(new GridResultRowViewModel());

        while (GridRows.Count > rowCount)
            GridRows.RemoveAt(GridRows.Count - 1);

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = GridRows[rowIndex];
            var startIndex = rowIndex * GridColumnCount;
            var rowItems = sourceResults
                .Skip(startIndex)
                .Take(GridColumnCount)
                .Select((result, offset) => new GridResultItemViewModel(result, startIndex + offset))
                .ToList();

            for (int itemIndex = 0; itemIndex < rowItems.Count; itemIndex++)
            {
                if (itemIndex < row.Items.Count)
                {
                    if (!ReferenceEquals(row.Items[itemIndex].Result, rowItems[itemIndex].Result) ||
                        row.Items[itemIndex].Index != rowItems[itemIndex].Index)
                    {
                        row.Items[itemIndex] = rowItems[itemIndex];
                    }
                }
                else
                {
                    row.Items.Add(rowItems[itemIndex]);
                }
            }

            while (row.Items.Count > rowItems.Count)
                row.Items.RemoveAt(row.Items.Count - 1);
        }

        RefreshGridSelectionState();
    }

    private void EnsureSelectionInVisibleRange()
    {
        if (Results.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        if (SelectedIndex >= Results.Count)
            SelectedIndex = Results.Count - 1;
    }

    private void RefreshGridSelectionState()
    {
        foreach (var row in GridRows)
        {
            foreach (var item in row.Items)
                item.IsSelected = item.Index == SelectedIndex;
        }
    }
}
