using System;
using System.Collections.Generic;

namespace Bloom.Ai;

/// One account's card as one provider answered; AiUsage sets Id.
internal sealed record UsageAccount(string Name, string? Plan, List<UsageLimit> Limits, DateTime Fetched)
{
    public string Id { get; init; } = ""; // the AiAccount it belongs to
    public string? Who { get; init; } // the account's e-mail, shown when one kind has more than one card
}

/// A limit's title, percent used and reset time (null means the reset time is unknown).
internal sealed record UsageLimit(string Title, double Percent, DateTime? Resets)
{
    /// Names a limit window from its length in minutes, as Codex's app-server and wham report it (Claude's /usage names
    /// its own): 300 → "5시간", 10080 → "주간".
    public static string Window(int mins) => mins switch
    {
        10080 => Tr("주간", "Weekly"),
        >= 1440 when mins % 1440 == 0 => Tr($"{mins / 1440}일", $"{mins / 1440}d"),
        >= 60 => Tr($"{mins / 60}시간", $"{mins / 60}h"),
        > 0 => Tr($"{mins}분", $"{mins} min"),
        _ => Tr("한도", "Limit"),
    };
}
