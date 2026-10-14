using System;

namespace Bloom.Ai;

/// 소진 예상: when a limit runs out if the rest of its window is used at the average rate of the part gone so far.
internal static class UsagePace
{
    /// Less of the window gone than this: one burst would stand for the whole window, so no guess yet.
    private const double TooEarly = 0.10;

    /// The expected run-out, or null when there is none to give: the reset or the window length is unknown, nothing is
    /// used yet (or all of it already), the window just began, or the limit lasts until the reset.
    public static DateTime? RunOut(UsageLimit limit, DateTime now)
    {
        if (limit.Resets is not { } resets || limit.WindowMins is not { } mins || limit.Percent <= 0 || limit.Percent >= 100) return null;
        var window = TimeSpan.FromMinutes(mins);
        var elapsed = window - (resets - now); // the window started at resets − window
        if (elapsed < window * TooEarly) return null;
        var runOut = now + (elapsed * ((100 - limit.Percent) / limit.Percent));
        return runOut < resets ? runOut : null;
    }
}
