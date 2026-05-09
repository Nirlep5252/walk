using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed class CurrencyPlugin : IQueryPlugin
{
    public string Name => "Currency";
    public int Priority => 85;

    private readonly CurrencyConversionService _currencyConversionService;
    private readonly FavoriteService? _favoriteService;

    public CurrencyPlugin(CacheService cache, TimeSpan cacheTtl, FavoriteService? favoriteService = null)
        : this(new CurrencyConversionService(cache, cacheTtl), favoriteService)
    {
    }

    public CurrencyPlugin(CurrencyConversionService currencyConversionService, FavoriteService? favoriteService = null)
    {
        _currencyConversionService = currencyConversionService;
        _favoriteService = favoriteService;
    }

    public static bool TryParseQuery(string query, out decimal amount, out string from, out string to)
    {
        return CurrencyConversionService.TryParseQuery(query, out amount, out from, out to);
    }

    public async Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        try
        {
            var conversion = await _currencyConversionService.ConvertAsync(query, ct).ConfigureAwait(false);
            if (conversion is null)
                return [];

            var actions = new List<SearchAction>
            {
                new()
                {
                    Label = "Copy Result",
                    HintLabel = "Copy",
                    Execute = () => System.Windows.Clipboard.SetText(conversion.Formatted),
                    KeyGesture = "Enter"
                }
            };

            if (_favoriteService is not null)
            {
                actions.Add(FavoriteService.CreateToggleAction(
                    _favoriteService,
                    FavoriteService.FromCurrency(conversion.Amount, conversion.From, conversion.To)));
            }

            actions.Add(new SearchAction
            {
                Label = "Swap Currencies",
                Execute = () => { }
            });

            return
            [
                new SearchResult
                {
                    Title = conversion.ResultText,
                    Subtitle = conversion.RateText,
                    PluginName = Name,
                    Score = 0.95,
                    IconGlyph = "$",
                    Actions = actions,
                }
            ];
        }
        catch
        {
            return [];
        }
    }

    public sealed class ExchangeRateData
    {
        public string BaseCurrency { get; set; } = "";
        public Dictionary<string, decimal> Rates { get; set; } = new();
    }
}
