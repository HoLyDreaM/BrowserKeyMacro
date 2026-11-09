using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BrowserKeyMacro.Services;

public sealed class ForegroundMatchResult
{
    /// <summary>Aynı process ön planda (gönderim canContinue için gevşek).</summary>
    public bool IsMatch { get; init; }
    /// <summary>Seçilen HWND birebir ön planda (yanlış Edge penceresi = false).</summary>
    public bool IsHwndMatch { get; init; }
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

        var hwndMatch = selected.Handle != IntPtr.Zero
            && foreground.Handle == selected.Handle
            && IsWindow(selected.Handle);

        var processMatch = !string.IsNullOrWhiteSpace(foreground.ProcessName)
            && foreground.ProcessName.Equals(selected.ProcessName, StringComparison.OrdinalIgnoreCase);

        // canContinue: process yeterli; öne alma kararı HWND ile (MainForm IsHwndMatch)
        var isMatch = processMatch;

        MacroDebugLog.Write(
            $"Evaluate selectedProc={selected.ProcessName} selectedHWND=0x{selected.Handle.ToInt64():X} " +
            $"fgProc={foreground.ProcessName} fgHWND=0x{foreground.Handle.ToInt64():X} " +
            $"hwndMatch={hwndMatch} processMatch={processMatch} match={isMatch}");

        return new ForegroundMatchResult
        {
            IsMatch = isMatch,
            IsHwndMatch = hwndMatch,
            WindowClosed = false,
            ForegroundHandle = foreground.Handle,
            ForegroundProcessName = foreground.ProcessName,
            ForegroundTitle = foreground.Title,
            Message = hwndMatch
                ? $"Seçilen pencere ön planda: {selected.ProcessName}"
                : processMatch
                    ? $"Aynı işlem başka pencerede (aktif HWND=0x{foreground.Handle.ToInt64():X})"
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
    /// Hedef zaten ön plandaysa Alt/ForceForeground no-op (oyunda Alt enjeksiyonu yok).
    /// </summary>
    public bool TryBringToFront(BrowserWindowInfo selected)
    {
        var target = ResolveTarget(selected);
        if (target is null)
            return false;

        try
        {
            var already = GetForegroundSnapshot();
            // Yalnız seçilen HWND öndeyse no-op — aynı process başka Edge yetmez
            if (already is not null && already.Handle == target.Handle)
            {
                MacroDebugLog.Write(
                    $"BringToFront no-op (hwnd FG) proc={target.ProcessName} " +
                    $"hwnd=0x{target.Handle.ToInt64():X}");
                return true;
            }

            AllowSetForegroundWindow(ASFW_ANY);
            ForceForegroundWindow(target.Handle);

            for (var attempt = 0; attempt < 8; attempt++)
            {
                Thread.Sleep(40 + attempt * 20);
                var snap = GetForegroundSnapshot();
                if (snap is not null && snap.Handle == target.Handle)
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

        // Zaten FG → Alt enjeksiyonu / AttachThreadInput yok
        var foreground = GetForegroundWindow();
        if (foreground == hWnd)
        {
            MacroDebugLog.Write($"ForceForeground no-op (hwnd already FG) 0x{hWnd.ToInt64():X}");
            return;
        }

        // Alt / SwitchToThisWindow(fAltTab) yok — başka tarayıcıyı öne atıyordu
        if (IsIconic(hWnd))
            ShowWindow(hWnd, SW_RESTORE);
        else
            ShowWindow(hWnd, SW_SHOW);

        BringWindowToTop(hWnd);
        AllowSetForegroundWindow(ASFW_ANY);
        SetForegroundWindow(hWnd);

        foreground = GetForegroundWindow();
        if (foreground == hWnd)
            return;

        var targetThread = GetWindowThreadProcessId(hWnd, out _);
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();

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
