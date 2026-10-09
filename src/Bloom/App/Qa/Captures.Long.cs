using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

internal static partial class Captures
{
    /// `--qa-set long` (fresh data, dark): long words and sentences wherever a user's own text shows — the search line,
    /// reminders and timers on the home, a favourite's name, notes (a title and a line with no spaces), the 계산기, a
    /// quick chat and the chat window (a long question, a link with no spaces, a table, a long code line, long titles
    /// and a long group name), a notice card, a translation, the clipboard list and the alarm card. With
    /// BLOOM_DESIGN_CHECK set, what doesn't fit is in the report.
    private static async Task LongScreens(Host host, Launcher w, string dir)
    {
        Theme.Apply("dark");
        Settings.Current.FilesInRoot = false;
        Settings.Current.WindowsInRoot = false;
        string url = "https://example.com/reports/2026/q3/sales-overview-final-v12-really-final-with-a-very-long-name.pdf";

        foreach (string q in new[] { Tr("하트", "heart"), Tr("별", "star"), Tr("커피", "coffee"), Tr("체크", "check") }) // emoji from the older symbol blocks next to newer ones
            await Shot(w, dir, "g00-emoji-" + q, Mode.Emoji, q, 700);
        // the red heart drawn a few ways (it came out as black line art)
        var forms = new[] { ("40", 40), ("64", 64), ("96", 96), ("128", 128), ("168", 168), ("200", 200) };
        foreach (var (name, px) in forms)
            if (ColorEmoji.Render("\u2764\uFE0F", px, Theme.P.Dark) is { } bmp)
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(dir, $"g00-heart-{name}.png"));
                png.Save(fs);
            }
        await Shot(w, dir, "g01-long-query", Mode.Root, Tr("다음 달 팀 워크숍 장소 후보 세 곳의 교통편과 비용을 비교해서 표로 정리해 줘", "Compare travel and cost for the three venues we shortlisted for next month's team workshop"), 700);

        Timers.Add("reminder", Tr("분기 보고서 최종본을 재무팀과 법무팀에 함께 보내고 회신 받기", "Send the final quarterly report to finance and legal and get both replies"), DateTime.Today.AddDays(1).AddHours(15));
        Timers.Add("timer", Tr("오븐 속 바스크 치즈케이크 겉면 색 확인하기", "Check the colour on top of the basque cheesecake"), DateTime.Now.AddMinutes(25));
        Favorites.Add(new Favorites.Entry { Id = "url:" + url, Kind = "url", Title = Tr("사내 위키 · 신규 입사자 온보딩 가이드 전체 목차", "Team wiki: the whole onboarding guide for new starters"), Target = url });
        await Shot(w, dir, "g02-home-long", Mode.Root, "", 900);

        var note = Notes.New(Tr("프로젝트 공개 전에 마지막으로 확인할 일들을 모아 둔 아주 긴 제목의 메모\n- 라이선스 확인\n- 링크 ", "A note with a very long title: everything to check one last time before going public\n- licence\n- link ") + url);
        Notes.New(Tr("짧은 메모", "A short note"));
        await Shot(w, dir, "g03-note-result", Mode.Root, Tr("메모 라이선스", "note licence"), 700);
        w.Go(Mode.Root);
        host.Tools.Open("notes");
        await Group(host, w, dir, "g04-notes-list");
        ((NotesTool)host.Tools.Find("notes")!.Tool).Show(note.Id);
        await Group(host, w, dir, "g05-note-open");
        host.Tools.Open("calc");
        ((CalcTool)host.Tools.Find("calc")!.Tool).TypeForQa("1234567890*987654321+(4500000/3)-12345.6789");
        await Group(host, w, dir, "g06-calc-long");
        host.Tools.Open("timers");
        await Group(host, w, dir, "g07-timers-tool");
        foreach (var t in host.Tools.WindowsForQa.ToArray()) host.Tools.Close(t);

        var chat = host.Chats.New();
        chat.Title = Tr("다음 주 월요일 영업팀 회의 발표 자료 목차와 핵심 문장 정리", "Contents and key lines for Monday's sales team presentation");
        chat.Messages.Add(new ChatMessage { Role = "user", Text = Tr("아래 내용을 바탕으로 다음 주 월요일 오전 회의에서 쓸 발표 자료의 목차와 각 장의 핵심 문장을 정리해 줘. 시간은 15분이고 듣는 사람은 영업팀이야.\n참고 링크: ", "From the notes below, outline the deck for Monday morning's meeting with one key line per section. It's 15 minutes and the audience is the sales team.\nReference: ") + url });
        chat.Messages.Add(new ChatMessage
        {
            Role = "assistant",
            Text = Tr("목차와 핵심 문장이에요.\n\n| 장 | 핵심 문장 |\n|---|---|\n| 1. 3분기 돌아보기 | 석 달 내내 매출이 올랐고 9월이 가장 높았어요 |\n| 2. 무엇이 통했나 | 재구매 고객 비율이 처음으로 사십 퍼센트를 넘었어요 |\n\n```text\nSELECT region, SUM(amount) AS total FROM sales WHERE quarter = 'Q3' GROUP BY region ORDER BY total DESC;\n```\n\n링크는 ",
                "Here are the sections and key lines.\n\n| Section | Key line |\n|---|---|\n| 1. Q3 in review | Sales rose every month and September was the highest |\n| 2. What worked | Repeat customers passed forty percent for the first time |\n\n```text\nSELECT region, SUM(amount) AS total FROM sales WHERE quarter = 'Q3' GROUP BY region ORDER BY total DESC;\n```\n\nThe link is ") + url,
        });
        w.ShowChat(chat);
        await Snap(w, dir, "g08-chat-long");
        var group = host.Chats.NewGroup();
        host.Chats.Rename(group, Tr("2026년 하반기 신규 프로젝트 기획과 예산 검토", "Planning and budget review for the new projects in the second half of 2026"));
        chat.Group = group.Id;
        var cw = new ChatWindow(host, chat) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        try
        {
            cw.Show();
            await Task.Delay(400);
            await cw.Dispatcher.InvokeAsync(() => cw.Capture(Path.Combine(dir, "g09-chatwin-long.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            cw.Width = cw.MinWidth;
            cw.Height = cw.MinHeight;
            await Task.Delay(400);
            await cw.Dispatcher.InvokeAsync(() => cw.Capture(Path.Combine(dir, "g10-chatwin-long-small.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            cw.Close();
        }

        var nw = new NoticeWindow { Offscreen = true };
        nw.Add(new Notice("done", Tr("Claude Code · 작업 완료", "Claude Code · Done"), Tr("결제 모듈 리팩터링과 통합 테스트 추가, 그리고 오래된 API 호출을 한꺼번에 정리하기", "Refactor the payment module, add integration tests and clean up the old API calls in one go"), Tr("47분 걸렸어요 · 파일 23개를 바꿨어요", "Took 47 min · 23 files changed")));
        nw.Add(new Notice("waiting", Tr("Codex · 승인 대기", "Codex · Needs approval"), url, null));
        await Task.Delay(400);
        await nw.Dispatcher.InvokeAsync(() => nw.Capture(Path.Combine(dir, "g11-notice-long.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        nw.Close();

        var tw = new Ocr.TranslateWindow(host, new Ocr.ScreenRect(0, 0, 0, 0), null, false) { Left = -32000, Top = -32000, ShowActivated = false };
        tw.Show();
        tw.FillForQa(
            "Thanks for joining the onboarding session. Before your first week ends, please finish the security training, set up two-factor sign-in on every work account, and book a short call with your mentor. The full checklist is at " + url,
            Tr("온보딩 세션에 와 주셔서 고마워요. 첫 주가 끝나기 전에 보안 교육을 마치고, 모든 업무 계정에 2단계 로그인을 켜고, 멘토와 짧은 통화 일정을 잡아 주세요. 전체 체크리스트는 여기 있어요: ", "Thanks for joining the onboarding session. Before your first week ends, finish the security training, turn on two-factor sign-in everywhere and book a short call with your mentor. The checklist: ") + url, 1);
        await Task.Delay(300);
        tw.Capture(Path.Combine(dir, "g12-translate-long.png"));
        tw.Close();

        host.Clipboard.Add(new Clip.ClipEntry { Id = "clip:long-qa", Kind = "text", Text = Tr("회의록: 다음 분기 목표는 재구매율 45%, 신규 고객 1,200명, 그리고 고객 문의 응답 시간을 평균 2시간 안으로 줄이는 것. 자료 ", "Minutes: next quarter we aim for 45% repeat buyers, 1,200 new customers and replies to customers within two hours on average. Deck ") + url, Copied = DateTimeOffset.Now.ToUnixTimeMilliseconds(), Source = Tr("메모장", "Notepad") });
        await Shot(w, dir, "g13-clipboard-long", Mode.Clipboard, "", 900);

        var aw = new AlarmWindow(new TimerEntry { Kind = "reminder", Label = Tr("분기 보고서 최종본을 재무팀과 법무팀에 함께 보내고 회신 받기", "Send the final quarterly report to finance and legal and get both replies"), Due = DateTimeOffset.Now.ToUnixTimeMilliseconds() }, _ => { }, false);
        var card = (FrameworkElement)aw.Content;
        aw.Content = null;
        var holder = new System.Windows.Controls.Border { Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x38)), Child = card, Padding = new Thickness(10) };
        holder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        holder.Arrange(new Rect(holder.DesiredSize));
        holder.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(holder.ActualWidth * 1.5), (int)Math.Ceiling(holder.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        rtb.Render(holder);
        DesignCheck.Look(holder, rtb, 1.5, "g14-alarm-long.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(Path.Combine(dir, "g14-alarm-long.png"))) enc.Save(fs);
        aw.Close();
    }
}
