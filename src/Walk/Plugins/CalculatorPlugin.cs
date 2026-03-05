using System.Text.RegularExpressions;
using NCalc;
using Walk.Models;

namespace Walk.Plugins;

public sealed partial class CalculatorPlugin : IQueryPlugin
{
    public string Name => "Calculator";
    public int Priority => 80;

    [GeneratedRegex(@"^[\d\s+\-*/^().,%]+$|.*\b(sqrt|sin|cos|tan|abs|log|pow|round|ceiling|floor|exp|acos|asin|atan|max|min|pi)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex MathPattern();

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed) || !MathPattern().IsMatch(trimmed))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        try
        {
            // Replace ^ with Pow() for user convenience
            var normalized = PowerPattern().Replace(trimmed, "Pow($1,$2)");

            // Replace % with Mod operator for modulo
            // But only when it's used as modulo (number % number), not percentage
            normalized = ModuloPattern().Replace(normalized, "$1 % $2");

            var expression = new Expression(normalized, ExpressionOptions.DecimalAsDefault);
            var result = expression.Evaluate();

            if (result is null)
                return Task.FromResult<IReadOnlyList<SearchResult>>([]);

            var formatted = FormatResult(result);

            var copyAction = new SearchAction
            {
                Label = "Copy to Clipboard",
                Execute = () => System.Windows.Clipboard.SetText(formatted),
                KeyGesture = "Enter"
            };

            IReadOnlyList<SearchResult> results =
            [
                new SearchResult
                {
                    Title = $"= {formatted}",
                    Subtitle = trimmed,
                    PluginName = Name,
                    Score = 0.95,
                    Actions = [copyAction]
                }
            ];
            return Task.FromResult(results);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }
    }

    private static string FormatResult(object result)
    {
        return result switch
        {
            double d => d.ToString("G"),
            decimal m => m.ToString("G"),
            float f => f.ToString("G"),
            _ => result.ToString() ?? "0"
        };
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*\^\s*(\d+(?:\.\d+)?)")]
    private static partial Regex PowerPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%\s*(\d+(?:\.\d+)?)")]
    private static partial Regex ModuloPattern();
}
