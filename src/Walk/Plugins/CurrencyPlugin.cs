using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed partial class CurrencyPlugin : IQueryPlugin
{
    public string Name => "Currency";
    public int Priority => 85;

    private readonly CacheService _cache;
    private readonly TimeSpan _cacheTtl;
    private static readonly HttpClient HttpClient = new();

    [GeneratedRegex(@"^([\d.,]+)\s*([A-Za-z]{3})\s+(?:to|in)\s+([A-Za-z]{3})$",
        RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyPattern();

    public CurrencyPlugin(CacheService cache, TimeSpan cacheTtl)
    {
        _cache = cache;
        _cacheTtl = cacheTtl;
    }

    public static bool TryParseQuery(string query, out decimal amount, out string from, out string to)
    {
        amount = 0;
        from = "";
        to = "";

        var match = CurrencyPattern().Match(query.Trim());
        if (!match.Success)
            return false;

        if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out amount) || amount <= 0)
            return false;

        from = match.Groups[2].Value.ToUpperInvariant();
        to = match.Groups[3].Value.ToUpperInvariant();
        return true;
    }

    public async Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (!TryParseQuery(query, out var amount, out var from, out var to))
            return [];

        try
        {
            var rates = await _cache.GetOrSetAsync(
                $"currency_{from}.json",
                _cacheTtl,
                () => FetchRatesAsync(from, ct));

            if (rates is null || !rates.Rates.TryGetValue(to, out var rate))
                return [];

            var converted = amount * rate;
            var formatted = converted.ToString("N2", CultureInfo.InvariantCulture);
            var resultText = $"{amount} {from} = {formatted} {to}";

            return
            [
                new SearchResult
                {
                    Title = resultText,
                    Subtitle = $"Rate: 1 {from} = {rate:N6} {to}",
                    PluginName = Name,
                    Score = 0.95,
                    IconGlyph = "$",
                    Actions =
                    [
                        new SearchAction
                        {
                            Label = "Copy Result",
                            HintLabel = "Copy",
                            Execute = () => System.Windows.Clipboard.SetText(formatted),
                            KeyGesture = "Enter"
                        },
                        new SearchAction
                        {
                            Label = "Swap Currencies",
                            Execute = () => { }
                        }
                    ]
                }
            ];
        }
        catch
        {
            return [];
        }
    }

    private static async Task<ExchangeRateData> FetchRatesAsync(string baseCurrency, CancellationToken ct)
    {
        var url = $"https://open.er-api.com/v6/latest/{baseCurrency}";
        var json = await HttpClient.GetStringAsync(url, ct);
        var doc = JsonDocument.Parse(json);
        var ratesElement = doc.RootElement.GetProperty("rates");

        var rates = new Dictionary<string, decimal>();
        foreach (var prop in ratesElement.EnumerateObject())
        {
            rates[prop.Name] = prop.Value.GetDecimal();
        }

        return new ExchangeRateData { BaseCurrency = baseCurrency, Rates = rates };
    }

    public class ExchangeRateData
    {
        public string BaseCurrency { get; set; } = "";
        public Dictionary<string, decimal> Rates { get; set; } = new();
    }
}
