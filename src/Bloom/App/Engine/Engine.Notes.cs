using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// 메모장 in the search line: "메모 우유 사기" keeps a new note at once; notes whose text matches open in 메모.
internal sealed partial class Engine
{
    [GeneratedRegex(@"^\s*(?:메모|note)\s+(\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NoteLine();

    /// The notes section for a typed line; `first`: it starts with a new note to keep (it goes above everything).
    private Section? RootNotes(string text, Query q, out bool first)
    {
        first = false;
        var s = new Section(Tr("메모", "Notes"));
        if (NoteLine().Match(text) is { Success: true } m)
        {
            string body = m.Groups[1].Value.Trim();
            first = true;
            s.Items.Add(new ResultItem
            {
                Id = "note:new",
                Kind = ItemKind.Command,
                Title = Tr($"메모로 남기기: {body}", $"Keep as a note: {body}"),
                Subtitle = Tr("Enter로 메모 저장", "Enter keeps it · it goes into Notes"),
                Icon = IconSpec.Of(G.Document, true),
                PrimaryLabel = Tr("저장", "Keep"),
                Run = () =>
                {
                    Notes.New(body);
                    host.Window.ShowToast(Tr("메모 저장 완료", "Kept as a note"));
                },
            });
        }
        foreach (var (n, _) in Notes.Ordered.Select(n => (n, sc: Matcher.Score(q, n.Match))).Where(x => x.sc > 0).OrderByDescending(x => x.sc).Take(3))
        {
            string id = n.Id;
            s.Items.Add(new ResultItem
            {
                Id = "note:" + id,
                Kind = ItemKind.Command,
                Title = n.Title,
                Subtitle = Tr($"메모 · {Ui.ToolUi.Ago(n.UpdatedAt)}", $"Note · {Ui.ToolUi.Ago(n.UpdatedAt)}"),
                Icon = IconSpec.Of(G.Document, true),
                PrimaryLabel = Tr("열기", "Open"),
                StayOpen = true,
                Run = () => host.Tools.Open("notes", id),
            });
        }
        return s.Items.Count > 0 ? s : null;
    }
}
