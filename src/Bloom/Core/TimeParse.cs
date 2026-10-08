using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Bloom.Core;

/// What a line of text asks for: a timer of some length, or a reminder at some moment with its words.
internal sealed record TimerAsk(string Kind, TimeSpan? Length, DateTime Due, string Label);

/// Reads "10분 타이머", "타이머 1시간 30분", "timer 5m", "30분 뒤 빨래 확인", "15:30 회의 알림", "오후 3시 반에 전화 알려줘",
/// "내일 9시 업로드 리마인더". A bare time with no timer / reminder word ("10분") is not taken (it may be a search).
internal static partial class TimeParse
{
    [GeneratedRegex(@"(에|at)\s")]
    private static partial Regex AtWord();

    [GeneratedRegex(@"(내일|tomorrow|오전|오후|\bam\b|\bpm\b)", RegexOptions.IgnoreCase)]
    private static partial Regex DayWords();

    [GeneratedRegex(@"^\s*(에|at|to)\s+|\s+(에|at)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EdgeWords();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(시간|hours?|hrs?|h|분|minutes?|mins?|m|초|seconds?|secs?|s)(?![a-z가-힣])", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPart();

    [GeneratedRegex(@"(?:(오전|오후|am|pm)\s*)?(\d{1,2})(?::(\d{2})|\s*시(?:\s*(\d{1,2})\s*분|\s*(반))?)?(?:\s*(am|pm)\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex ClockPart();

    [GeneratedRegex(@"(타이머|timer|스톱워치)", RegexOptions.IgnoreCase)]
    private static partial Regex TimerWord();

    [GeneratedRegex(@"(알림|알려\s*줘|알려줘|리마인더|리마인드|remind(?:er)?(?:\s+me)?)", RegexOptions.IgnoreCase)]
    private static partial Regex ReminderWord();

    [GeneratedRegex(@"(?<![가-힣])(뒤에|뒤|후에|후|있다가|in|after)(?![가-힣a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex AfterWord();

    public static TimerAsk? Read(string text)
    {
        string t = text.Trim();
        if (t.Length < 2) return null;
        bool timerWord = TimerWord().IsMatch(t), reminderWord = ReminderWord().IsMatch(t), afterWord = AfterWord().IsMatch(t);

        // a length: "1시간 30분", "90초", "5m"
        TimeSpan length = TimeSpan.Zero;
        var durations = DurationPart().Matches(t);
        string rest = t;
        foreach (Match m in durations)
        {
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) continue;
            string u = m.Groups[2].Value.ToLowerInvariant();
            length += u.StartsWith('시') || u.StartsWith('h') ? TimeSpan.FromHours(n) : u.StartsWith('분') || u.StartsWith('m') ? TimeSpan.FromMinutes(n) : TimeSpan.FromSeconds(n);
            rest = rest.Replace(m.Value, " ");
        }

        // a clock time: "15:30", "오후 3시 반", "9시", "3:30pm", "3pm" (only when no length was given); a bare number
        // ("2 apples") is not a time
        DateTime? at = null;
        Match? clock = null;
        if (length == TimeSpan.Zero)
            foreach (Match m in ClockPart().Matches(t))
                if (m.Groups[3].Success || m.Value.Contains('시') || m.Groups[1].Success || m.Groups[6].Success)
                {
                    clock = m;
                    break;
                }
        if (clock is { } c)
        {
            int h = int.Parse(c.Groups[2].Value, CultureInfo.InvariantCulture);
            int min = c.Groups[3].Success ? int.Parse(c.Groups[3].Value, CultureInfo.InvariantCulture) : c.Groups[4].Success ? int.Parse(c.Groups[4].Value, CultureInfo.InvariantCulture) : c.Groups[5].Success ? 30 : 0;
            string ampm = (c.Groups[1].Value + c.Groups[6].Value).ToLowerInvariant();
            if (ampm is "오후" or "pm" && h < 12) h += 12;
            if (ampm is "오전" or "am" && h == 12) h = 0;
            if (h is >= 0 and < 24 && min is >= 0 and < 60)
            {
                var d = DateTime.Today.AddHours(h).AddMinutes(min);
                bool tomorrow = t.Contains("내일", StringComparison.Ordinal) || t.Contains("tomorrow", StringComparison.OrdinalIgnoreCase);
                if (tomorrow) d = d.AddDays(1);
                // "3시" with no 오전/오후 that has passed: the same hour in the afternoon if that is still ahead, else tomorrow
                else if (d <= DateTime.Now && ampm.Length == 0 && h < 12 && d.AddHours(12) > DateTime.Now) d = d.AddHours(12);
                else if (d <= DateTime.Now) d = d.AddDays(1);
                at = d;
                rest = rest.Replace(c.Value, " ");
            }
        }
        if (length == TimeSpan.Zero && at is null) return null;
        if (!timerWord && !reminderWord && !(afterWord && length > TimeSpan.Zero) && !(at is not null && AtWord().IsMatch(t))) return null;

        // the words left are the label
        rest = TimerWord().Replace(rest, " ");
        rest = ReminderWord().Replace(rest, " ");
        rest = AfterWord().Replace(rest, " ");
        rest = DayWords().Replace(rest, " ");
        rest = EdgeWords().Replace(rest, " ");
        rest = Spaces().Replace(rest, " ").Trim(' ', ',', '.', '·');
        if (rest is "에" or "at") rest = "";

        if (length > TimeSpan.Zero)
        {
            if (length > TimeSpan.FromDays(7)) return null;
            bool reminder = reminderWord || (afterWord && rest.Length > 0 && !timerWord);
            string label = rest.Length > 0 ? rest : reminder ? Tr("알림", "Reminder") : Tr($"{Timers.DurationText(length)} 타이머", $"{Timers.DurationText(length)} timer");
            return new TimerAsk(reminder ? "reminder" : "timer", length, DateTime.Now + length, label);
        }
        return new TimerAsk("reminder", null, at!.Value, rest.Length > 0 ? rest : Tr("알림", "Reminder"));
    }
}
