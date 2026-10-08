using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App;

/// Running 내 명령 — a script hidden (its result as a notice card) or in a console window, a snippet pasted into the
/// window in front, a chain's lines one after another — and the file actions of a search result (rename, move, copy,
/// recycle) the way Explorer does them.
internal sealed partial class Host
{
    private readonly Dictionary<string, Process> runningScripts = new();
    private readonly Dictionary<string, string> lastOutput = new();

    public bool IsRunning(UserCommand c) => runningScripts.ContainsKey(c.Id);

    public string? LastOutput(UserCommand c) => lastOutput.GetValueOrDefault(c.Id);

    /// {clipboard} in a snippet or a script line.
    public string ClipText()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return "";
        }
    }

    /// `show` / `admin`: this once in a console window (as administrator); `depth`: how deep in chains this run is.
    public void RunMine(UserCommand c, string input, bool? show = null, bool admin = false, int depth = 0)
    {
        switch (c.Kind)
        {
            case "snippet":
                _ = Clipboard.PasteText(UserCommands.Expand(UserCommands.SnippetText(c.Body), input, ClipText));
                break;
            case "chain":
                _ = RunChain(c, input, depth);
                break;
            default:
                RunScript(c, input, show ?? c.ShowWindow, admin);
                break;
        }
    }

    public void StopMine(UserCommand c)
    {
        if (!runningScripts.TryGetValue(c.Id, out var p)) return;
        try
        {
            p.Kill(true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            /* it ended on its own */
        }
    }

    private static string Head(bool ok) => ok ? Tr("내 명령 · 완료", "My command · Done") : Tr("내 명령 · 오류", "My command · Error");

    private void RunScript(UserCommand c, string input, bool show, bool admin)
    {
        if (!show && !admin && runningScripts.ContainsKey(c.Id))
        {
            Window.ShowToast(Tr($"‘{c.Name}’ 은(는) 아직 실행 중이에요", $"‘{c.Name}’ is still running"));
            return;
        }
        var psi = UserCommands.StartInfo(UserCommands.ScriptLine(c.Body, input, ClipText), show || admin);
        if (admin) psi.Verb = "runas";
        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return; // the administrator question was answered no
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Error("my command " + c.Name, e);
            Notices.Result(false, Head(false), c.Name, Tr("실행하지 못했어요 — ", "Couldn't start it — ") + e.Message);
            return;
        }
        if (p is null || show || admin) return;
        runningScripts[c.Id] = p;
        Later();
        var clock = Stopwatch.StartNew();
        _ = Task.Run(async () =>
        {
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            string output = await outTask, error = await errTask;
            int code = p.ExitCode;
            p.Dispose();
            await ui.InvokeAsync(() =>
            {
                runningScripts.Remove(c.Id);
                lastOutput[c.Id] = (output.TrimEnd() + (error.Trim().Length > 0 ? "\n" + error.Trim() : "")).Trim();
                bool ok = code == 0;
                string took = Tr($"{Notices.Span(clock.Elapsed)} 걸렸어요", $"Took {Notices.Span(clock.Elapsed)}");
                string? said = UserCommands.LastLine(ok ? output : error.Trim().Length > 0 ? error : output);
                Notices.Result(ok, Head(ok), c.Name, ok ? said ?? took : Tr($"코드 {code}", $"Code {code}") + (said is null ? "" : " · " + said));
                Later();
            });
        });
    }

    /// A chain: each line as if typed in the search line and Enter pressed on its first answer; "기다리기 2초" waits.
    /// Lines that find nothing are skipped, and a notice says which.
    private async Task RunChain(UserCommand c, string input, int depth)
    {
        if (depth > 2)
        {
            Notices.Result(false, Head(false), c.Name, Tr("묶음 안의 묶음은 세 겹까지만 돼요", "Chains go at most three deep"));
            return;
        }
        await Task.Delay(150); // the launcher is out of the way first
        var skipped = new List<string>();
        foreach (string line in UserCommands.Steps(c.Body))
        {
            string step = UserCommands.Expand(line, input, ClipText);
            if (UserCommands.Wait(step) is { } wait)
            {
                await Task.Delay(wait);
                continue;
            }
            if (Engine.StepAction(step, depth) is { } run)
            {
                try
                {
                    run();
                }
                catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
                {
                    Log.Error("chain step " + step, e);
                    skipped.Add(step);
                }
                await Task.Delay(350); // an app or a page gets a moment to open before the next line
            }
            else skipped.Add(step);
        }
        if (skipped.Count > 0) Notices.Result(false, Head(false), c.Name, Tr($"건너뛴 단계: {string.Join(", ", skipped)}", $"Skipped: {string.Join(", ", skipped)}"));
    }

    // ── file actions (Ctrl+K on a file or folder) ─────────────────────────────────────────────
    /// 다른 폴더로 옮기기 / 복사: the Windows folder picker, then Explorer's own move or copy.
    public void MoveOrCopy(string path, bool move)
    {
        Window.Dismiss();
        var pick = new Microsoft.Win32.OpenFolderDialog
        {
            Title = move ? Tr("어느 폴더로 옮길까요?", "Move to which folder?") : Tr("어느 폴더에 복사할까요?", "Copy to which folder?"),
            InitialDirectory = Path.GetDirectoryName(path.TrimEnd('\\')) ?? "",
        };
        if (pick.ShowDialog() != true) return;
        string name = Path.GetFileName(path.TrimEnd('\\'));
        bool ok = move ? ShellFiles.MoveTo(path, pick.FolderName, IntPtr.Zero) : ShellFiles.CopyTo(path, pick.FolderName, IntPtr.Zero);
        if (ok) Notices.Result(true, move ? Tr("파일 · 옮김", "File · Moved") : Tr("파일 · 복사함", "File · Copied"), name, pick.FolderName);
        else if (File.Exists(path) || Directory.Exists(path)) Notices.Result(false, Tr("파일 · 그대로", "File · Unchanged"), name, move ? Tr("옮기지 못했거나 취소했어요", "Not moved, or cancelled") : Tr("복사하지 못했거나 취소했어요", "Not copied, or cancelled"));
    }

    /// 휴지통으로 보내기 (asked first in the launcher).
    public bool Recycle(string path) => ShellFiles.Recycle(path, IntPtr.Zero);
}
