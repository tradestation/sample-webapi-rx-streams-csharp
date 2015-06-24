using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace sample_webapi_rx_streams_csharp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<Quote> quoteChanges;
        private ObservableCollection<Task<Stream>> openStreams;

        public MainWindow()
        {
            InitializeComponent();

            quoteChanges = new ObservableCollection<Quote>();
            openStreams = new ObservableCollection<Task<Stream>>();

            quotes.ItemsSource = quoteChanges;
            openStreams.CollectionChanged += ((s, e) =>
            {
                streamCounter.Content = openStreams.Count();
            });
            startButton.Click += ((s, e) => Start(symbolList.Text, accessToken.Text));
        }

        private void Start(String symbols, String token)
        {
            // Consider watching and managing these streams. Retry opening streams if they die.
            const int MAX_RETRIES = 10;
            for (int attempts = 0; attempts < MAX_RETRIES; attempts++)
            {
                try
                {
                    // Starting the stream is separate from Observing on the Stream.
                    var streamAsync = API.GetQuoteChangesStreamAsync(symbols, token);
                    openStreams.Add(streamAsync);

                    SubscribeToQuotes(streamAsync);
                    break;
                }
                catch { }

                // For this sample, the logic is to wait a little and retry up to 10 times.
                // In a production application, it's appropriate to have a back-off multiplier.
                Thread.Sleep(250);
            }
        }

        private void SubscribeToQuotes(Task<Stream> streamAsync)
        {
            // The TradeStation WebAPI will always return a 200 HTTP status for streams regardless
            // of success or failure. You will want to handle connection issues here.
            try
            {
                API.StreamQuotes(streamAsync)

                    // Schedule each Stream on its own Thread You might see other Rx examples using
                    // Scheduler.ThreadPool, but that is now deprecated.
                    .SubscribeOn(NewThreadScheduler.Default)

                    // This is a WPF-specific feature and comes with the Rx-WPF Helpers NuGet
                    // package. If you are not building a WPF app, you can skip this.
                    .ObserveOnDispatcher()

                    // Subscribe to Quotes and update the UI
                    .Subscribe(
                    quote =>
                    {
                        // Upsert the quote that comes in from the stream
                        var existingQuote = quoteChanges
                            .Where(q => q.Symbol == quote.Symbol)
                            .FirstOrDefault();

                        if (existingQuote != null)
                        {
                            var index = quoteChanges.IndexOf(existingQuote);
                            quoteChanges[index] = quote;
                        }
                        else
                        {
                            quoteChanges.Add(quote);
                        }
                    },
                    ex =>
                    {
                        // In the case that a 401 is returned, you should not retry the request
                        // with the existing token.
                        if (ex.Message.Contains("401"))
                        {
                            // Maybe request a new token.
                        }

                        // The connection failed to open, consider retrying.
                        openStreams.Remove(streamAsync);
                    },
                    () =>
                    {
                        // The connection died mid-stream, consider retrying.
                        openStreams.Remove(streamAsync);
                    });
            }
            catch (Exception)
            {
                // Consider some retry logic here in your application to re-establish the
                // connection.
                openStreams.Remove(streamAsync);
            }
        }
    }
}
