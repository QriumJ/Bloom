using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Tests;

internal static class RootRankingTests
{
    private sealed record Source(string Id);

    public static void ScoresAndTiesMatchOriginalTraversal()
    {
        // Include insertion-sort and quicksort boundaries, with many ties across source groups.
        foreach (int count in new[] { 0, 1, 8, 9, 10, 16, 17, 64, 400 })
        {
            for (int seed = 0; seed < 12; seed++)
            {
                var random = new Random(seed);
                var original = new List<ResultItem>();
                var candidates = new List<RootCandidate>();
                for (int i = 0; i < count; i++)
                {
                    var source = new Source($"{i % 7}:{i}");
                    int score = random.Next(8) * 100 + 300;
                    original.Add(new ResultItem { Id = source.Id, Score = score });
                    candidates.Add(new(source, score));
                }
                original.Sort((x, y) => y.Score.CompareTo(x.Score));
                var output = new List<ResultItem>();
                int made = 0;
                RootRanking.AppendTop(candidates, output, source =>
                {
                    made++;
                    return new ResultItem { Id = ((Source)source).Id };
                });
                Check.Eq(made, Math.Min(9, count), "only retained rows are built");
                Check.Eq(string.Join("|", output.Select(x => $"{x.Id}={x.Score}")),
                    string.Join("|", original.Take(9).Select(x => $"{x.Id}={x.Score}")), "same scores, IDs and tie order");
            }
        }
    }

    public static void ExistingRowsAndLeadingUrlKeepTheirPlace()
    {
        var leading = new ResultItem { Id = "url:typed" };
        var toggle = new ResultItem { Id = "sys:toggle", Score = 720 };
        var output = new List<ResultItem> { leading };
        RootRanking.AppendTop(new() { new(toggle, toggle.Score) }, output, source => (ResultItem)source);
        Check.True(ReferenceEquals(output[0], leading), "typed URL stays before ranked rows");
        Check.True(ReferenceEquals(output[1], toggle), "already built toggle/layout/group rows keep identity");
        Check.Eq(output[1].Score, 720, "precomputed row score is preserved");
    }

    private static ResultItem Materialize(object value)
    {
        var source = (Source)value;
        return new ResultItem
        {
            Id = source.Id,
            Title = source.Id,
            AsFavorite = new Favorites.Entry { Id = source.Id, Title = source.Id },
            Run = () => GC.KeepAlive(source),
            MoreActions = () => new List<ActionItem>(),
        };
    }

    public static void BroadQueryAllocatesFewerRows()
    {
        var sources = Enumerable.Range(0, 400).Select(i => new Source("app:" + i)).ToArray();
        long Measure(bool deferred)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int pass = 0; pass < 30; pass++)
            {
                var output = new List<ResultItem>();
                if (deferred)
                {
                    var candidates = new List<RootCandidate>();
                    for (int i = 0; i < sources.Length; i++) candidates.Add(new(sources[i], i % 10 + 500));
                    RootRanking.AppendTop(candidates, output, Materialize);
                }
                else
                {
                    var original = new List<ResultItem>();
                    for (int i = 0; i < sources.Length; i++)
                    {
                        var item = Materialize(sources[i]);
                        item.Score = i % 10 + 500;
                        original.Add(item);
                    }
                    original.Sort((x, y) => y.Score.CompareTo(x.Score));
                    output.AddRange(original.Take(9));
                }
                GC.KeepAlive(output);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        _ = Measure(false);
        _ = Measure(true);
        long eager = Measure(false), deferred = Measure(true);
        Console.WriteLine($"        root ranking allocations (400 matches x30): eager {eager:N0} B, deferred {deferred:N0} B");
        Check.True(deferred < eager / 2, "deferred rows reduce synthetic broad-query allocations by at least half");
    }
}
