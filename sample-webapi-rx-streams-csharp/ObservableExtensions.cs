using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

namespace sample_webapi_rx_streams_csharp
{
    internal static class ObservableExtensions
    {
        /// <summary>
        /// A Utility method that will build a snapshot from the latest changes
        /// </summary>
        /// <param name="source">A Dictionary Observable</param>
        /// <returns>An quote snapshot observable</returns>
        public static IObservable<Dictionary<string, object>>
            BuildSnapshot(this IObservable<Dictionary<string, object>> source)
        {
            const String SYMBOL = "Symbol";

            // This Scan will build a new list of snapshots on every update
            return source.Scan(
                Tuple.Create(
                new List<Dictionary<string, object>>(),
                new Dictionary<string, object>()), (acc, quote) =>
                {
                    var quoteSnapshot = acc.Item1
                        .Where(q => (string)q[SYMBOL] == (string)quote[SYMBOL])
                        .FirstOrDefault();

                    if (quoteSnapshot != null)
                    {
                        // Everytime a quote change is provided, we need to build a snapshot by
                        // appending the new changes over the previous snapshot.
                        foreach (var key in quote.Keys)
                        {
                            quoteSnapshot[key] = quote[key];
                        }
                        acc.Item1[acc.Item1.IndexOf(quoteSnapshot)] = quoteSnapshot;
                        return Tuple.Create(acc.Item1, quoteSnapshot);
                    }
                    else
                    {
                        // The TradeStation WebAPI always provides a quote snapshot as the first
                        // response in a quote changes stream. We want to capture that snapshot
                        // here.
                        acc.Item1.Add(quote);
                        return Tuple.Create(acc.Item1, quote);
                    }
                })
                // Grab the Quote Snapshot from the Tuple
                .SelectMany(tuple => Observable.Return(tuple.Item2));
        }
    }
}
