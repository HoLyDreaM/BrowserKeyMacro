using System.Runtime.InteropServices;

namespace BrowserKeyMacro.Services;

/// <summary>
/// Tetik: WH_KEYBOARD_LL + sabit VK (Keys).
/// Tuş atama sırasında tamamen unregister; callback'ler no-op.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int LlkhfUp = 0x80;
    private const int LlkhfInjected = 0x10;
    private const int DebounceMs = 200;

    private readonly LowLevelProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private Control? _ui;
    private Action? _onTrigger;
    private Action<string, bool>? _onKeySeen;
    private volatile bool _registered;
    private volatile bool _isCapturing;
    private volatile ushort _vk;
    private string _keyName = string.Empty;
    private string _pausedKeyName = string.Empty;
    private int _lastWin32Error;
    private int _wasDown;
    private int _downVk;
    private long _lastFireTick;
    private volatile bool _suppressInput;
    private bool _disposed;

    public GlobalHotkeyService() => _proc = HookCallback;

    public bool IsRegistered => _registered;
    public string CurrentKeyName => _keyName;
    public ushort CurrentVk => _vk;
    public int LastWin32Error => _lastWin32Error;
    public bool IsCapturing => _isCapturing;
    /// <summary>
    /// Tuş atama başladı: hook tamamen kapatılır, hiçbir callback TextBox/INI değiştirmez.
    /// </summary>
    public void BeginCaptureSuspend()
    {
        _isCapturing = true;
        if (_registered)
        {
            _pausedKeyName = _keyName;
            UnregisterHookOnly();
            MacroDebugLog.Write("CaptureSuspend: hook kaldırıldı");
        }
        else
        {
            MacroDebugLog.Write("CaptureSuspend: zaten kayıtsız");
        }
    }

    /// <summary>Atama bitti: makro çalışıyorsa tetik yeniden kaydedilir.</summary>
    public void EndCaptureSuspend(bool restoreIfHadKey)
    {
        _isCapturing = false;
        if (!restoreIfHadKey || string.IsNullOrEmpty(_pausedKeyName))
        {
            _pausedKeyName = string.Empty;
            MacroDebugLog.Write("CaptureResume: restore yok");
            return;
        }

        var key = _pausedKeyName;
        _pausedKeyName = string.Empty;
        if (TryRegister(key, out var err))
            MacroDebugLog.Write($"CaptureResume: yeniden kayıt OK ({key})");
        else
            MacroDebugLog.Write($"CaptureResume: kayıt FAIL ({key}) Win32={err}");
    }

    public void SuppressInput(bool suppress)
    {
        _suppressInput = suppress;
        if (!suppress)
        {
            Interlocked.Exchange(ref _wasDown, 0);
            Interlocked.Exchange(ref _downVk, 0);
            Interlocked.Exchange(ref _lastFireTick, 0);
        }

        MacroDebugLog.Write($"SuppressInput={suppress}");
    }

    public void ResetTriggerEdge(string reason)
    {
        Interlocked.Exchange(ref _wasDown, 0);
        Interlocked.Exchange(ref _lastFireTick, 0);
        MacroDebugLog.Write($"ResetTriggerEdge ({reason})");
    }

    public void SetNotifyWindow(IntPtr hwnd) =>
        MacroDebugLog.Write($"SetNotifyWindow HWND=0x{hwnd.ToInt64():X}");

    public void SetUiNotify(Control ui, Action onTrigger, Action<string, bool>? onKeySeen = null)
    {
        _ui = ui;
        _onTrigger = onTrigger;
        _onKeySeen = onKeySeen;
    }

    public bool TryRegister(string keyName, out int win32Error)
    {
        win32Error = 0;
        _lastWin32Error = 0;
        Unregister();

        if (_isCapturing)
        {
            _pausedKeyName = KeyBindingMapper.NormalizeStorageId(keyName) ?? keyName;
            MacroDebugLog.Write($"Register ertelendi (capturing) → {_pausedKeyName}");
            return true;
        }

        if (!KeyBindingMapper.TryGetVirtualKey(keyName, out var vk))
        {
            win32Error = 87;
            _lastWin32Error = win32Error;
            MacroDebugLog.Write($"Register fail invalid key '{keyName}'");
            return false;
        }

        if (_ui is null || _onTrigger is null)
        {
            win32Error = 1400;
            _lastWin32Error = win32Error;
            return false;
        }

        _vk = vk;
        _keyName = KeyBindingMapper.NormalizeStorageId(keyName) ?? keyName;
        _suppressInput = false;
        Interlocked.Exchange(ref _wasDown, 0);
        Interlocked.Exchange(ref _downVk, 0);
        Interlocked.Exchange(ref _lastFireTick, 0);

        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            win32Error = Marshal.GetLastWin32Error();
            _lastWin32Error = win32Error;
            MacroDebugLog.Write($"LL hook FAIL Win32={win32Error}");
            _vk = 0;
            _keyName = string.Empty;
            return false;
        }

        _registered = true;
        MacroDebugLog.Write($"Register LL want={_keyName} vk=0x{vk:X2}");
        return true;
    }

    public void Unregister()
    {
        var was = _registered || _hook != IntPtr.Zero;
        var prevName = _keyName;
        UnregisterHookOnly();
        _vk = 0;
        _keyName = string.Empty;
        _pausedKeyName = string.Empty;
        Interlocked.Exchange(ref _wasDown, 0);
        Interlocked.Exchange(ref _downVk, 0);
        if (was)
            MacroDebugLog.Write($"Unregister ({prevName})");
    }

    private void UnregisterHookOnly()
    {
        _registered = false;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public void LogDiagnostic(string reason, WindowSnapshot? foreground)
    {
        MacroDebugLog.Write(
            $"{reason} | registered={_registered} capturing={_isCapturing} key={_keyName} vk=0x{_vk:X2} | " +
            $"aktif Process={foreground?.ProcessName ?? "-"} Title=\"{foreground?.Title ?? "-"}\"");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Unregister();
        _disposed = true;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Atama / kayıt yokken hiçbir iş yapma
        if (nCode >= 0 && !_isCapturing && _registered && _hook != IntPtr.Zero && lParam != IntPtr.Zero)
        {
            try
            {
                var data = Marshal.PtrToStructure<KbdLlHook>(lParam);
                // Klavye yazılımı / düzen aracı / oyun istemcisi harfleri yeniden enjekte edebiliyor;
                // yalnız kendi SendInput işaretimizi (dwExtraInfo) yok say.
                var ours = (uint)data.dwExtraInfo == KeyboardInputService.InjectedExtraInfoValue;
                if (!ours)
                {
                    var keyUp = (data.flags & LlkhfUp) != 0;
                    var injected = (data.flags & LlkhfInjected) != 0;
                    HandleKey(NormalizeModifier((ushort)data.vkCode), keyUp, injected, data.scanCode);
                }
            }
            catch (Exception ex)
            {
                MacroDebugLog.Write($"LL hook hata: {ex.Message}");
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void HandleKey(ushort vk, bool keyUp, bool injected, uint scanCode)
    {
        if (_isCapturing || !_registered)
            return;

        var name = KeyBindingMapper.DisplayFromVirtualKey(vk);
        var isTrigger = _vk != 0 && vk == _vk;

        if (keyUp)
        {
            Interlocked.CompareExchange(ref _downVk, 0, vk);
            if (isTrigger)
                Interlocked.Exchange(ref _wasDown, 0);
            return;
        }

        if (_suppressInput)
        {
            if (isTrigger)
                Interlocked.Exchange(ref _wasDown, 1);
            return;
        }

        var repeat = Interlocked.Exchange(ref _downVk, vk) == vk;
        if (!repeat)
        {
            MacroDebugLog.Write(
                $"KEY vk=0x{vk:X2} '{name}' scan=0x{scanCode:X2} injected={(injected ? 1 : 0)} trigger={isTrigger} want={_keyName}");
            NotifyKeySeen(name, isTrigger);
        }

        if (isTrigger)
            TryFireEdge("ll-hook");
    }

    private void TryFireEdge(string source)
    {
        if (!_registered || _suppressInput || _isCapturing)
            return;

        if (Interlocked.CompareExchange(ref _wasDown, 1, 0) != 0)
            return;

        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastFireTick);
        if (last != 0 && now - last < DebounceMs)
        {
            MacroDebugLog.Write($"Trigger skip ({source}) debounce");
            return;
        }

        Interlocked.Exchange(ref _lastFireTick, now);
        MacroDebugLog.Write($"Trigger FIRE ({source}) {_keyName} vk=0x{_vk:X2}");

        var ui = _ui;
        var cb = _onTrigger;
        if (ui is null || cb is null || ui.IsDisposed || !ui.IsHandleCreated)
            return;

        try
        {
            ui.BeginInvoke(cb);
        }
        catch (Exception ex)
        {
            MacroDebugLog.Write($"BeginInvoke hata: {ex.Message}");
        }
    }

    private void NotifyKeySeen(string name, bool isTrigger)
    {
        if (_isCapturing)
            return;

        var ui = _ui;
        var cb = _onKeySeen;
        if (ui is null || cb is null || ui.IsDisposed || !ui.IsHandleCreated)
            return;

        try
        {
            ui.BeginInvoke(cb, name, isTrigger);
        }
        catch (Exception ex)
        {
            MacroDebugLog.Write($"KeySeen hata: {ex.Message}");
        }
    }

    private static ushort NormalizeModifier(ushort vk) => vk switch
    {
        0xA0 or 0xA1 => 0x10,
        0xA2 or 0xA3 => 0x11,
        0xA4 or 0xA5 => 0x12,
        _ => vk
    };

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHook
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
