using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// Bloom Lens: picking an area of the screen and what is done with it.
internal sealed partial class Host
{
    /// The recognizer language from the settings (null = the user's own languages).
    public string? OcrLanguage() => Settings.Current.OcrLanguage is { Length: > 0 } l && l != "auto" ? l : null;

    /// Screen text: pick an area, then read it (text), translate it once (translate) or keep
    /// translating it (live). Reading is Windows' own OCR; only translating calls Claude / ChatGPT.
    /// The area picked last (Bloom Lens): where it is, its pixels, its PNG (for the preview and the AI).
    public LensShot? LastLens { get; private set; }

    /// Bloom Lens: pick an area, then "menu" shows what to do with it; ask / text / translate / live do that at once.
    public async void StartLens(string kind)
    {
        if (ocrBusy) return;
        ocrBusy = true;
        try
        {
            Window.Dismiss();
            await Task.Delay(150); // the launcher is off the screen before the picture is taken
            string hint = kind switch
            {
                "ask" => Tr("AI에게 보여줄 영역을 끌어서 고르세요", "Drag over the area to show AI"),
                "live" => Tr("실시간으로 번역할 영역을 끌어서 고르세요", "Drag over the area to translate live"),
                "translate" => Tr("번역할 영역을 끌어서 고르세요", "Drag over the area to translate"),
                "text" => Tr("글자를 읽을 영역을 끌어서 고르세요", "Drag over the area to read"),
                "pin" => Tr("화면에 고정할 영역을 끌어서 고르세요", "Drag over the area to pin"),
                "capture" => Tr("캡처할 영역을 끌어서 고르세요", "Drag over the area to capture"),
                _ => Tr("영역을 끌어서 고르세요 — 고른 뒤 할 일을 선택하세요", "Drag over an area — then pick what to do"),
            };
            var pick = await Ocr.ScreenGrab.Pick(hint);
            if (pick is not { } p) return;
            LastLens = new LensShot(p.rect, p.bgra, SaveLens(p.rect, p.bgra), DateTime.Now);
            if (kind == "menu")
            {
                OpenView(Mode.Lens);
                return;
            }
            await LensDo(kind);
        }
        catch (Exception e)
        {
            Log.Error("lens", e);
        }
        finally
        {
            ocrBusy = false;
        }
    }

    /// One of the six things to do with the last area.
    public async Task LensDo(string kind)
    {
        if (LastLens is not { } l) return;
        try
        {
            switch (kind)
            {
                case "ask":
                    if (!Window.IsVisible) Summon();
                    Window.ShowChat(Chats.New(), null, null, l.Png);
                    return;
                case "live":
                    Window.Dismiss();
                    new Ocr.TranslateWindow(this, l.Rect, null, true).Show();
                    return;
                case "pin":
                    Window.Dismiss();
                    new Ocr.PinWindow(this, l).Show();
                    return;
                case "capture":
                    Window.Dismiss(); // back to the app it is pasted into
                    Capture(l);
                    return;
            }
            var (px, w, h, _) = Ocr.ScreenGrab.ForOcr(l.Bgra, l.Rect.Width, l.Rect.Height);
            var r = await Ocr.WinOcr.Recognize(px, w, h, OcrLanguage());
            if (r is null || r.Text.Trim().Length == 0)
            {
                Summon();
                if (r is null)
                    Notices.Result(false, "Bloom Lens", Tr("글자 인식 실패", "Text recognition failed"), Tr("설치된 인식 언어를 확인하세요", "Check installed recognition languages"), Tr("인식 언어 설정", "Recognition language settings"), () => OpenSettings("lens", Tr("인식 언어", "Recognition language")));
                else Window.ShowToast(Tr("인식된 글자 없음", "No text found"));
                return;
            }
            if (kind == "translate")
            {
                Window.Dismiss();
                new Ocr.TranslateWindow(this, l.Rect, r.Text, false).Show();
                return;
            }
            LastOcr = r;
            if (Settings.Current.OcrCopy) Launch.Copy(r.Text);
            if (!Window.IsVisible) Summon();
            OpenView(Mode.Ocr);
            Window.ShowToast(Settings.Current.OcrCopy ? Tr($"{r.Lines.Count}줄 인식·복사 완료", $"Read and copied {r.Lines.Count} lines") : Tr($"{r.Lines.Count}줄 인식 완료", $"Read {r.Lines.Count} lines"));
        }
        catch (Exception e)
        {
            Log.Error("lens " + kind, e);
        }
    }

    /// 캡처: the area as a picture on the clipboard (the bitmap + its PNG), told by a corner card.
    private static void Capture(LensShot l)
    {
        var picture = BitmapSource.Create(l.Rect.Width, l.Rect.Height, 96, 96, PixelFormats.Bgra32, null, l.Bgra, l.Rect.Width * 4);
        picture.Freeze();
        bool ok = Launch.CopyImage(picture, l.Png);
        Notices.Result(ok, "Bloom Lens", ok ? Tr("캡처 복사 완료", "Capture copied") : Tr("클립보드에 복사하지 못했습니다. 다시 시도하세요.", "Could not copy to the clipboard. Try again."),
            ok ? Tr($"{l.Rect.Width} × {l.Rect.Height} · Ctrl+V로 붙여넣으세요", $"{l.Rect.Width} × {l.Rect.Height} · paste it with Ctrl+V") : Tr("다른 앱이 클립보드를 쓰고 있습니다 — 다시 시도하세요", "Another app holds the clipboard — try again"));
    }

    /// The picked area as a PNG under Local\lens (the last 30 are kept; a chat that sent one shows it).
    public void SetLensForQa(Ocr.ScreenRect rect, byte[] bgra) => LastLens = new LensShot(rect, bgra, SaveLens(rect, bgra), DateTime.Now);

    private static string SaveLens(Ocr.ScreenRect rect, byte[] bgra)
    {
        string dir = Paths.InLocal("lens");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
        var bmp = BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Bgra32, null, bgra, rect.Width * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(file)) enc.Save(fs);
        foreach (var old in new DirectoryInfo(dir).GetFiles("*.png").OrderByDescending(f => f.Name).Skip(30))
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
            }
        return file;
    }

    private Space.SpaceWindow? spaceWindow;
}
