using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace Bloom.Ui;

/// Append a long list without holding the UI thread through every remaining row.
internal static class RenderSlices
{
    private const int MaxSteps = 4;
    private const double BudgetMilliseconds = 4;

    // The first call already runs at Background priority (or after the opening's reveal).
    public static void Run(Dispatcher dispatcher, int first, int count, Action<int> step, Func<bool> current, Action? completed = null)
    {
        int next = first;
        void Slice()
        {
            long started = Stopwatch.GetTimestamp();
            int built = 0;
            while (next < count && built < MaxSteps)
            {
                if (!current()) return;
                step(next++);
                built++;
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= BudgetMilliseconds) break;
            }
            if (!current()) return;
            if (next < count) dispatcher.BeginInvoke(Slice, DispatcherPriority.Background);
            else completed?.Invoke();
        }
        Slice();
    }
}
