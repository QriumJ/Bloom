using System;
using System.Collections.Generic;

namespace Bloom.Ai;

/// One account's card as one provider answered; AiUsage sets Id.
internal sealed record UsageAccount(string Name, string? Plan, List<UsageLimit> Limits, DateTime Fetched)
{
    public string Id { get; init; } = ""; // the AiAccount it belongs to
    public string? Who { get; init; } // the account's e-mail, shown when one kind has more than one card

    /// ChatGPT's plan as Codex's app-server and wham name it, in the card's words: the three Pro tiers by their monthly
    /// price (the user's call, 10-12), from Codex's own names (prolite, pro, promax; OpenAI publishes no table), the
    /// rest with a capital (plus → Plus).
    public static string? ChatGptPlan(string? type) => type?.ToLowerInvariant() switch
    {
        null or "" => null,
        "prolite" => "Pro 100",
        "pro" => "Pro 200",
        "promax" => "Pro 500",
        _ => char.ToUpperInvariant(type[0]) + type[1..],
    };
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
