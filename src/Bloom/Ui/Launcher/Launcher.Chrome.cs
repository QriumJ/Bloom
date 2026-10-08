using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Ui;

/// The search line's lead, chip and hints, and the footer, for each view.
internal sealed partial class Launcher
{
    private void UpdateChrome()
    {
        string ph;
        string? chipLabel = null;
        FrameworkElement leadEl;
        hint.Children.Clear();
        footRight.Children.Clear();
        switch (Mode)
        {
            case Mode.Chat:
                leadEl = Theme.Sparkle(17, Theme.P.Accent);
                chipLabel = attachedImage is not null ? Tr("화면 · Bloom Lens", "Screen · Bloom Lens") : attachedFiles.Count > 0 ? Tr($"첨부 {attachedFiles.Count}개", $"{attachedFiles.Count} attached") : attachment is { } a ? (a.url.Length > 0 ? Tr("페이지 · ", "Page · ") : "") + ClipText(a.title, 16) : null;
                bool busy = thread?.Busy == true;
                ph = attachedImage is not null ? Tr("이 화면에 대해 물어보세요 (비우고 Enter = 설명해 줘)", "Ask about this screen (empty + Enter = explain it)")
                    : attachedFiles.Count > 0 ? Tr("첨부한 파일에 대해 물어보세요", "Ask about the attached files")
                    : thread is { Messages.Count: > 0 } ? Tr("이어서 묻기…", "Ask a follow-up…") : Tr("무엇이든 물어보세요…", "Ask anything…");
                // [Claude | ChatGPT] switches the side, [model ▾] opens the menu, [level] steps through
                string curModel = CurrentModel;
                hint.Children.Add(HeadChip(Ai.AiModels.IsChatGpt(curModel) ? "ChatGPT" : "Claude", SwitchProvider));
                var modelChip = HeadChip(Engine.ModelName(curModel) + "  ▾", PickModel);
                modelChip.Margin = new Thickness(6, 0, 0, 0);
                hint.Children.Add(modelChip);
                if (Ai.AiModels.HasEffort(curModel))
                {
                    var effortChip = HeadChip(Ai.AiModels.EffortName(thread is null ? Ai.AiModels.DefaultEffort(curModel) : Chats.EffortOf(thread)), CycleEffort);
                    effortChip.Margin = new Thickness(6, 0, 0, 0);
                    hint.Children.Add(effortChip);
                }
                if (thread is { Messages.Count: > 0 })
                {
                    var freshChip = HeadChip(Tr("새 대화", "New chat"), () => ShowChat(host.Chats.New()));
                    freshChip.Margin = new Thickness(12, 0, 0, 0);
                    hint.Children.Add(freshChip);
                }
                if (thread is not null && host.Chats.IsLive(thread))
                {
                    var t = thread;
                    var endChip = HeadChip(Tr("세션 종료", "End session"), () =>
                    {
                        host.Chats.EndSession(t);
                        ShowToast(Tr("세션을 닫았어요 — 다음 메시지에서 이어서 시작해요", "Session closed — the next message picks it up again"));
                    });
                    endChip.Margin = new Thickness(6, 0, 0, 0);
                    endChip.ToolTip = Tr("이 대화의 CLI를 닫아 메모리를 돌려줘요 · 대화는 남아요", "Closes this chat's CLI to free its memory · the chat stays");
                    hint.Children.Add(endChip);
                }
                Foot(busy ? Tr("중지", "Stop") : Tr("보내기", "Send"), busy ? "Esc" : "Enter");
                Divider();
                Foot(Tr("첨부", "Attach"), "Ctrl+O");
                Divider();
                Foot(Tr("새 대화", "New chat"), "Ctrl+N");
                Divider();
                Foot(Tr("더보기", "More"), "Ctrl+K");
                footContext.Text = thread is { Messages.Count: > 0 } ? ClipText(thread.Title, 34) : Tr("AI 채팅", "AI chat");
                work.Working = busy;
                break;
            case Mode.Form:
                leadEl = Back();
                chipLabel = form?.Title;
                ph = form?.Current.Hint ?? "";
                bool last = form is { } ff && ff.Step == ff.Fields.Count - 1;
                Foot(last ? Tr("저장", "Save") : Tr("다음", "Next"), "Enter");
                Divider();
                Foot(form is { Step: > 0 } ? Tr("이전", "Back") : Tr("취소", "Cancel"), form is { Step: > 0 } ? "Backspace" : "Esc");
                footContext.Text = form is { } fs ? $"{fs.Title} · {fs.Current.Label}" : "";
                break;
            default:
                leadEl = Mode == Mode.Root ? Theme.Glyph(G.Search, 17, Theme.P.TextFaint) : Back();
                var spec = View(Mode);
                chipLabel = spec.Chip();
                ph = spec.Placeholder();
                if (Mode == Mode.Files)
                    hint.Children.Add(HeadChip(Engine.FileKind(fileKind).name + "  ▾", PickFileKind));
                var item = Selected;
                if (item?.Run is not null && item.PrimaryLabel.Length > 0)
                {
                    Foot(item.PrimaryLabel, "Enter");
                    Divider();
                }
                Foot(Tr("더보기", "More"), "Ctrl+K");
                if (Mode == Mode.Root && box.Text.Length == 0)
                {
                    Divider();
                    footRight.Children.Add(HomeEditButton());
                }
                footContext.Text = spec.Context();
                break;
        }
        lead.Content = leadEl;
        placeholder.Text = ph;
        placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        chip.Visibility = chipLabel is null ? Visibility.Collapsed : Visibility.Visible;
        chipText.Text = chipLabel ?? "";
    }

    /// A small clickable chip at the right of the search line (the chat's model and level).
    private static Border HeadChip(string label, Action click)
    {
        var chip = new SmoothBorder
        {
            Child = Theme.T(label, 12, Theme.P.TextDim, Theme.Medium, false),
            Background = Theme.P.Tile,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 3),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return chip;
    }

    private FrameworkElement Back()
    {
        var g = Theme.Glyph('', 14, Theme.P.TextDim);
        var b = new SmoothBorder { Child = g, Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            Go(Mode.Root);
            e.Handled = true;
        };
        return b;
    }

    private static string ClipText(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    private void Foot(string label, string keys)
    {
        footRight.Children.Add(Keys.Caps(keys));
        var t = Theme.T(label, 12, Theme.P.TextDim, Theme.Medium, false);
        t.Margin = new Thickness(6, 0, 0, 0);
        footRight.Children.Add(t);
    }

    /// The home's layout button (footer, right end): 홈 편집 ↔ 편집 끝 (Ctrl+E).
    private FrameworkElement HomeEditButton()
    {
        bool on = HomeEditing;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Theme.Glyph(on ? G.Check : '\uE70F', 11, on ? Theme.P.AccentInk : Theme.P.TextDim);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.Margin = new Thickness(0, 1, 5, 0);
        sp.Children.Add(g);
        sp.Children.Add(Theme.T(on ? Tr("편집 끝", "Done") : Tr("홈 편집", "Edit home"), 12, on ? Theme.P.AccentInk : Theme.P.TextDim, Theme.Medium, false));
        var b = new SmoothBorder
        {
            Child = sp,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 9, 4),
            Background = on ? Theme.P.Accent : Theme.P.Key,
            Cursor = Cursors.Hand,
            ToolTip = on ? Tr("편집 끝 (Esc · Ctrl+E)", "Done (Esc · Ctrl+E)") : Tr("홈 화면 블록 순서 · 숨기기 (Ctrl+E)", "Order or hide the home blocks (Ctrl+E)"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!on)
        {
            b.MouseEnter += (_, _) => b.Background = Theme.P.Selected;
            b.MouseLeave += (_, _) => b.Background = Theme.P.Key;
        }
        b.MouseLeftButtonUp += (_, e) =>
        {
            ToggleHomeEdit();
            e.Handled = true;
        };
        return b;
    }

    private void Divider() => footRight.Children.Add(new System.Windows.Shapes.Ellipse { Width = 3, Height = 3, Fill = Theme.P.TextFaint, Opacity = 0.7, Margin = new Thickness(11, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center });
}
