using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace sample_webapi_rx_streams_csharp
{
    /// <summary>
    /// The TradeStation WebAPI communicates over HTTP. .NET 4.5+ includes
    /// an HttpClient with some Async options. In this sample, you can
    /// see how to open an async stream over HTTP and update changes
    /// as they come in.
    /// </summary>
    class API
    {
        /// <summary>
        /// Start a Quote/Changes Stream
        /// </summary>
        /// <param name="symbolList">
        /// Collection of comma-delimited symbols (i.e.: MSFT,GOOG)
        /// </param>
        /// <param name="token">API Access Token</param>
        /// <returns>A stream of chunked-encoded HTTP messages</returns>
        public static async Task<Stream> GetQuoteChangesStreamAsync(String symbolList, String token)
        {
            // A single request should not contain more than 30 symbols. If you require more than
            // that, the application should consider a batching mechanism. In this sample, the
            // stream will not initiate and the counter will decrement in the UI.
            if (symbolList.Split(new char[] { ',' }).Length >= 30)
            {
                throw new ArgumentException();
            }

            // Details about this endpoint can be found in our documentation:
            // https://tradestation.github.io/webapi-docs/en/stream/quote-changes/
            const String ENDPOINT = "stream/quote/changes";
            var query = String.Format("{0}/{1}?oauth_token={2}", ENDPOINT, symbolList, token);

            using (var client = new HttpClient()
            {
                // We are using the SIM environment in this sample.
                // When you are ready for LIVE, then the BaseAddress will
                // need to change. You can look at the available environments
                // in our documentation: http://tradestation.github.io/webapi-docs/
                BaseAddress = new Uri("http://sim.api.tradestation.com/v2/"),

                // If a stream takes more than 6 seconds to respond, consider it
                // a problem. Timeout and retry the request. However, note that more symbols
                // in a stream request will extend this timeout. You may want to increase the
                // timeout threshold if you are maxing out the number of symbols in a request.
                Timeout = TimeSpan.FromMilliseconds(6000)
            })
            {
                // This operation does not block. The returned task object will complete after the
                // whole response body is read. This method does not buffer the stream.
                // See more: https://msdn.microsoft.com/en-us/library/hh551739(v=vs.118).aspx
                return await client.GetStreamAsync(query);
            }
        }

        /// <summary>
        /// Observe Quote objects from a Stream
        /// </summary>
        /// <param name="streamAsync">An async chuncked-encoded HTTP Stream</param>
        /// <returns>A Quotes Observable</returns>
        public static IObservable<Quote> StreamQuotes(Task<Stream> streamAsync)
        {
            // Observe response updates on the streaming HTTP connection
            return StreamQuery(streamAsync)
                // From an open stream we are deserializing json data into a dictionary.
                // You don't have to deserialize to a dictionary first, but we've found it easier
                // to build snapshots with dictionaries before serializing to a Quote object.
                .Select(json => DeserializeJsonToDict(json))

                // This extension method will create a snapshot observable from the quote changes
                // dictionary.
                .BuildSnapshot()

                // Serialize the Quote Snapshot dictionary to a Quote Object Observable
                .Select(result => ConvertDictToQuote(result));
        }

        /// <summary>
        /// Utility method to deserialize from JSON to a Dictionary
        /// </summary>
        /// <param name="json">The JSON chunk from the HTTP response stream</param>
        /// <returns>A Dictionary of keys and values</returns>
        private static Dictionary<string, object> DeserializeJsonToDict(String json)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                // In this case, the json failed to serialize. You may want to implement some kind
                // of buffering mechanism to capture input between chunks and and try to re-
                // serialize.
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Utility to convert dictionary to Quote object
        /// </summary>
        /// <param name="snapshot">The Quote Snapshot as a dictionary</param>
        /// <returns>A serialized Quote object from the snapshot</returns>
        private static Quote ConvertDictToQuote(Dictionary<string, object> snapshot)
        {
            var newQuote = new Quote();
            foreach (var key in snapshot.Keys)
            {
                typeof(Quote).GetProperty(key).SetValue(newQuote, snapshot[key]);
            }
            return newQuote;
        }

        /// <summary>
        /// Utility method to convert an async stream to an Observable
        /// </summary>
        /// <param name="streamAsync">An asynchronous HTTP stream</param>
        /// <returns>A String Observable</returns>
        private static IObservable<String> StreamQuery(Task<Stream> streamAsync)
        {
            const String END = "END";
            const String ERROR = "ERROR";

            return Observable.Create<String>(async observer =>
            {
                try
                {
                    using (var stream = await streamAsync)
                    {
                        // It could take a long time before getting another update.
                        // This is especially true if the application is leaving a stream when the
                        // market is closed, such as over a weekend. By making the ReadTimeout
                        // infinite, the application should keep the stream open as long as the
                        // connection is not interrupted.
                        stream.ReadTimeout = Timeout.Infinite;

                        using (var reader = new StreamReader(stream))
                        {
                            while (!reader.EndOfStream)
                            {
                                var line = await reader.ReadLineAsync();

                                // All lines in a stream end in \r\n, we can assume the following
                                // failure scenarios:
                                // Line ends with END - The stream has ended on the server-side
                                // Line contains ERROR - There is an error with one of the symbols
                                // Note: in the future, an individual Symbol will ERROR without
                                // killing the entire stream.
                                // If any of these scenarios happen, consider retrying the request.
                                if (line.ToUpper().EndsWith(END) ||
                                    line.ToUpper().Contains(ERROR))
                                {
                                    observer.OnCompleted();
                                    return;
                                }

                                observer.OnNext(line);
                            }

                            // Streams are not guaranteed to run forever, they may end abruptly
                            // mid-stream. There could be a variety of reasons for this, but in any
                            // case, your application will want to initiate some retry mechanism.
                            observer.OnCompleted();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // A stream failed to start, your application will want to initiate some retry
                    // mechanism.
                    observer.OnError(ex);
                }
            });
        }
    }
}
