using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CurrencyConverter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
        private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

        private readonly ExchangeRateService _service = new();
        private CancellationTokenSource? _inFlight;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadCurrenciesAsync();
        }

        private async Task LoadCurrenciesAsync()
        {
            SetBusy(true, "Loading currencies...");

            IReadOnlyList<Currency> currencies;
            string status;

            try
            {
                currencies = await _service.GetCurrenciesAsync();
                status = $"{currencies.Count} currencies available.";
            }
            catch (Exception ex)
            {
                currencies = ExchangeRateService.FallbackCurrencies;
                status = $"Offline — using a built-in list of {currencies.Count} currencies. ({ex.Message})";
            }

            FromBox.ItemsSource = currencies;
            ToBox.ItemsSource = currencies;
            FromBox.SelectedValue = currencies.Any(c => c.Code == "USD") ? "USD" : currencies[0].Code;
            ToBox.SelectedValue = currencies.Any(c => c.Code == "INR") ? "INR" : currencies[^1].Code;

            SetBusy(false, status);
        }

        private async Task ConvertAsync()
        {
            if (!TryReadAmount(out var amount))
            {
                SetStatus("Enter a valid, non-negative amount.", isError: true);
                return;
            }

            if (FromBox.SelectedValue is not string from || ToBox.SelectedValue is not string to)
            {
                SetStatus("Pick both a source and a target currency.", isError: true);
                return;
            }

            // Supersede any conversion still running, so out-of-order replies cannot win.
            _inFlight?.Cancel();
            using var cts = new CancellationTokenSource();
            _inFlight = cts;

            SetBusy(true, "Fetching rate...");

            try
            {
                var result = await _service.ConvertAsync(amount, from, to, cts.Token);

                ResultText.Text = ExchangeRateService.Format(result.Amount, to);
                RateText.Text = $"1 {from} = {result.Rate.ToString("0.######", CultureInfo.CurrentCulture)} {to}"
                              + $"  ·  rate of {result.Date:yyyy-MM-dd}";
                SetBusy(false, "Converted.");
            }
            catch (OperationCanceledException)
            {
                // A newer conversion took over; it owns the UI from here.
            }
            catch (Exception ex)
            {
                ResultText.Text = "—";
                RateText.Text = "No result.";
                SetBusy(false, $"Could not fetch the rate: {ex.Message}", isError: true);
            }
            finally
            {
                if (ReferenceEquals(_inFlight, cts))
                    _inFlight = null;
            }
        }

        private bool TryReadAmount(out decimal amount) =>
            decimal.TryParse(
                AmountBox.Text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out amount) && amount >= 0;

        private void SetBusy(bool busy, string status, bool isError = false)
        {
            ConvertButton.IsEnabled = !busy;
            SwapButton.IsEnabled = !busy;
            RefreshButton.IsEnabled = !busy;
            ConvertIcon.Spin = busy;
            SetStatus(status, isError);
        }

        private void SetStatus(string text, bool isError = false)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? ErrorBrush : MutedBrush;
        }

        private async void ConvertButton_Click(object sender, RoutedEventArgs e) => await ConvertAsync();

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadCurrenciesAsync();

        private void SwapButton_Click(object sender, RoutedEventArgs e)
        {
            (FromBox.SelectedValue, ToBox.SelectedValue) = (ToBox.SelectedValue, FromBox.SelectedValue);
        }

        /// <summary>Stale results should not linger once the inputs no longer match them.</summary>
        private void Input_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            ResultText.Text = "—";
            RateText.Text = "Press Convert to update.";
        }

        [GeneratedRegex(@"^[0-9]*[.,]?[0-9]*$")]
        private static partial Regex AmountPattern();

        private void AmountBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var box = (TextBox)sender;
            var candidate = box.Text.Remove(box.SelectionStart, box.SelectionLength)
                                   .Insert(box.SelectionStart, e.Text);
            e.Handled = !AmountPattern().IsMatch(candidate);
        }
    }
}
