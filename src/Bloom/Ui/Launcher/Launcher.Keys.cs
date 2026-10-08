using System;
using System.Linq;
using System.Windows.Input;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Ui;

/// Keyboard: the search box's own keys, the list's, the chat's.
internal sealed partial class Launcher
{
    // ── keyboard ────────────────────────────────────────────────────────────────────────────────
    private void OnBoxKey(object sender, KeyEventArgs e)
    {
        // keys the TextBox would otherwise consume
        if (e.Key == Key.Tab && Mode == Mode.Root && Bloom.Core.Features.On("chat"))
        {
            // Tab: the conversation it picks up (if any); Shift+Tab: always a new one
            bool fresh = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            string q = box.Text.Trim();
            if (q.Length > 0) host.Ask(q, null, fresh);
            else host.OpenChat(fresh ? null : host.TabThread);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Back && box.Text.Length == 0 && Mode == Mode.Form)
        {
            FormBack();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && box.Text.Length == 0 && Mode is not Mode.Root and not Mode.Chat)
        {
            Go(Mode.Root);
            e.Handled = true;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0, shift = (mods & ModifierKeys.Shift) != 0;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (actions.IsOpen)
        {
            if (actions.HandleKey(key)) e.Handled = true;
            return;
        }
        if (key == Key.Escape && HomeEditing)
        {
            e.Handled = true;
            ToggleHomeEdit();
            return;
        }
        if (ctrl && key == Key.E && Mode == Mode.Root && box.Text.Length == 0)
        {
            ToggleHomeEdit();
            e.Handled = true;
            return;
        }
        if (key == Key.Escape)
        {
            e.Handled = true;
            if (Mode == Mode.Chat && thread?.Busy == true) host.Chats.Stop(thread);
            else if (Mode == Mode.Form) Go(form?.Back ?? Mode.Root);
            else if (Mode != Mode.Root) Go(Mode.Root);
            else if (box.Text.Length > 0) box.Clear();
            else Dismiss();
            return;
        }
        if (ctrl && key == Key.K)
        {
            OpenActions();
            e.Handled = true;
            return;
        }
        if (ctrl && key == Key.OemComma)
        {
            host.OpenSettings();
            e.Handled = true;
            return;
        }
        // 📌, the 도구 menu, and Bloom's size (Ctrl+= bigger, Ctrl+- smaller, Ctrl+0 back to 100 %)
        if (ctrl && key is Key.P or Key.J or Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract or Key.D0 or Key.NumPad0)
        {
            if (key == Key.P) SetPinned(!Pinned);
            else if (key == Key.J) OpenTools();
            else if (key is Key.OemPlus or Key.Add) Theme.StepScale(1);
            else if (key is Key.OemMinus or Key.Subtract) Theme.StepScale(-1);
            else Theme.SetScale(1);
            e.Handled = true;
            return;
        }
        if (Mode == Mode.Chat)
        {
            ChatKey(key, ctrl, shift, e);
            return;
        }
        if (Mode == Mode.Form)
        {
            if (key == Key.Enter)
            {
                FormNext();
                e.Handled = true;
            }
            return;
        }
        // Bloom Lens: 1–4 pick the action right away
        if (Mode == Mode.Lens && box.Text.Length == 0 && !ctrl && key is >= Key.D1 and <= Key.D4 && rows.Count >= key - Key.D1 + 1)
        {
            Select(key - Key.D1, false);
            RunSelected(null);
            e.Handled = true;
            return;
        }
        if (Mode == Mode.Files && ctrl && key == Key.Tab)
        {
            StepFileKind(shift ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (Mode == Mode.Calendar && box.Text.Length == 0 && !shift)
        {
            DateTime? to = key switch
            {
                Key.Left => calDay.AddDays(ctrl ? -7 : -1),
                Key.Right => calDay.AddDays(ctrl ? 7 : 1),
                Key.PageUp => calDay.AddMonths(-1),
                Key.PageDown => calDay.AddMonths(1),
                Key.Home => DateTime.Today,
                _ => null,
            };
            if (to is { } day)
            {
                CalendarGo(day);
                e.Handled = true;
                return;
            }
        }
        switch (key)
        {
            case Key.Left when inStrip && ctrl && shift:
                if (Selected is { } fl) MoveFavorite(fl, -1);
                e.Handled = true;
                break;
            case Key.Right when inStrip && ctrl && shift:
                if (Selected is { } fr) MoveFavorite(fr, 1);
                e.Handled = true;
                break;
            case Key.Left when inStrip:
                SelectStrip(stripSel - 1);
                e.Handled = true;
                break;
            case Key.Right when inStrip:
                SelectStrip(stripSel + 1);
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D9 when ctrl && !shift && Mode == Mode.Root:
                RunFavorite(key - Key.D1);
                e.Handled = true;
                break;
            case Key.Down when ctrl && shift:
                if (Selected is { } d) MoveFavorite(d, 1);
                e.Handled = true;
                break;
            case Key.Up when ctrl && shift:
                if (Selected is { } u) MoveFavorite(u, -1);
                e.Handled = true;
                break;
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                Move(8);
                e.Handled = true;
                break;
            case Key.PageUp:
                Move(-8);
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelected(ctrl && shift ? "Ctrl+Shift+Enter" : ctrl ? "Ctrl+Enter" : null);
                e.Handled = true;
                break;
            case Key.D when ctrl:
                if (Selected is { Pinnable: true } f) ToggleFavorite(f);
                else RunSelected("Ctrl+D"); // clipboard: pin
                e.Handled = true;
                break;
            case Key.Delete when ctrl:
                RunSelected("Ctrl+Delete");
                e.Handled = true;
                break;
            case Key.S when ctrl:
                RunSelected("Ctrl+S");
                e.Handled = true;
                break;
            case Key.W when ctrl:
                RunSelected("Ctrl+W"); // 창 전환: close the window
                e.Handled = true;
                break;
            case Key.C when ctrl && shift:
                RunSelected("Ctrl+Shift+C");
                e.Handled = true;
                break;
            case Key.C when ctrl && box.SelectionLength == 0 && Selected is { } c:
                if (c.Kind == ItemKind.Calc) c.Run?.Invoke();
                else
                {
                    var act = ActionsFor(c).FirstOrDefault(a => a.Keys == "Ctrl+Shift+C");
                    act?.Run();
                }
                if (c.Kind == ItemKind.Calc || ActionsFor(c).Any(a => a.Keys == "Ctrl+Shift+C")) ShowToast(Tr("복사했어요", "Copied"));
                e.Handled = true;
                break;
        }
    }

    private void ChatKey(Key key, bool ctrl, bool shift, KeyEventArgs e)
    {
        switch (key)
        {
            case Key.Enter when !shift:
                SendChat();
                e.Handled = true;
                break;
            case Key.N when ctrl:
                ShowChat(host.Chats.New());
                e.Handled = true;
                break;
            case Key.O when ctrl && shift:
                OpenInWindow();
                e.Handled = true;
                break;
            case Key.O when ctrl:
                PickFiles();
                e.Handled = true;
                break;
            case Key.V when ctrl && !shift:
                if (PasteAttachment()) e.Handled = true;
                break;
            case Key.L when ctrl:
                Go(Mode.Threads);
                e.Handled = true;
                break;
            case Key.M when ctrl && shift:
                SwitchProvider();
                e.Handled = true;
                break;
            case Key.M when ctrl:
                PickModel();
                e.Handled = true;
                break;
            case Key.E when ctrl:
                CycleEffort();
                e.Handled = true;
                break;
            case Key.C when ctrl && shift:
                if (thread?.Messages.LastOrDefault(m => m.Role == "assistant" && m.Text.Length > 0) is { } last)
                {
                    Launch.Copy(last.Text);
                    ShowToast(Tr("답변을 복사했어요", "Answer copied"));
                }
                e.Handled = true;
                break;
            case Key.Up when box.Text.Length == 0:
            case Key.PageUp:
                chat.ScrollBy(-120);
                e.Handled = true;
                break;
            case Key.Down when box.Text.Length == 0:
            case Key.PageDown:
                chat.ScrollBy(120);
                e.Handled = true;
                break;
        }
    }
}
