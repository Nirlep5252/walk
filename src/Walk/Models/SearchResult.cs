using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Walk.Models;

public sealed class SearchResult : ObservableObject
{
    private static readonly SemaphoreSlim PreviewLoadSemaphore = new(2, 2);
    private Func<CancellationToken, Task<ImageSource?>>? _previewLoader;
    private int _previewLoadStarted;
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string PluginName { get; init; } = "";
    public double Score { get; set; }
    public required IReadOnlyList<SearchAction> Actions { get; init; }
    public string? IconGlyph { get; init; }

    private ImageSource? _icon;
    private ImageSource? _preview;

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public ImageSource? Preview
    {
        get => _preview;
        set
        {
            if (SetProperty(ref _preview, value))
            {
                OnPropertyChanged(nameof(HasPreview));
                OnPropertyChanged(nameof(ShowsPreviewPlaceholder));
            }
        }
    }

    public bool HasPreview => Preview is not null;
    public bool ShowsPreviewPlaceholder => Preview is null;

    public void SetPreviewLoader(Func<CancellationToken, Task<ImageSource?>> previewLoader)
    {
        _previewLoader = previewLoader;
    }

    public async Task EnsurePreviewAsync(CancellationToken ct = default)
    {
        if (Preview is not null || _previewLoader is null)
            return;

        if (Interlocked.Exchange(ref _previewLoadStarted, 1) == 1)
            return;

        var acquired = false;
        try
        {
            await PreviewLoadSemaphore.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;

            if (Preview is not null)
                return;

            var preview = await _previewLoader(ct).ConfigureAwait(false);
            if (preview is null || ct.IsCancellationRequested)
                return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                Preview = preview;
                return;
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                    Preview = preview;
            });
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref _previewLoadStarted, 0);
        }
        catch
        {
        }
        finally
        {
            if (acquired)
                PreviewLoadSemaphore.Release();
        }
    }

    public string DisplayIconGlyph => string.IsNullOrWhiteSpace(IconGlyph)
        ? GetDefaultIconGlyph()
        : IconGlyph!;

    private string GetDefaultIconGlyph()
    {
        return PluginName switch
        {
            "Calculator" => "=",
            "Currency" => "$",
            "Files" => "\uD83D\uDCC4",
            "Run" => "\u25B6",
            "System" => "\u23FB",
            _ => PluginName.Length > 0 ? PluginName[..1].ToUpperInvariant() : "?",
        };
    }
}
