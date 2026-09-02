using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace CurrencyConverter
{
    /// <summary>A currency as shown in the From/To pickers.</summary>
    /// <remarks>
    /// MainWindow.xaml binds to these two members by name:
    /// DisplayMemberPath="Display" and SelectedValuePath="Code".
    /// </remarks>
    public sealed record Currency(string Code, string Name)
    {
        public string Display => $"{Code} — {Name}";
    }

    /// <summary>Outcome of a single conversion.</summary>
    /// <param name="Amount">The converted amount, in the target currency.</param>
    /// <param name="Rate">Units of target currency per one unit of source currency.</param>
    /// <param name="Date">Date the published rate applies to.</param>
    public sealed record ConversionResult(decimal Amount, decimal Rate, DateOnly Date);

    /// <summary>
    /// Looks up reference rates from the Frankfurter API (ECB data, no API key required).
    /// </summary>
    public sealed class ExchangeRateService
    {
        private static readonly HttpClient Http = new()
        {
            BaseAddress = new Uri("https://api.frankfurter.app/"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Used when the currency list cannot be fetched, so the UI still works offline.</summary>
        public static readonly IReadOnlyList<Currency> FallbackCurrencies =
        [
            new("AUD", "Australian Dollar"),
            new("CAD", "Canadian Dollar"),
            new("CHF", "Swiss Franc"),
            new("CNY", "Chinese Renminbi Yuan"),
            new("EUR", "Euro"),
            new("GBP", "British Pound"),
            new("INR", "Indian Rupee"),
            new("JPY", "Japanese Yen"),
            new("SGD", "Singapore Dollar"),
            new("USD", "United States Dollar")
        ];

        /// <summary>Fetches every currency the rate service supports, ordered by code.</summary>
        public async Task<IReadOnlyList<Currency>> GetCurrenciesAsync(CancellationToken ct = default)
        {
            var map = await Http.GetFromJsonAsync<Dictionary<string, string>>("currencies", JsonOptions, ct)
                          .ConfigureAwait(false);

            if (map is null || map.Count == 0)
                throw new InvalidOperationException("The rate service returned an empty currency list.");

            return map.Select(kvp => new Currency(kvp.Key, kvp.Value))
                      .OrderBy(c => c.Code, StringComparer.Ordinal)
                      .ToList();
        }

        /// <summary>Converts <paramref name="amount"/> from one currency to another.</summary>
        public async Task<ConversionResult> ConvertAsync(
            decimal amount, string from, string to, CancellationToken ct = default)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return new ConversionResult(amount, 1m, DateOnly.FromDateTime(DateTime.Today));

            // Ask for the unit rate and scale locally, so an amount of 0 still yields a usable rate.
            var url = $"latest?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";

            var payload = await Http.GetFromJsonAsync<RatesResponse>(url, JsonOptions, ct).ConfigureAwait(false)
                          ?? throw new InvalidOperationException("The rate service returned an empty response.");

            if (payload.Rates is null || !payload.Rates.TryGetValue(to, out var rate))
                throw new InvalidOperationException($"No published rate for {from} to {to}.");

            return new ConversionResult(amount * rate, rate, payload.Date);
        }

        /// <summary>Formats an amount with the currency code, e.g. <c>1,234.56 EUR</c>.</summary>
        public static string Format(decimal amount, string code) =>
            string.Create(CultureInfo.CurrentCulture, $"{amount:N2} {code}");

        private sealed record RatesResponse(decimal Amount, string? Base, DateOnly Date, Dictionary<string, decimal>? Rates);
    }
}
