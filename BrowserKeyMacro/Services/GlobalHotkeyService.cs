using System.Runtime.InteropServices;

namespace BrowserKeyMacro.Services;

/// <summary>
/// Birincil tetikleyici: RegisterRawInputDevices (keyboard, RIDEV_INPUTSINK) → WM_INPUT → edge → UI BeginInvoke.
/// Yedek: yalnız kayıt başarısız veya WM_INPUT gelmezse GetAsyncKeyState poll.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmInput = 0x00FF;
    private const int RidInput = 0x10000003;
    private const int RimTypeKeyboard = 1;
    private const int RidevInputSink = 0x00000100;
    private const int RidevRemove = 0x00000001;
    private const ushort RiKeyBreak = 0x0001;
    private const int DebounceMs = 400;
    private const int FallbackWatchMs = 800;

    private IntPtr _hwndTarget;
    private Control? _ui;
    private Action? _onTrigger;
    private Thread? _pollThread;
    private Thread? _watchThread;
    private volatile bool _stop;
    private volatile bool _registered;
    private volatile bool _rawInputOk;
    private volatile bool _usePollFallback;
    private volatile bool _wmInputSeen;
    private volatile ushort _vk;
    private string _keyName = string.Empty;
    private int _lastWin32Error;
    private int _rawDown;
    private int _pollDown;
    private long _lastFireTick;
    private int _spyBudget;
    private volatile bool _suppressInput;
    private bool _disposed;

    public bool IsRegistered => _registered;
    public string CurrentKeyName => _keyName;
    public ushort CurrentVk => _vk;
    public int LastWin32Error => _lastWin32Error;
    public bool IsRawInputActive => _rawInputOk && !_usePollFallback;
    public bool IsPollFallbackActive => _usePollFallback;

    /// <summary>
    /// SendInput sırasında Raw Input / poll tetik ve spy kapalı — kendi gönderdiğimiz tuşlar
    /// "yanlış tuş" diye durum kartını doldurmasın, F yeniden ateşlenmesin.
    /// </summary>
    public void SuppressInput(bool suppress) => _suppressInput = suppress;

    /// <summary>
    /// Ana form HWND — Raw Input hwndTarget. HandleCreated sonrası çağrılmalı.
    /// </summary>
    public void SetNotifyWindow(IntPtr hwnd)
    {
        _hwndTarget = hwnd;
        MacroDebugLog.Write($"SetNotifyWindow HWND=0x{hwnd.ToInt64():X}");
    }

    public void SetUiNotify(Control ui, Action onTrigger)
    {
        _ui = ui;
        _onTrigger = onTrigger;
    }

    public bool TryRegister(string keyName, out int win32Error)
    {
        win32Error = 0;
        _lastWin32Error = 0;
        Unregister();

        if (!KeyboardInputService.TryMapVirtualKey(keyName, out var vk))
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
            MacroDebugLog.Write("Register fail: UI notify yok");
            return false;
        }

        if (_hwndTarget == IntPtr.Zero)
        {
            if (_ui.IsHandleCreated)
                _hwndTarget = _ui.Handle;
            if (_hwndTarget == IntPtr.Zero)
            {
                win32Error = 1400;
                _lastWin32Error = win32Error;
                MacroDebugLog.Write("Register fail: notify HWND yok");
                return false;
            }
        }

        _vk = vk;
        _keyName = keyName;
        _stop = false;
        _spyBudget = 12; // yalnız dosya logu; UI'ye gitmez
        _suppressInput = false;
        _wmInputSeen = false;
        _usePollFallback = false;
        _rawInputOk = false;
        Interlocked.Exchange(ref _rawDown, 0);
        Interlocked.Exchange(ref _pollDown, 0);
        Interlocked.Exchange(ref _lastFireTick, 0);

        if (!TryRegisterRawInput(out win32Error))
        {
            MacroDebugLog.Write($"Raw Input register FAIL Win32={win32Error} → Fallback poll armed");
            _rawInputOk = false;
            _usePollFallback = true;
            StartPollThread();
            _registered = true;
            MacroDebugLog.Write(
                $"Register success (poll-fallback) VK=0x{vk:X2} ({keyName}) HWND=0x{_hwndTarget.ToInt64():X}");
            return true;
        }

        _rawInputOk = true;
        MacroDebugLog.Write(
            $"Raw Input register OK VK=0x{vk:X2} ({keyName}) HWND=0x{_hwndTarget.ToInt64():X} RIDEV_INPUTSINK");
        StartWatchdogThread();
        _registered = true;
        MacroDebugLog.Write(
            $"Register success (rawinput-primary) VK=0x{vk:X2} ({keyName}) path=rawinput");
        return true;
    }

    public void Unregister()
    {
        var was = _registered || _vk != 0;
        var prevVk = _vk;
        var prevName = _keyName;
        var prevPath = _usePollFallback ? "poll-fallback" : (_rawInputOk ? "rawinput" : "none");

        _registered = false;
        _stop = true;

        if (_pollThread is not null)
        {
            if (!_pollThread.Join(1000))
                MacroDebugLog.Write("Poll thread Join timeout");
            _pollThread = null;
        }

        if (_watchThread is not null)
        {
            if (!_watchThread.Join(1000))
                MacroDebugLog.Write("Watch thread Join timeout");
            _watchThread = null;
        }

        if (_rawInputOk && _hwndTarget != IntPtr.Zero)
            UnregisterRawInput();

        _rawInputOk = false;
        _usePollFallback = false;
        _wmInputSeen = false;
        _vk = 0;
        _keyName = string.Empty;
        if (was)
            MacroDebugLog.Write($"Unregister VK=0x{prevVk:X2} ({prevName}) path={prevPath}");
    }

    /// <summary>WM_INPUT bu servise ait mi (ana form WndProc kontrolü).</summary>
    public bool IsOurHotkeyMessage(in Message m) =>
        m.Msg == WmInput && _registered && _rawInputOk;

    /// <summary>
    /// Ana form WndProc içinden çağrılır. WM_INPUT işler; true = bu mesaj Raw Input idi.
    /// </summary>
    public bool ProcessMessage(ref Message m)
    {
        if (m.Msg != WmInput || !_registered || !_rawInputOk)
            return false;

        try
        {
            if (!TryReadRawKeyboard(m.LParam, out var vKey, out var keyUp, out var makeCode))
                return true;

            _wmInputSeen = true;
            if (_suppressInput)
                return true;

            var vk = (ushort)vKey;

            // Dosya logu (kısa bütçe) — UI'ye yanlış tuş spam'i YOK
            if (!keyUp && vk != _vk && _spyBudget > 0 && _vk != 0)
            {
                _spyBudget--;
                var name = VkDisplayName(vk);
                MacroDebugLog.Write(
                    $"KEY spy (rawinput) VK=0x{vk:X2} ({name}) make=0x{makeCode:X} (want 0x{_vk:X2} {_keyName})");
            }

            if (vk == _vk && _vk != 0)
            {
                if (keyUp)
                {
                    Interlocked.Exchange(ref _rawDown, 0);
                }
                else if (!_usePollFallback && Interlocked.CompareExchange(ref _rawDown, 1, 0) == 0)
                {
                    Fire("rawinput");
                }
            }
        }
        catch (Exception ex)
        {
            MacroDebugLog.Write($"ProcessMessage WM_INPUT hata: {ex.Message}");
        }

        return true;
    }

    public void LogDiagnostic(string reason, WindowSnapshot? foreground)
    {
        var path = !_registered ? "none"
            : _usePollFallback ? "poll-fallback"
            : _rawInputOk ? "rawinput"
            : "none";
        MacroDebugLog.Write(
            $"{reason} | registered={_registered} path={path} key={_keyName} VK=0x{_vk:X2} wmInputSeen={_wmInputSeen} | " +
            $"aktif Process={foreground?.ProcessName ?? "-"} Title=\"{foreground?.Title ?? "-"}\"");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Unregister();
        _disposed = true;
    }

    private bool TryRegisterRawInput(out int win32Error)
    {
        win32Error = 0;
        var devices = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = RidevInputSink,
                hwndTarget = _hwndTarget
            }
        };

        if (RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            return true;

        win32Error = Marshal.GetLastWin32Error();
        _lastWin32Error = win32Error;
        return false;
    }

    private void UnregisterRawInput()
    {
        try
        {
            var devices = new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,
                    usUsage = 0x06,
                    dwFlags = RidevRemove,
                    hwndTarget = IntPtr.Zero
                }
            };
            RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            MacroDebugLog.Write("Raw Input unregister (RIDEV_REMOVE)");
        }
        catch (Exception ex)
        {
            MacroDebugLog.Write($"Raw Input unregister hata: {ex.Message}");
        }
    }

    private void StartPollThread()
    {
        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "BKM-PollFallback",
            Priority = ThreadPriority.AboveNormal
        };
        _pollThread.Start();
    }

    private void StartWatchdogThread()
    {
        _watchThread = new Thread(WatchdogLoop)
        {
            IsBackground = true,
            Name = "BKM-RawWatch",
            Priority = ThreadPriority.BelowNormal
        };
        _watchThread.Start();
    }

    /// <summary>
    /// Raw Input kayıtlı ama WM_INPUT hiç gelmezken tetikleyici basılırsa poll yedeğini silahlandır.
    /// Kısa yarış için: tuş aktivitesinden sonra bir dönem daha bekler; hâlâ WM_INPUT yoksa silahlanır.
    /// </summary>
    private void WatchdogLoop()
    {
        MacroDebugLog.Write("Raw Input watchdog start");
        var pendingProve = false;
        while (!_stop && _registered && !_usePollFallback)
        {
            try
            {
                if (_wmInputSeen)
                {
                    MacroDebugLog.Write("Raw Input watchdog: WM_INPUT görüldü, poll gerekmez");
                    break;
                }

                if (_vk != 0)
                {
                    var down = (GetAsyncKeyState(_vk) & 0x8000) != 0;
                    if (down && !pendingProve)
                    {
                        pendingProve = true;
                        MacroDebugLog.Write(
                            "Raw Input watchdog: tetikleyici basıldı, WM_INPUT doğrulaması bekleniyor…");
                    }
                    else if (pendingProve && !_wmInputSeen)
                    {
                        MacroDebugLog.Write(
                            "Fallback poll armed (no WM_INPUT after proven key activity) → path=poll-fallback");
                        _usePollFallback = true;
                        StartPollThread();
                        Interlocked.Exchange(ref _pollDown, 0);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MacroDebugLog.Write($"Watchdog hata: {ex.Message}");
            }

            Thread.Sleep(FallbackWatchMs);
        }

        MacroDebugLog.Write("Raw Input watchdog exit");
    }

    private void PollLoop()
    {
        MacroDebugLog.Write("PollLoop start 5ms (fallback)");
        while (!_stop)
        {
            try
            {
                if (_registered && _usePollFallback && _vk != 0 && !_suppressInput)
                {
                    var down = (GetAsyncKeyState(_vk) & 0x8000) != 0;
                    if (!down)
                    {
                        Interlocked.Exchange(ref _pollDown, 0);
                    }
                    else if (Interlocked.CompareExchange(ref _pollDown, 1, 0) == 0)
                    {
                        Fire("poll-fallback");
                    }
                }
            }
            catch (Exception ex)
            {
                MacroDebugLog.Write($"PollLoop hata: {ex.Message}");
            }

            Thread.Sleep(5);
        }

        MacroDebugLog.Write("PollLoop exit");
    }

    private void Fire(string source)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastFireTick);
        if (last != 0 && now - last < DebounceMs)
        {
            MacroDebugLog.Write($"Trigger skip ({source}) debounce");
            return;
        }

        Interlocked.Exchange(ref _lastFireTick, now);

        var fg = GetForegroundWindow();
        GetWindowThreadProcessId(fg, out var pid);
        var proc = "?";
        try
        {
            if (pid != 0)
                proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            // ignore
        }

        MacroDebugLog.Write(
            $"Trigger FIRE ({source}) VK=0x{_vk:X2} ({_keyName}) fgProc={proc} → BeginInvoke");

        var ui = _ui;
        var cb = _onTrigger;
        if (ui is null || cb is null || ui.IsDisposed)
            return;

        try
        {
            if (ui.IsHandleCreated)
                ui.BeginInvoke(cb);
            else
                MacroDebugLog.Write("Trigger FIRE ama UI handle yok");
        }
        catch (Exception ex)
        {
            MacroDebugLog.Write($"BeginInvoke hata: {ex.Message}");
        }
    }

    private static bool TryReadRawKeyboard(IntPtr hRawInput, out ushort vKey, out bool keyUp, out ushort makeCode)
    {
        vKey = 0;
        keyUp = false;
        makeCode = 0;

        uint size = 0;
        GetRawInputData(hRawInput, RidInput, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (size == 0)
            return false;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var written = GetRawInputData(hRawInput, RidInput, buffer, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
            if (written == unchecked((uint)-1) || written == 0)
                return false;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.dwType != RimTypeKeyboard)
                return false;

            var kbPtr = IntPtr.Add(buffer, Marshal.SizeOf<RAWINPUTHEADER>());
            var kb = Marshal.PtrToStructure<RAWKEYBOARD>(kbPtr);
            vKey = kb.VKey;
            makeCode = kb.MakeCode;
            keyUp = (kb.Flags & RiKeyBreak) != 0;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string VkDisplayName(ushort vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Esc",
        0x05 => "X1",
        0x06 => "X2",
        0x04 => "MButton",
        0xBD => "-",
        0xBB => "=",
        0xDB => "[",
        0xDD => "]",
        0xBC => ",",
        0xBE => ".",
        0xC0 => "`",
        _ => $"VK_0x{vk:X2}"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public int dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public int dwType;
        public int dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
