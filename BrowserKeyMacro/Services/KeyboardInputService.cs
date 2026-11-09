using System.Runtime.InteropServices;

namespace BrowserKeyMacro.Services;

public sealed class KeySendResult
{
    public string KeyName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public uint DownSent { get; init; }
    public uint UpSent { get; init; }
    public int LastWin32Error { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class KeyboardInputService
{
    private const int KeyDownUpGapMs = 20;

    /// <summary>
    /// SendInput <c>dwExtraInfo</c> işareti — kendi enjeksiyonumuzu Raw Input'ta yok saymak için.
    /// Caps Lock harf VK eşlemesini bozmaz; yol <c>ToUnicode</c>/char değil sabit VK ('A'..'Z', '0'..'9').
    /// </summary>
    public const uint InjectedExtraInfoValue = 0x00424B4D; // 'BKM\0'

    public static readonly UIntPtr InjectedExtraInfo = (UIntPtr)InjectedExtraInfoValue;

    public IReadOnlyList<string> NormalizeSequence(IEnumerable<string?> keys)
    {
        var list = new List<string>();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var normalized = NormalizeKeyName(key);
            if (normalized is null)
                continue;

            list.Add(normalized);
        }

        return list;
    }

    /// <summary>
    /// Ara tuş doluysa her ana tuştan hemen önce ara tuşu ekler: E,1,E,2,...
    /// </summary>
    public IReadOnlyList<string> BuildSequenceWithIntermediate(
        IEnumerable<string?> mainKeys,
        string? intermediateKey)
    {
        var mains = NormalizeSequence(mainKeys);
        if (mains.Count == 0)
            return mains;

        var intermediate = NormalizeKeyName(intermediateKey);
        if (intermediate is null)
            return mains;

        var expanded = new List<string>(mains.Count * 2);
        foreach (var key in mains)
        {
            expanded.Add(intermediate);
            expanded.Add(key);
        }

        return expanded;
    }

    /// <summary>
    /// Tuşları sırayla bir kez gönderir (döngü yok). Her tuş için KEYDOWN + bekleme + KEYUP.
    /// </summary>
    public IReadOnlyList<KeySendResult> SendSequenceDetailed(
        IReadOnlyList<string> keys,
        int delayMs,
        Func<bool>? canContinue = null,
        Action<KeySendResult>? onKeySent = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<KeySendResult>();
        if (keys.Count == 0)
            return results;

        var delay = Math.Clamp(delayMs, 10, 5000);

        for (var i = 0; i < keys.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (canContinue is not null && !canContinue())
                break;

            var result = SendKeyAhkStyle(keys[i]);
            results.Add(result);
            onKeySent?.Invoke(result);

            if (!result.Success)
                break;

            if (i < keys.Count - 1)
                Thread.Sleep(delay);
        }

        return results;
    }

    public int SendSequence(
        IReadOnlyList<string> keys,
        int delayMs,
        Func<bool>? canContinue = null,
        CancellationToken cancellationToken = default)
    {
        return SendSequenceDetailed(keys, delayMs, canContinue, null, cancellationToken)
            .Count(r => r.Success);
    }

    /// <summary>
    /// Tetik tuşu hâlâ fiziksel basılıysa KEYUP enjekte et — oyun F basılıyken Z/1/3 yutabiliyor.
    /// Yalnız GetAsyncKeyState 0x8000 true iken; dwExtraInfo ile Raw echo armed desync önlenir.
    /// </summary>
    public void ForceKeyUp(string keyName)
    {
        var normalized = NormalizeKeyName(keyName);
        if (normalized is null || !TryMapVirtualKey(normalized, out var vk))
            return;
        ForceKeyUpVk(vk, normalized);
    }

    /// <summary>F klavye: tetik F fizikselde VK_Q olabilir — kayıtlı VK kullan.</summary>
    public void ForceKeyUpVk(ushort vk, string? label = null)
    {
        if (vk == 0)
            return;

        if ((GetAsyncKeyState(vk) & 0x8000) == 0)
            return;

        var mapped = MapVirtualKey(vk, MAPVK_VK_TO_VSC_EX);
        var scanCode = (ushort)(mapped & 0xFF);
        if (scanCode == 0)
            scanCode = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);

        var extended = IsExtendedKey(vk) || (mapped & 0xFF000000) != 0;
        var extFlag = (extended ? KEYEVENTF_EXTENDEDKEY : 0u) | KEYEVENTF_SCANCODE;
        _ = SendOne(0, scanCode, extFlag, keyUp: true, out _);
        _ = SendOne(vk, scanCode, extended ? KEYEVENTF_EXTENDEDKEY : 0u, keyUp: true, out _);
        MacroDebugLog.Write($"ForceKeyUp {label ?? "vk"} vk=0x{vk:X2} extra=0x{InjectedExtraInfoValue:X}");
        Thread.Sleep(30);
    }

    /// <summary>
    /// AutoHotkey SendInput benzeri: VK + scan KEYDOWN / KEYUP (Test ve hotkey aynı yol).
    /// Caps Lock açık/kapalı fark etmez — harf/rakam sabit VK; ToUnicode kullanılmaz.
    /// </summary>
    public KeySendResult SendKeyAhkStyle(string keyName)
    {
        var normalized = NormalizeKeyName(keyName);
        if (normalized is null || !TryMapVirtualKey(normalized, out var vk))
        {
            return new KeySendResult
            {
                KeyName = keyName,
                Success = false,
                Message = $"Geçersiz tuş: {keyName}"
            };
        }

        var mapped = MapVirtualKey(vk, MAPVK_VK_TO_VSC_EX);
        var scanCode = (ushort)(mapped & 0xFF);
        if (scanCode == 0)
            scanCode = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);

        var extended = IsExtendedKey(vk) || (mapped & 0xFF000000) != 0;
        var extFlag = extended ? KEYEVENTF_EXTENDEDKEY : 0u;
        var path = "vk+scan";

        // 1.18'de çalışan yol: VK + scan (oyun bunu alıyordu)
        var downSent = SendOne(vk, scanCode, extFlag, keyUp: false, out var downError);
        Thread.Sleep(KeyDownUpGapMs);
        var upSent = SendOne(vk, scanCode, extFlag, keyUp: true, out var upError);

        if (downSent != 1 || upSent != 1)
        {
            path = "scancode";
            MacroDebugLog.Write(
                $"SendInput VK yolu zayıf key={normalized} down={downSent} up={upSent} — scancode");
            var scanFlags = KEYEVENTF_SCANCODE | extFlag;
            downSent = SendOne(0, scanCode, scanFlags, keyUp: false, out downError);
            Thread.Sleep(KeyDownUpGapMs);
            upSent = SendOne(0, scanCode, scanFlags, keyUp: true, out upError);
        }

        var lastError = downSent != 1 ? downError : (upSent != 1 ? upError : 0);
        var ok = downSent == 1 && upSent == 1;

        var message = ok
            ? $"{normalized} gönderildi | SendInput DOWN={downSent} UP={upSent}"
            : $"{normalized} HATA | SendInput DOWN={downSent} UP={upSent} Win32={lastError}";

        MacroDebugLog.Write(
            $"SendInput key={normalized} vk=0x{vk:X2} scan=0x{scanCode:X2} ext={extended} path={path} " +
            $"down={downSent} up={upSent} win32={lastError}");

        return new KeySendResult
        {
            KeyName = normalized,
            Success = ok,
            DownSent = downSent,
            UpSent = upSent,
            LastWin32Error = lastError,
            Message = message
        };
    }

    private static uint SendOne(ushort vk, ushort scan, uint flags, bool keyUp, out int win32Error)
    {
        var f = flags;
        if (keyUp)
            f |= KEYEVENTF_KEYUP;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = f,
                    time = 0,
                    dwExtraInfo = InjectedExtraInfo
                }
            }
        };

        SetLastError(0);
        var sent = SendInput(1, new[] { input }, INPUT.Size);
        win32Error = Marshal.GetLastWin32Error();
        return sent;
    }

    private static bool IsExtendedKey(ushort vk) =>
        vk is 0x25 or 0x26 or 0x27 or 0x28 // arrows
            or 0x2D or 0x2E or 0x24 or 0x23 // Ins Del Home End
            or 0x21 or 0x22 // PgUp PgDn
            or 0x5B or 0x5C; // Win

    /// <summary>INI / Tag sabit id (F, D1, NumPad1). Layout yok.</summary>
    public static string? NormalizeKeyName(string? raw) =>
        KeyBindingMapper.NormalizeStorageId(raw);

    public static string? FromKeys(Keys keys) =>
        KeyBindingMapper.TryFromKeyCode(keys, out var id, out _, out _) ? id : null;

    public static string? FromMouseButtons(MouseButtons buttons) =>
        KeyBindingMapper.FromMouseButtons(buttons);

    /// <summary>Sabit VK (Keys enum). Char/ToUnicode/scan yok.</summary>
    public static bool TryMapVirtualKey(string keyName, out ushort vk) =>
        KeyBindingMapper.TryGetVirtualKey(keyName, out vk);

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint MAPVK_VK_TO_VSC_EX = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf<INPUT>();
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint dwErrCode);
}
