using System.Collections.ObjectModel;

namespace Walk.ViewModels;

public sealed class GridResultRowViewModel
{
    public ObservableCollection<GridResultItemViewModel> Items { get; } = [];
}
