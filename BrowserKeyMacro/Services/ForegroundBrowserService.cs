using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BrowserKeyMacro.Services;

public sealed class ForegroundMatchResult
{
    public bool IsMatch { get; init; }
    public bool WindowClosed { get; init; }
    public IntPtr ForegroundHandle { get; init; }
    public string? ForegroundProcessName { get; init; }
    public string? ForegroundTitle { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class WindowSnapshot
{
    public IntPtr Handle { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int ProcessId { get; init; }
}

/// <summary>
/// Seçilen hedefe göre genel ön plan kontrolü / WinActivate (AHK benzeri).
/// Tarayıcı adına özel dal yok.
/// </summary>
public sealed class ForegroundBrowserService
{
    private readonly BrowserWindowService _browserWindows;

    public ForegroundBrowserService(BrowserWindowService? browserWindows = null)
    {
        _browserWindows = browserWindows ?? new BrowserWindowService();
    }

    public ForegroundMatchResult Evaluate(BrowserWindowInfo? selected)
    {
        if (selected is null || string.IsNullOrWhiteSpace(selected.ProcessName))
        {
            return new ForegroundMatchResult
            {
                IsMatch = false,
                WindowClosed = true,
                Message = "Seçilen tarayıcı penceresi tanımlı değil."
            };
        }

        var processOpen = _browserWindows.HasVisibleWindowForProcess(selected.ProcessName);
        if (!processOpen)
        {
            MacroDebugLog.Write($"Evaluate KAPALI selectedProc={selected.ProcessName}");
            return new ForegroundMatchResult
            {
                IsMatch = false,
                WindowClosed = true,
                Message = $"Seçilen işlem açık değil: {selected.ProcessName}"
            };
        }

        var foreground = GetForegroundSnapshot();
        if (foreground is null || foreground.Handle == IntPtr.Zero)
        {
            return new ForegroundMatchResult
            {
                IsMatch = false,
                WindowClosed = false,
                Message = "Aktif pencere okunamadı."
            };
        }

        var processMatch = !string.IsNullOrWhiteSpace(foreground.ProcessName)
            && foreground.ProcessName.Equals(selected.ProcessName, StringComparison.OrdinalIgnoreCase);

        MacroDebugLog.Write(
            $"Evaluate selectedProc={selected.ProcessName} selectedHWND=0x{selected.Handle.ToInt64():X} " +
            $"fgProc={foreground.ProcessName} fgHWND=0x{foreground.Handle.ToInt64():X} match={processMatch}");

        return new ForegroundMatchResult
        {
            IsMatch = processMatch,
            WindowClosed = false,
            ForegroundHandle = foreground.Handle,
            ForegroundProcessName = foreground.ProcessName,
            ForegroundTitle = foreground.Title,
            Message = processMatch
                ? $"Seçilen işlem ön planda: {selected.ProcessName}"
                : $"Seçilen işlem ön planda değil (aktif: {foreground.ProcessName ?? "?"}, beklenen: {selected.ProcessName})"
        };
    }

    public WindowSnapshot? GetForegroundSnapshot()
    {
        var foreground = GetForegroundWindow();
        return foreground == IntPtr.Zero ? null : SnapshotFromHandle(foreground);
    }

    public static WindowSnapshot? SnapshotFromHandle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            return null;

        var title = GetWindowTitle(hWnd);
        GetWindowThreadProcessId(hWnd, out var pid);
        var processName = string.Empty;
        if (pid != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch
            {
                processName = string.Empty;
            }
        }

        return new WindowSnapshot
        {
            Handle = hWnd,
            ProcessName = processName,
            Title = title,
            ProcessId = (int)pid
        };
    }

    /// <summary>
    /// AutoHotkey WinActivate benzeri: hedef pencereyi zorla öne getirir.
    /// </summary>
    public bool TryBringToFront(BrowserWindowInfo selected)
    {
        var target = ResolveTarget(selected);
        if (target is null)
            return false;

        try
        {
            AllowSetForegroundWindow(ASFW_ANY);
            ForceForegroundWindow(target.Handle);

            for (var attempt = 0; attempt < 8; attempt++)
            {
                Thread.Sleep(40 + attempt * 20);
                var snap = GetForegroundSnapshot();
                if (snap is not null
                    && snap.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    MacroDebugLog.Write(
                        $"BringToFront OK attempt={attempt} proc={target.ProcessName} hwnd=0x{target.Handle.ToInt64():X}");
                    return true;
                }

                ForceForegroundWindow(target.Handle);
            }

            var last = GetForegroundSnapshot();
            MacroDebugLog.Write(
                $"BringToFront FAIL proc={target.ProcessName} fgProc={last?.ProcessName}");
            return false;
        }
        catch (Exception ex)
        {
            MacroDebugLog.Write($"BringToFront hata: {ex.Message}");
            return false;
        }
    }

    private BrowserWindowInfo? ResolveTarget(BrowserWindowInfo selected)
    {
        if (selected.Handle != IntPtr.Zero && IsWindow(selected.Handle))
            return selected;

        return _browserWindows.RefreshSelectedWindow(selected);
    }

    private static void ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            return;

        if (IsIconic(hWnd))
            ShowWindow(hWnd, SW_RESTORE);
        else
            ShowWindow(hWnd, SW_SHOW);

        ShowWindow(hWnd, SW_SHOWNA);
        BringWindowToTop(hWnd);
        SwitchToThisWindow(hWnd, true);

        var foreground = GetForegroundWindow();
        var targetThread = GetWindowThreadProcessId(hWnd, out _);
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();

        // AHK tarzı: kısa Alt tuşu ile SetForegroundWindow kilidini gevşet
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        var attachedFg = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attachedFg = AttachThreadInput(currentThread, foregroundThread, true);

            if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);

            AllowSetForegroundWindow(ASFW_ANY);
            SetForegroundWindow(hWnd);
            BringWindowToTop(hWnd);
            SetActiveWindow(hWnd);
            SetFocus(hWnd);
        }
        finally
        {
            if (attachedTarget)
                AttachThreadInput(currentThread, targetThread, false);
            if (attachedFg)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return string.Empty;

        var sb = new StringBuilder(length + 1);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private const int SW_SHOWNA = 8;
    private const uint ASFW_ANY = 0xFFFFFFFF;
    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
