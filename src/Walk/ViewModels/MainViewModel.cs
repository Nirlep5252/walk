using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Walk.Models;
using Walk.Services;

namespace Walk.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly QueryRouter _router;
    private readonly int _maxResults;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private bool _isVisible;

    public ObservableCollection<SearchResult> Results { get; } = [];
    public ObservableCollection<SearchAction> VisibleActions { get; } = [];

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
        RefreshSelectionState();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchAsync(value);
    }

    private async Task SearchAsync(string query)
    {
        CancelPendingSearch();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (string.IsNullOrWhiteSpace(query))
        {
            Results.Clear();
            SelectedIndex = -1;
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

        IReadOnlyList<SearchResult> results;
        try
        {
            results = await _router.RouteAsync(query, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        // Update collection in-place to minimize UI layout passes
        var newResults = _maxResults > 0
            ? results.Take(_maxResults).ToList()
            : results.ToList();
        for (int i = 0; i < newResults.Count; i++)
        {
            if (i < Results.Count)
                Results[i] = newResults[i];
            else
                Results.Add(newResults[i]);
        }
        while (Results.Count > newResults.Count)
            Results.RemoveAt(Results.Count - 1);

        SelectedIndex = Results.Count > 0 ? 0 : -1;
        RefreshSelectionState();
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

    public void Show()
    {
        CancelPendingSearch();
        SearchText = "";
        Results.Clear();
        SelectedIndex = -1;
        RefreshSelectionState();
        IsVisible = true;
    }

    public void Hide()
    {
        CancelPendingSearch();
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
}
