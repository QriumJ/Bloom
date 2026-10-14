using System;
using Bloom.Ai;

namespace Bloom.Tests;

/// 소진 예상 (UsagePace): the run-out at the average rate since the window started, and when there is none.
internal static class UsagePaceTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0);

    private static UsageLimit Week(double percent, TimeSpan untilReset) => new("주간", percent, Now + untilReset, UsageLimit.WeekMins);

    /// 50 % in the first 3.5 of 7 days → the other half takes the same 3.5 days, which ends right at the reset: none.
    /// 60 % in 3.5 days → the other 40 % takes 2.33 days, more than a day before the reset.
    public static void AverageRate()
    {
        Check.Eq(UsagePace.RunOut(Week(50, TimeSpan.FromDays(3.5)), Now), null, "exactly on pace: lasts to the reset");
        Check.Eq(UsagePace.RunOut(Week(40, TimeSpan.FromDays(3.5)), Now), null, "slower than the window: lasts");
        var out60 = UsagePace.RunOut(Week(60, TimeSpan.FromDays(3.5)), Now)!.Value;
        Check.Near((out60 - Now).TotalDays, 3.5 * 40 / 60, 1e-6, "40 % left at 60 %/3.5 days");
        var five = new UsageLimit("5시간", 80, Now.AddHours(2), UsageLimit.SessionMins); // 3 h gone, 80 % used
        Check.Near((UsagePace.RunOut(five, Now)!.Value - Now).TotalMinutes, 45, 1e-6, "20 % more at 80 %/3 h is 45 min");
    }

    public static void NoneWithoutWhatItNeeds()
    {
        Check.Eq(UsagePace.RunOut(new UsageLimit("주간", 60, null, UsageLimit.WeekMins), Now), null, "reset unknown");
        Check.Eq(UsagePace.RunOut(new UsageLimit("주간", 60, Now.AddDays(3.5)), Now), null, "window length unknown");
        Check.Eq(UsagePace.RunOut(Week(0, TimeSpan.FromDays(3)), Now), null, "nothing used");
        Check.Eq(UsagePace.RunOut(Week(100, TimeSpan.FromDays(3)), Now), null, "used up already");
        Check.Eq(UsagePace.RunOut(Week(60, TimeSpan.FromDays(-1)), Now), null, "the reset has passed");
        Check.Eq(UsagePace.RunOut(Week(60, TimeSpan.FromDays(8)), Now), null, "the reset is further off than the window");
    }

    /// Under 10 % of the window gone is too early; a little past it there is an answer.
    public static void TooEarly()
    {
        var window = TimeSpan.FromMinutes(UsageLimit.WeekMins);
        Check.Eq(UsagePace.RunOut(Week(30, window * 0.91), Now), null, "9 % of the window gone");
        Check.True(UsagePace.RunOut(Week(30, window * 0.89), Now) is not null, "11 % gone: 30 % used runs out before the reset");
    }

    /// The providers' window lengths reach the limit: wham seconds and Claude's two fixed windows.
    public static void WindowsFromProviders()
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse("""
            {"plan_type":"plus","rate_limit":{"primary_window":{"used_percent":12,"limit_window_seconds":18000,"reset_after_seconds":3600},
              "secondary_window":{"used_percent":48,"limit_window_seconds":604800,"reset_after_seconds":3600}}}
            """)!;
        var a = ChatGptLight.Parse(body, null, Now)!;
        Check.Eq((a.Limits[0].WindowMins, a.Limits[1].WindowMins), ((int?)300, (int?)10080), "wham window seconds → minutes");
        var claude = ClaudeUsage.Parse("Current session: 3% used · resets 3pm\nCurrent week (all models): 40% used · resets Oct 20, 3pm", Now);
        Check.Eq((claude[0].WindowMins, claude[1].WindowMins), ((int?)300, (int?)10080), "Claude's session and week");
    }
}
