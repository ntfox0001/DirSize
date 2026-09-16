using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using DirSize.Mvvm;

namespace DirSize.Services;

/// <summary>
/// 调用本机豆包桌面客户端（UI 自动化）：
/// 1) 检测豆包进程/窗口；没有则尝试从开始菜单/桌面快捷方式自动打开；打不开则提示手动打开。
/// 2) 窗口在则聚焦，并【先定位真实输入框】再把文字送入：
///    优先 UIA 定位输入控件（支持 ValuePattern 则直接填入）；
///    否则点击窗口底部输入区聚焦 → 剪贴板粘贴(Ctrl+V，带重试)；
///    最后兜底 SendInput 逐字输入。全程不依赖单个易失败环节。
/// </summary>
public static class DoubaoHelper
{
    private static readonly string[] NameTokens = { "doubao", "豆包" };

    #region Win32 P/Invoke
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtra);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private const int SW_RESTORE = 9;
    private const uint KEYEVENTF_KEYUP = 0x02;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint CF_UNICODETEXT = 13;
    #endregion

    /// <summary>把 prompt 输入豆包窗口并发送。返回用户可见状态（已本地化）。</summary>
    public static string SendToDoubao(string prompt)
    {
        var hwnd = EnsureWindow();
        if (hwnd == IntPtr.Zero)
            return Locale.T(
                "未能自动打开豆包，请手动打开豆包后，再点一次“发豆包”。",
                "Could not launch Doubao automatically. Please open Doubao manually, then click “Ask Doubao” again.");

        FocusWindow(hwnd);

        // 1) UIA 定位输入控件并尽量直接填入
        if (TryFillViaUia(hwnd, prompt))
        {
            SendEnter();
            return 结果(true, false);
        }

        // 2) 让输入框获得焦点：UIA 聚焦 或 点击窗口底部输入区
        FocusInputArea(hwnd);

        // 3) 剪贴板粘贴（带重试；对 Electron 类输入框最可靠）
        if (TrySetClipboard(prompt))
        {
            SendCtrlV();
            SendEnter();
            return 结果(true, false);
        }

        // 4) 兜底：SendInput 逐字输入
        TypeText(prompt);
        SendEnter();
        return 结果(true, false);
    }

    private static string 结果(bool ok, bool manual)
        => Locale.T("已把目录信息发送给豆包，请到豆包窗口查看回复。",
                    "Sent the folder info to Doubao — check the Doubao window for the reply.");

    private static IntPtr EnsureWindow()
    {
        var hwnd = FindWindow();
        if (hwnd != IntPtr.Zero) return hwnd;

        if (TryLaunch())
            for (int i = 0; i < 24 && (hwnd = FindWindow()) == IntPtr.Zero; i++)
                Thread.Sleep(500);
        return hwnd;
    }

    private static IntPtr FindWindow()
    {
        foreach (var p in Process.GetProcesses())
            if (IsDoubaoName(p.ProcessName) && p.MainWindowHandle != IntPtr.Zero)
                return p.MainWindowHandle;
        foreach (var p in Process.GetProcesses())
            if (IsDoubaoName(p.ProcessName) && HasDoubaoTitle(p))
                return new IntPtr(p.MainWindowHandle);
        return IntPtr.Zero;
    }

    private static bool IsDoubaoName(string name)
        => !string.IsNullOrWhiteSpace(name)
           && NameTokens.Any(t => name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);

    private static bool HasDoubaoTitle(Process p)
    {
        try { return NameTokens.Any(t => p.MainWindowTitle.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0); }
        catch { return false; }
    }

    private static void FocusWindow(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        Thread.Sleep(300);
        SetForegroundWindow(hwnd);
    }

    private static bool TryLaunch()
    {
        string[] dirs;
        try
        {
            dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            }.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct().ToArray();
        }
        catch { return false; }

        foreach (var dir in dirs)
        {
            IEnumerable<string> links;
            try { links = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var lnk in links)
            {
                if (!NameTokens.Any(t => Path.GetFileNameWithoutExtension(lnk).IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                try { Process.Start(new ProcessStartInfo { FileName = lnk, UseShellExecute = true }); return true; }
                catch { }
            }
        }
        return false;
    }

    /// <summary>UIA 定位输入控件；支持 ValuePattern 直接填入并返回 true。</summary>
    private static bool TryFillViaUia(IntPtr hwnd, string text)
    {
        AutomationElement root;
        try { root = AutomationElement.FromHandle(hwnd); }
        catch { return false; }

        // Chromium/网页内核里输入框多为 Edit 或 Document 控件类型
        var cond = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));

        AutomationElement input;
        try
        {
            input = root.FindFirst(TreeScope.Descendants, cond);
            if (input == null) return false;
            if (!input.Current.IsEnabled) return false;
        }
        catch { return false; }

        try
        {
            if (input.TryGetCurrentPattern(ValuePattern.Pattern, out var p) && p is ValuePattern vp)
            {
                vp.SetValue(text);
                return true;
            }
        }
        catch { /* 有些控件不支持设值 */ }

        return false;
    }

    /// <summary>让输入框获得焦点：优先 UIA SetFocus，否则点击窗口底部输入区。</summary>
    private static void FocusInputArea(IntPtr hwnd)
    {
        try
        {
            var el = AutomationElement.FromHandle(hwnd);
            var cond = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            var input = el.FindFirst(TreeScope.Descendants, cond);
            if (input != null && input.Current.IsEnabled) { try { input.SetFocus(); Thread.Sleep(200); } catch { } }
        }
        catch { }

        if (GetWindowRect(hwnd, out var r))
        {
            int x = r.Left + (r.Right - r.Left) / 2;
            int y = r.Top + Math.Max((r.Bottom - r.Top) - 40, r.Top + (r.Bottom - r.Top) * 3 / 5);
            try
            {
                SetCursorPos(x, y);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(250);
            }
            catch { }
        }
    }

    /// <summary>带重试地把文本设到剪贴板，避免 OpenClipboard 竞争（0x800401D0）。</summary>
    private static bool TrySetClipboard(string s)
    {
        for (int i = 0; i < 6; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var hMem = Marshal.StringToHGlobalUni(s);
                    if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
                    { Marshal.FreeHGlobal(hMem); return false; }
                    return true;
                }
                catch { return false; }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(120);
        }
        return false;
    }

    private static void SendCtrlV()
    {
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        keybd_event(0x56, 0, 0, UIntPtr.Zero);
        keybd_event(0x56, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SendEnter()
    {
        keybd_event(0x0D, 0, 0, UIntPtr.Zero);
        keybd_event(0x0D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>SendInput UNICODE 逐字输入（不经过剪贴板），最终兜底。BMP 内字符（含中文）均可。</summary>
    private static void TypeText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var ch in text)
        {
            if (char.IsHighSurrogate(ch)) continue;
            if (ch == '\n') { inputs.Add(Key(VK_RETURN, false)); inputs.Add(Key(VK_RETURN, true)); continue; }
            inputs.Add(Key(ch, false));
            inputs.Add(Key(ch, true));
        }
        if (inputs.Count > 0) SendInputSafe(inputs.ToArray());
    }

    private static void SendInputSafe(INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    private static INPUT Key(char ch, bool keyUp)
    {
        uint flags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0);
        return new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = flags } } };
    }

    private static INPUT Key(ushort k, bool keyUp)
    {
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
        return new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = k, dwFlags = flags } } };
    }

    private const ushort VK_RETURN = 0x0D;
    private const uint KEYEVENTF_UNICODE = 0x04;
    private const uint INPUT_KEYBOARD = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public INPUTUNION u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }
}