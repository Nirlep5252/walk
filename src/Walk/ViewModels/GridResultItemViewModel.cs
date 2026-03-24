using CommunityToolkit.Mvvm.ComponentModel;
using Walk.Models;

namespace Walk.ViewModels;

public partial class GridResultItemViewModel(SearchResult result, int index) : ObservableObject
{
    public SearchResult Result { get; } = result;
    public int Index { get; } = index;

    [ObservableProperty]
    private bool _isSelected;
}
