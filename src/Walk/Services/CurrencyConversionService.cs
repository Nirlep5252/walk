using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Walk.Services;

public sealed partial class CurrencyConversionService
{
    private readonly CacheService _cache;
    private readonly Func<TimeSpan> _cacheTtlProvider;
    private static readonly HttpClient HttpClient = new();

    [GeneratedRegex(@"^([\d.,]+)\s*([A-Za-z]{3})\s+(?:to|in)\s+([A-Za-z]{3})$",
        RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyPattern();

    public CurrencyConversionService(CacheService cache, TimeSpan cacheTtl)
        : this(cache, () => cacheTtl)
    {
    }

    public CurrencyConversionService(CacheService cache, Func<TimeSpan> cacheTtlProvider)
    {
        _cache = cache;
        _cacheTtlProvider = cacheTtlProvider;
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

    public Task<CurrencyConversionResult?> ConvertAsync(string query, CancellationToken ct)
    {
        return TryParseQuery(query, out var amount, out var from, out var to)
            ? ConvertAsync(amount, from, to, ct)
            : Task.FromResult<CurrencyConversionResult?>(null);
    }

    public async Task<CurrencyConversionResult?> ConvertAsync(decimal amount, string from, string to, CancellationToken ct)
    {
        from = from.ToUpperInvariant();
        to = to.ToUpperInvariant();

        var rates = await _cache.GetOrSetAsync(
            $"currency_{from}.json",
            _cacheTtlProvider(),
            () => FetchRatesAsync(from, ct)).ConfigureAwait(false);

        if (rates is null || !rates.Rates.TryGetValue(to, out var rate))
            return null;

        var converted = amount * rate;
        var formatted = converted.ToString("N2", CultureInfo.InvariantCulture);
        var query = NormalizeQuery(amount, from, to);

        return new CurrencyConversionResult(
            query,
            amount,
            from,
            to,
            rate,
            converted,
            formatted,
            $"{amount.ToString(CultureInfo.InvariantCulture)} {from} = {formatted} {to}",
            $"Rate: 1 {from} = {rate:N6} {to}");
    }

    public static string NormalizeQuery(decimal amount, string from, string to)
    {
        return $"{amount.ToString(CultureInfo.InvariantCulture)} {from.ToUpperInvariant()} to {to.ToUpperInvariant()}";
    }

    private static async Task<ExchangeRateData> FetchRatesAsync(string baseCurrency, CancellationToken ct)
    {
        var url = $"https://open.er-api.com/v6/latest/{baseCurrency}";
        var json = await HttpClient.GetStringAsync(url, ct).ConfigureAwait(false);
        var doc = JsonDocument.Parse(json);
        var ratesElement = doc.RootElement.GetProperty("rates");

        var rates = new Dictionary<string, decimal>();
        foreach (var prop in ratesElement.EnumerateObject())
            rates[prop.Name] = prop.Value.GetDecimal();

        return new ExchangeRateData { BaseCurrency = baseCurrency, Rates = rates };
    }

    public sealed class ExchangeRateData
    {
        public string BaseCurrency { get; set; } = "";
        public Dictionary<string, decimal> Rates { get; set; } = new();
    }
}

public sealed record CurrencyConversionResult(
    string Query,
    decimal Amount,
    string From,
    string To,
    decimal Rate,
    decimal Converted,
    string Formatted,
    string ResultText,
    string RateText);
