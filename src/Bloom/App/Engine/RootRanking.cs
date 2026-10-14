using System;
using System.Collections.Generic;
using Bloom.Search;

namespace Bloom.App;

/// A matched source and its final score, before expensive row/actions construction.
internal readonly record struct RootCandidate(object Source, int Score);

internal static class RootRanking
{
    public static void AppendTop(List<RootCandidate> candidates, List<ResultItem> output, Func<object, ResultItem> materialize)
    {
        // Keep the same traversal, comparator and List.Sort tie behavior as the root list.
        candidates.Sort((x, y) => y.Score.CompareTo(x.Score));
        for (int i = 0; i < Math.Min(9, candidates.Count); i++)
        {
            var candidate = candidates[i];
            var item = materialize(candidate.Source);
            item.Score = candidate.Score;
            output.Add(item);
        }
    }
}
