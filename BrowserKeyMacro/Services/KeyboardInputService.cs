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
    /// AutoHotkey SendInput benzeri: VK + ScanCode, KEYDOWN / KEYUP.
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

        // 1) Klasik AHK yolu: wVk + wScan
        var downSent = SendOne(vk, scanCode, extFlag, keyUp: false, out var downError);
        Thread.Sleep(KeyDownUpGapMs);
        var upSent = SendOne(vk, scanCode, extFlag, keyUp: true, out var upError);

        // 2) Yedek: saf scan-code (bazı oyun / tarayıcı motorları)
        if (downSent != 1 || upSent != 1)
        {
            MacroDebugLog.Write(
                $"SendInput VK yolu zayıf key={normalized} down={downSent} up={upSent} — scan-code deneniyor");

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
            $"SendInput key={normalized} vk=0x{vk:X2} scan=0x{scanCode:X2} ext={extended} " +
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
                    dwExtraInfo = UIntPtr.Zero
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

    public static string? NormalizeKeyName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var key = raw.Trim();
        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase)
            || key.Equals(" ", StringComparison.Ordinal))
            return "Space";

        if (key.Equals("Enter", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Return", StringComparison.OrdinalIgnoreCase))
            return "Enter";

        if (key.Equals("Tab", StringComparison.OrdinalIgnoreCase))
            return "Tab";

        if (key.Equals("Escape", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Esc", StringComparison.OrdinalIgnoreCase))
            return "Escape";

        if (key.Equals("Left", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ArrowLeft", StringComparison.OrdinalIgnoreCase))
            return "Left";

        if (key.Equals("Right", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ArrowRight", StringComparison.OrdinalIgnoreCase))
            return "Right";

        if (key.Equals("Up", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ArrowUp", StringComparison.OrdinalIgnoreCase))
            return "Up";

        if (key.Equals("Down", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ArrowDown", StringComparison.OrdinalIgnoreCase))
            return "Down";

        if (key.Length >= 2 && key.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key[1..], out var fn) && fn is >= 1 and <= 12)
            return $"F{fn}";

        if (key.Equals("X1", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Mouse4", StringComparison.OrdinalIgnoreCase)
            || key.Equals("XButton1", StringComparison.OrdinalIgnoreCase))
            return "X1";

        if (key.Equals("X2", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Mouse5", StringComparison.OrdinalIgnoreCase)
            || key.Equals("XButton2", StringComparison.OrdinalIgnoreCase))
            return "X2";

        if (key.Equals("MButton", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Middle", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Mouse3", StringComparison.OrdinalIgnoreCase))
            return "MButton";

        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return ch.ToString();
        }

        return null;
    }

    public static string? FromKeys(Keys keys)
    {
        if (keys is >= Keys.F1 and <= Keys.F12)
            return $"F{(keys - Keys.F1) + 1}";

        if (keys is >= Keys.D0 and <= Keys.D9)
            return ((char)('0' + (keys - Keys.D0))).ToString();

        if (keys is >= Keys.NumPad0 and <= Keys.NumPad9)
            return ((char)('0' + (keys - Keys.NumPad0))).ToString();

        if (keys is >= Keys.A and <= Keys.Z)
            return keys.ToString().ToUpperInvariant();

        return keys switch
        {
            Keys.Enter or Keys.Return => "Enter",
            Keys.Tab => "Tab",
            Keys.Space => "Space",
            Keys.Escape => "Escape",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.XButton1 => "X1",
            Keys.XButton2 => "X2",
            Keys.MButton => "MButton",
            _ => null
        };
    }

    public static string? FromMouseButtons(MouseButtons buttons) =>
        buttons switch
        {
            MouseButtons.XButton1 => "X1",
            MouseButtons.XButton2 => "X2",
            MouseButtons.Middle => "MButton",
            _ => null
        };

    public static bool TryMapVirtualKey(string keyName, out ushort vk)
    {
        vk = 0;
        var normalized = NormalizeKeyName(keyName);
        if (normalized is null)
            return false;

        if (normalized.Length == 1)
        {
            var ch = normalized[0];
            if (ch is >= 'A' and <= 'Z')
            {
                vk = (ushort)ch;
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                vk = (ushort)ch;
                return true;
            }
        }

        if (normalized.StartsWith('F') && int.TryParse(normalized[1..], out var fn) && fn is >= 1 and <= 12)
        {
            vk = (ushort)(0x70 + (fn - 1));
            return true;
        }

        vk = normalized switch
        {
            "Enter" => 0x0D,
            "Tab" => 0x09,
            "Space" => 0x20,
            "Escape" => 0x1B,
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            "X1" => 0x05,      // VK_XBUTTON1 — oyunda klavyeden daha güvenilir
            "X2" => 0x06,      // VK_XBUTTON2
            "MButton" => 0x04, // VK_MBUTTON
            _ => (ushort)0
        };

        return vk != 0;
    }

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

    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint dwErrCode);
}
