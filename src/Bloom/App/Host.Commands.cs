using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App;

/// The launcher's own commands (cmd:…).
internal sealed partial class Host
{
    // ── commands ────────────────────────────────────────────────────────────────────────────────
    public void RunCommand(string id)
    {
        switch (id)
        {
            case "cmd:ai":
                OpenChatWindow();
                break;
            case "cmd:threads":
                OpenView(Mode.Threads);
                break;
            case "cmd:files":
                OpenView(Mode.Files);
                break;
            case "cmd:clipboard":
                OpenView(Mode.Clipboard);
                break;
            case "cmd:calendar":
                OpenView(Mode.Calendar);
                break;
            case "cmd:quicklinks":
                OpenView(Mode.Quicklinks);
                break;
            case "cmd:commands":
                OpenView(Mode.Commands);
                break;
            case "cmd:processes":
                OpenProcesses();
                break;
            case "cmd:space":
                OpenSpace(null);
                break;
            case "cmd:notes":
                Tools.Open("notes");
                break;
            case "cmd:tools":
                Window.OpenTools();
                break;
            case "cmd:lens":
                StartLens("menu");
                break;
            case "cmd:windows":
                OpenWindows.Invalidate();
                OpenView(Mode.Windows);
                break;
            case "cmd:emoji":
                OpenView(Mode.Emoji);
                break;
            case "cmd:recent":
                OpenView(Mode.Recent);
                break;
            case "cmd:system":
                OpenView(Mode.System);
                break;
            case "cmd:quick":
                StartQuick(false);
                break;
            case "cmd:timer":
                OpenView(Mode.Timers);
                break;
            case "cmd:color":
                if (ColorBook.Now is null) StartColor();
                else OpenView(Mode.Color);
                break;
            case "cmd:export":
                Window.Dismiss();
                ExportSettings(null);
                break;
            case "cmd:import":
                Window.Dismiss();
                ImportSettings(null);
                break;
            case "cmd:welcome":
                Window.Dismiss();
                ShowOnboarding();
                break;
            case "cmd:settings":
                OpenSettings();
                break;
            case "cmd:reindex":
                Apps.Rescan();
                Window.ShowToast(Tr("앱 목록을 다시 읽는 중…", "Reading the app list again…"));
                break;
            case "cmd:lock":
                Window.Dismiss();
                Win32.LockWorkStation();
                break;
            case "cmd:restart":
            case "cmd:shutdown":
                // asked once more, as the Start menu's power button is two clicks away
                bool restart = id == "cmd:restart";
                Window.Confirm(restart ? Tr("PC를 다시 시작할까요?", "Restart the PC?") : Tr("PC를 끌까요?", "Shut down the PC?"), new ActionItem
                {
                    Title = restart ? Tr("다시 시작", "Restart") : Tr("시스템 종료", "Shut down"),
                    Keys = "Enter",
                    Glyph = restart ? G.Restart : G.Power,
                    Danger = true,
                    Run = () => Power(restart),
                });
                break;
            case "cmd:quit":
                QuitForGood();
                break;
        }
    }

    private void Power(bool restart)
    {
        Log.Write(restart ? "power: restart" : "power: shut down");
        SaveAll();
        if (Win32.ExitWindows(restart)) return;
        Log.Write("power: refused");
        Summon();
        Window.ShowToast(restart ? Tr("다시 시작하지 못했어요", "Couldn't restart") : Tr("시스템을 종료하지 못했어요", "Couldn't shut down"));
    }
}
