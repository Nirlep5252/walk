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

    public MainViewModel(QueryRouter router, int maxResults = 8)
    {
        _router = router;
        _maxResults = maxResults;
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchAsync(value);
    }

    private async Task SearchAsync(string query)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await Task.Delay(100, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var results = await _router.RouteAsync(query, token);

        if (token.IsCancellationRequested)
            return;

        Results.Clear();
        foreach (var result in results.Take(_maxResults))
            Results.Add(result);

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    [RelayCommand]
    private void ExecuteSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            var result = Results[SelectedIndex];
            if (result.Actions.Count > 0)
                result.Actions[0].Execute();

            Hide();
        }
    }

    [RelayCommand]
    private void ExecuteAsAdmin()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            var result = Results[SelectedIndex];
            var adminAction = result.Actions.FirstOrDefault(a => a.Label.Contains("Admin"));
            adminAction?.Execute();
            Hide();
        }
    }

    public void Show()
    {
        SearchText = "";
        Results.Clear();
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }
}
