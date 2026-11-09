using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BrowserKeyMacro.Services;

public sealed class BrowserWindowInfo
{
    public IntPtr Handle { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int ProcessId { get; init; }

    public string DisplayText => $"{Title} | {ProcessName}";
}

public sealed class BrowserWindowService
{
    /// <summary>
    /// Yalnızca ComboBox listesini doldurmak için kullanılır.
    /// Tuş gönderimi / aktif kontrol bu listeye bakmaz; seçilen ProcessName dinamik kullanılır.
    /// </summary>
    private static readonly HashSet<string> KnownBrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "msedgewebview2",
        "firefox",
        "brave",
        "opera",
        "opera_gx",
        "vivaldi",
        "chromium",
        "iexplore",
        "waterfox",
        "librewolf",
        "tor",
        "maxthon",
        "yandex",
        "browser",
        "arc",
        "sidekick",
        "iridium",
        "ungoogled-chromium",
        "slimjet",
        "centbrowser",
        "comet",
        "dragon",
        "seamonkey",
        "palemoon"
    };

    public IReadOnlyList<BrowserWindowInfo> GetOpenBrowserWindows()
    {
        var results = new List<BrowserWindowInfo>();
        var seen = new HashSet<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                return true;

            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                return true;

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == 0)
                return true;

            string processName;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                return true;
            }

            if (!IsBrowserProcess(processName))
                return true;

            if (!seen.Add(hWnd))
                return true;

            results.Add(new BrowserWindowInfo
            {
                Handle = hWnd,
                ProcessName = processName,
                Title = title.Trim(),
                ProcessId = (int)processId
            });

            return true;
        }, IntPtr.Zero);

        return results
            .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public BrowserWindowInfo? FindMatchingWindow(string processName, string title)
    {
        var windows = GetOpenBrowserWindows();
        if (windows.Count == 0)
            return null;

        var exact = windows.FirstOrDefault(w =>
            w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)
            && w.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        return windows.FirstOrDefault(w =>
            w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Seçilen HWND hâlâ açıksa process/title bilgisini günceller.
    /// HWND geçersizse aynı process adı (+ mümkünse başlık) ile yeniden bulur.
    /// </summary>
    public BrowserWindowInfo? RefreshSelectedWindow(BrowserWindowInfo selected)
    {
        if (selected.Handle != IntPtr.Zero && IsWindow(selected.Handle))
        {
            GetWindowThreadProcessId(selected.Handle, out var processId);
            string processName = selected.ProcessName;
            try
            {
                if (processId != 0)
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
            }
            catch
            {
                // eski process adını koru
            }

            var title = GetWindowTitle(selected.Handle).Trim();
            if (string.IsNullOrWhiteSpace(title))
                title = selected.Title;

            return new BrowserWindowInfo
            {
                Handle = selected.Handle,
                ProcessName = processName,
                Title = title,
                ProcessId = processId != 0 ? (int)processId : selected.ProcessId
            };
        }

        return FindMatchingWindow(selected.ProcessName, selected.Title);
    }

    /// <summary>
    /// Seçilen işlem adına ait görünür üst düzey pencere var mı? (tarayıcı adına özel koşul yok)
    /// </summary>
    public bool HasVisibleWindowForProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var found = false;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                return true;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                return true;

            GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == 0)
                return true;

            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    return false; // dur
                }
            }
            catch
            {
                // yoksay
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static bool IsBrowserProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        if (KnownBrowserProcesses.Contains(processName))
            return true;

        // Liste dışı tarayıcıları da göstermek için genel ipuçları (yalnız keşif)
        var lower = processName.ToLowerInvariant();
        return lower.Contains("chrome", StringComparison.Ordinal)
            || lower.Contains("edge", StringComparison.Ordinal)
            || lower.Contains("brave", StringComparison.Ordinal)
            || lower.Contains("opera", StringComparison.Ordinal)
            || lower.Contains("firefox", StringComparison.Ordinal)
            || lower.Contains("browser", StringComparison.Ordinal)
            || lower.Contains("chromium", StringComparison.Ordinal);
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

    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
}
