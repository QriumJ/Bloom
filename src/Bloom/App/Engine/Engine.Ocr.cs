using System.Collections.Generic;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The text read from the screen: what to do with it (the text itself is in the preview pane).
internal sealed partial class Engine
{
    /// Bloom Lens: the area just picked (its picture on the right), and the six things to do with it.
    public List<Section> LensView()
    {
        if (host.LastLens is not { } l)
            return new List<Section> { new Section("Bloom Lens") { Items = { new ResultItem { Id = "lens:none", Kind = ItemKind.Info, Title = Tr("고른 영역이 없어요", "No area picked"), Subtitle = Tr("Enter로 영역 고르기", "Enter picks an area"), Icon = IconSpec.Of(G.Page), PrimaryLabel = Tr("고르기", "Pick"), Run = () => host.StartLens("menu") } } } };
        Search.Detail Preview()
        {
            var d = new Search.Detail { Image = l.Png };
            d.Add(Tr("영역", "Area"), $"{l.Rect.Width} × {l.Rect.Height}").Add(Tr("고른 때", "Picked at"), l.At.ToString("HH:mm:ss"));
            return d;
        }
        ResultItem Item(string id, string title, string sub, char glyph, bool accent, string label, bool stay, string kind) => new()
        {
            Id = id,
            Kind = ItemKind.Lens,
            Title = title,
            Subtitle = sub,
            Icon = IconSpec.Of(glyph, accent),
            PrimaryLabel = label,
            StayOpen = stay,
            Run = () => _ = host.LensDo(kind),
            Detail = Preview,
        };
        var s = new Section("Bloom Lens", Tr("1~6으로 바로", "1–6 right away"));
        s.Items.Add(Item("lens:ask", Tr("1  AI에게 묻기", "1  Ask AI"), Tr("이 화면을 그림으로 보내고 물어봐요 · 사용량 1회", "Sends this part of the screen as a picture · 1 use"), G.Sparkle, true, Tr("묻기", "Ask"), true, "ask"));
        s.Items.Add(Item("lens:translate", Tr("2  번역", "2  Translate"), Tr("글자를 읽어 번역해요 · 사용량 1회", "Reads the text and translates it · 1 use"), G.Globe, false, Tr("번역", "Translate"), false, "translate"));
        s.Items.Add(Item("lens:text", Tr("3  글자 복사", "3  Copy text"), Tr("PC 안에서 읽어 복사해요 · 사용량 없음", "Read on this PC and copied · no usage"), G.Copy, false, Tr("복사", "Copy"), true, "text"));
        s.Items.Add(Item("lens:live", Tr("4  실시간 번역", "4  Live translation"), Tr("이 영역을 계속 읽어 바뀔 때만 번역해요 · 사용량 계속", "Keeps reading this area and translates when it changes · ongoing usage"), G.Refresh, false, Tr("시작", "Start"), false, "live"));
        s.Items.Add(Item("lens:pin", Tr("5  화면에 고정", "5  Pin to the screen"), Tr("이 영역을 그림으로 맨 위에 띄워 둬요 · 끌어서 옮기고 휠로 크기", "Keeps this area on top as a picture · drag to move, wheel to size"), G.Pin, false, Tr("고정", "Pin"), false, "pin"));
        s.Items.Add(Item("lens:capture", Tr("6  캡처", "6  Capture"), Tr("이 영역을 그림으로 복사해요 · Ctrl+V로 어디든 붙여넣기", "Copies this area as a picture · paste it anywhere with Ctrl+V"), G.Camera, false, Tr("복사", "Copy"), false, "capture"));
        var again = new Section(null);
        again.Items.Add(new ResultItem { Id = "lens:again", Kind = ItemKind.Lens, Title = Tr("다른 영역 고르기", "Pick another area"), Icon = IconSpec.Of(G.Page), PrimaryLabel = Tr("고르기", "Pick"), Run = () => host.StartLens("menu"), Detail = Preview });
        return new List<Section> { s, again };
    }

    public List<Section> OcrView()
    {
        var s = new Section(Tr("읽은 글자", "Text read"), host.LastOcr is { } o ? Tr($"{o.Lines.Count}줄 · Windows OCR · PC 안에서 읽었어요", $"{o.Lines.Count} lines · Windows OCR · read on this PC") : null);
        if (host.LastOcr is not { } r)
        {
            s.Items.Add(new ResultItem { Id = "ocr:none", Kind = ItemKind.Info, Title = Tr("아직 읽은 글자가 없어요", "No text read yet"), Icon = IconSpec.Of(G.Info) });
            return new List<Section> { s };
        }
        string text = r.Text;
        Search.Detail Preview()
        {
            var d = new Search.Detail { Text = text };
            d.Add(Tr("줄", "Lines"), Tr($"{r.Lines.Count}줄", $"{r.Lines.Count}")).Add(Tr("글자 수", "Characters"), Tr($"{text.Length:#,0}자", $"{text.Length:#,0}")).Add(Tr("인식 언어", "Recognized as"), r.Language);
            return d;
        }
        ResultItem Item(string id, string title, string? sub, char glyph, bool accent, string label, System.Action run, bool stay = false) => new()
        {
            Id = id,
            Kind = ItemKind.Ocr,
            Title = title,
            Subtitle = sub,
            Icon = IconSpec.Of(glyph, accent),
            PrimaryLabel = label,
            Run = run,
            StayOpen = stay,
            Detail = Preview,
        };
        s.Items.Add(Item("ocr:copy", Tr("텍스트 복사", "Copy the text"), Settings.Current.OcrCopy ? Tr("이미 복사해 뒀어요", "Already copied") : null, G.Copy, false, Tr("복사", "Copy"), () => Launch.Copy(text)));
        s.Items.Add(Item("ocr:translate", Tr("번역하기", "Translate"), Tr("Claude/ChatGPT 사용량 1회", "1 use of Claude/ChatGPT"), G.Globe, false, Tr("번역", "Translate"), () => new Ocr.TranslateWindow(host, new Ocr.ScreenRect(0, 0, 0, 0), text, false).Show()));
        s.Items.Add(Item("ocr:ai", Tr("AI에게 이 글로 묻기", "Ask AI about this text"), null, G.Sparkle, true, Tr("묻기", "Ask"), () => host.Window.ShowChat(host.Chats.New(), null, (Tr("화면 텍스트", "Screen text"), "", text)), stay: true));
        s.Items.Add(Item("ocr:oneline", Tr("한 줄로 합쳐서 복사", "Copy as one line"), Tr("줄바꿈을 띄어쓰기로", "Line breaks become spaces"), G.Copy, false, Tr("복사", "Copy"), () => Launch.Copy(string.Join(" ", text.Split('\n')))));
        s.Items.Add(Item("ocr:again", Tr("다른 영역 고르기", "Pick another area"), null, G.Page, false, Tr("다시 고르기", "Pick again"), () => host.StartLens("menu")));
        return new List<Section> { s };
    }
}
