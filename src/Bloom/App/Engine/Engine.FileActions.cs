using System;
using System.Collections.Generic;
using System.IO;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App;

/// What Ctrl+K does to a file or folder besides opening it: a real rename, a move or copy to another folder, the
/// Recycle Bin — each through Explorer's own file operation (its progress, its name-clash questions).
internal sealed partial class Engine
{
    private const char RenameGlyph = '', MoveGlyph = '';

    private IEnumerable<ActionItem> FileActions(string path, string name, bool folder)
    {
        yield return new ActionItem { Title = Tr("이름 바꾸기…", "Rename…"), Glyph = RenameGlyph, StayOpen = true, Run = () => host.Window.StartForm(RenameForm(path, name, folder)) };
        yield return new ActionItem { Title = Tr("다른 폴더로 옮기기…", "Move to another folder…"), Glyph = MoveGlyph, Run = () => host.MoveOrCopy(path, true) };
        yield return new ActionItem { Title = Tr("다른 폴더에 복사…", "Copy to another folder…"), Glyph = G.Copy, Run = () => host.MoveOrCopy(path, false) };
        yield return new ActionItem
        {
            Title = Tr("휴지통으로 보내기", "Move to the Recycle Bin"),
            Glyph = G.Delete,
            Danger = true,
            StayOpen = true,
            Run = () => host.Window.Confirm(Tr($"‘{name}’을(를) 휴지통으로 보낼까요?", $"Move ‘{name}’ to the Recycle Bin?"), new ActionItem
            {
                Title = Tr("휴지통으로", "Move to bin"),
                Keys = "Enter",
                Glyph = G.Delete,
                Danger = true,
                StayOpen = true,
                Run = () =>
                {
                    bool ok = host.Recycle(path);
                    host.Window.Refresh(false);
                    host.Window.ShowToast(ok ? Tr($"‘{name}’ 휴지통으로 보냈어요 — 휴지통에서 되돌릴 수 있어요", $"Moved ‘{name}’ to the Recycle Bin — you can restore it from there")
                        : Tr("휴지통으로 보내지 못했어요 (사용 중이거나 권한이 없어요)", "Couldn't move it to the Recycle Bin (in use, or no permission)"));
                },
            }),
        };
    }

    /// The file's (or folder's) real name in its folder; afterwards the view searches for the new name.
    public FormSpec RenameForm(string path, string name, bool folder)
    {
        var back = host.Window.Mode == Mode.Form ? Mode.Root : host.Window.Mode;
        string dir = Path.GetDirectoryName(path.TrimEnd('\\')) ?? "";
        var f = new FormSpec { Title = folder ? Tr("폴더 이름 바꾸기", "Rename folder") : Tr("파일 이름 바꾸기", "Rename file"), Back = back };
        f.Fields.Add(new FormField
        {
            Label = Tr("새 이름", "New name"),
            Hint = folder ? Tr("새 폴더 이름", "The folder's new name") : Tr("새 파일 이름 — 확장자까지 적은 그대로 바뀌어요", "The file's new name — the extension changes as typed too"),
            Value = name,
            Check = v =>
            {
                string n = v.Trim();
                if (n.Length == 0) return (null, Tr("이름을 넣어 주세요", "Please enter a name"));
                if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return (null, Tr("이름에 \\ / : * ? \" < > | 는 쓸 수 없어요", "A name can't have \\ / : * ? \" < > |"));
                if (n == name) return (null, Tr("이름이 그대로예요 — 바꾸지 않으려면 Esc", "That's the same name — Esc to leave it"));
                string to = Path.Combine(dir, n);
                if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase) && (File.Exists(to) || Directory.Exists(to)))
                    return (null, Tr($"같은 폴더에 ‘{n}’이(가) 이미 있어요", $"‘{n}’ is already in that folder"));
                return (n, null);
            },
        });
        f.Done = form =>
        {
            string n = form[0];
            bool ok = ShellFiles.Rename(path, n, IntPtr.Zero);
            host.Window.Go(back, ok ? Path.GetFileNameWithoutExtension(n) : "");
            host.Window.ShowToast(ok ? Tr($"‘{name}’ → ‘{n}’", $"‘{name}’ → ‘{n}’") : Tr("이름을 바꾸지 못했어요 (사용 중이거나 권한이 없어요)", "Couldn't rename it (in use, or no permission)"));
        };
        return f;
    }
}
