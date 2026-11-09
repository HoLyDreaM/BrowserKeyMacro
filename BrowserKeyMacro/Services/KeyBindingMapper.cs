namespace BrowserKeyMacro.Services;

/// <summary>
/// Tuş atama: yalnızca <see cref="Keys"/> / sanal tuş kodu.
/// Klavye düzeni, KeyChar, ScanCode, ToUnicode kullanılmaz.
/// INI: sabit enum tarzı id (F, D1, NumPad1). UI: okunabilir ad.
/// </summary>
public static class KeyBindingMapper
{
    /// <summary>Yalnız tetik listesinde kullanılan, KeyCapture ile atanmayan tuşlar.</summary>
    private static readonly (string Id, string Display, ushort Vk, string[] Aliases)[] TriggerExtras =
    {
        ("Ctrl", "Ctrl", 0x11, new[] { "Control", "ControlKey" }),
        ("Shift", "Shift", 0x10, new[] { "ShiftKey" }),
        ("Alt", "Alt", 0x12, new[] { "Menu" }),
        ("Insert", "Insert", 0x2D, new[] { "Ins" }),
        ("Delete", "Delete", 0x2E, new[] { "Del" }),
        ("Home", "Home", 0x24, Array.Empty<string>()),
        ("End", "End", 0x23, Array.Empty<string>()),
        ("PageUp", "Page Up", 0x21, new[] { "PgUp", "Prior" }),
        ("PageDown", "Page Down", 0x22, new[] { "PgDn", "Next" }),
        ("Pause", "Pause", 0x13, Array.Empty<string>())
    };

    /// <summary>Tetik açılır listesi: harf ve rakam yok (oyun/klavye yazılımı onları yeniden enjekte edebiliyor).</summary>
    public static IReadOnlyList<string> TriggerChoices { get; } = BuildTriggerChoices();

    private static List<string> BuildTriggerChoices()
    {
        var list = new List<string>();
        for (var i = 1; i <= 12; i++)
            list.Add($"F{i}");
        list.AddRange(new[] { "Escape", "Tab", "Space", "Enter", "Ctrl", "Shift", "Alt",
            "Left", "Right", "Up", "Down", "Insert", "Delete", "Home", "End", "PageUp", "PageDown", "Pause" });
        return list;
    }

    private static bool TryGetTriggerExtra(string key, out (string Id, string Display, ushort Vk) extra)
    {
        foreach (var e in TriggerExtras)
        {
            if (key.Equals(e.Id, StringComparison.OrdinalIgnoreCase)
                || key.Equals(e.Display, StringComparison.OrdinalIgnoreCase)
                || e.Aliases.Any(a => key.Equals(a, StringComparison.OrdinalIgnoreCase)))
            {
                extra = (e.Id, e.Display, e.Vk);
                return true;
            }
        }

        extra = default;
        return false;
    }

    /// <summary>
    /// KeyDown <paramref name="keyCode"/> → INI id + ekran adı.
    /// Modifier tek başına reddedilir.
    /// </summary>
    public static bool TryFromKeyCode(
        Keys keyCode,
        out string storageId,
        out string displayName,
        out string? error)
    {
        storageId = string.Empty;
        displayName = string.Empty;
        error = null;

        var code = keyCode & Keys.KeyCode;
        if (code == Keys.None)
        {
            error = "Geçersiz tuş.";
            return false;
        }

        if (IsModifierOnly(code))
        {
            error = "Tek başına Ctrl/Alt/Shift/Win atanamaz.";
            return false;
        }

        if (code is Keys.Back or Keys.Delete)
        {
            error = null;
            return false;
        }

        if (!TryStorageIdFromKeys(code, out storageId!))
        {
            error = $"Bu tuş atanamaz: {code}";
            return false;
        }

        displayName = ToDisplayName(storageId);
        return true;
    }

    /// <summary>INI id → Keys (yükleme). Layout bağımsız.</summary>
    public static bool TryParseKeys(string? storageOrRaw, out Keys key)
    {
        key = Keys.None;
        var id = NormalizeStorageId(storageOrRaw);
        if (id is null)
            return false;

        if (id.Length == 1 && id[0] is >= 'A' and <= 'Z')
        {
            key = Keys.A + (id[0] - 'A');
            return true;
        }

        if (id.Length == 2 && id[0] == 'D' && id[1] is >= '0' and <= '9')
        {
            key = Keys.D0 + (id[1] - '0');
            return true;
        }

        if (id.StartsWith("NumPad", StringComparison.Ordinal) && id.Length == 7
            && id[6] is >= '0' and <= '9')
        {
            key = Keys.NumPad0 + (id[6] - '0');
            return true;
        }

        if (id.Length >= 2 && id[0] == 'F'
            && int.TryParse(id[1..], out var fn) && fn is >= 1 and <= 12)
        {
            key = Keys.F1 + (fn - 1);
            return true;
        }

        key = id switch
        {
            "Enter" => Keys.Enter,
            "Tab" => Keys.Tab,
            "Space" => Keys.Space,
            "Escape" => Keys.Escape,
            "Left" => Keys.Left,
            "Right" => Keys.Right,
            "Up" => Keys.Up,
            "Down" => Keys.Down,
            "X1" => Keys.XButton1,
            "X2" => Keys.XButton2,
            "MButton" => Keys.MButton,
            _ => Keys.None
        };

        return key != Keys.None;
    }

    public static bool IsModifierOnly(Keys keyCode)
    {
        var code = keyCode & Keys.KeyCode;
        return code is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin;
    }

    /// <summary>INI / Tag için sabit id. Eski "1", "Boşluk" vb. kabul eder.</summary>
    public static string? NormalizeStorageId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var key = raw.Trim();

        if (TryGetTriggerExtra(key, out var extra))
            return extra.Id;

        // Zaten D0–D9
        if (key.Length == 2 && key[0] is 'D' or 'd'
            && key[1] is >= '0' and <= '9')
            return $"D{key[1]}";

        // NumPad
        if (key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)
            && key.Length == 7 && key[6] is >= '0' and <= '9')
            return $"NumPad{key[6]}";

        if (key.StartsWith("Num ", StringComparison.OrdinalIgnoreCase)
            && key.Length == 5 && key[4] is >= '0' and <= '9')
            return $"NumPad{key[4]}";

        if (key.Equals("Boşluk", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Space", StringComparison.OrdinalIgnoreCase)
            || key == " ")
            return "Space";

        if (key.Equals("Enter", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Return", StringComparison.OrdinalIgnoreCase))
            return "Enter";

        if (key.Equals("Esc", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Escape", StringComparison.OrdinalIgnoreCase))
            return "Escape";

        if (key.Equals("Sol Ok", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Left", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ArrowLeft", StringComparison.OrdinalIgnoreCase))
            return "Left";

        if (key.Equals("Sağ Ok", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Right", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ArrowRight", StringComparison.OrdinalIgnoreCase))
            return "Right";

        if (key.Equals("Yukarı Ok", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Up", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ArrowUp", StringComparison.OrdinalIgnoreCase))
            return "Up";

        if (key.Equals("Aşağı Ok", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Down", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ArrowDown", StringComparison.OrdinalIgnoreCase))
            return "Down";

        if (key.Equals("Tab", StringComparison.OrdinalIgnoreCase))
            return "Tab";

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

        // Tek rakam → D#
        if (key.Length == 1 && key[0] is >= '0' and <= '9')
            return $"D{key[0]}";

        // A–Z
        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            if (ch is >= 'A' and <= 'Z')
                return ch.ToString();
        }

        // Keys enum adı (Keys.F.ToString() == "F")
        if (Enum.TryParse<Keys>(key, ignoreCase: true, out var parsed)
            && parsed != Keys.None
            && TryStorageIdFromKeys(parsed & Keys.KeyCode, out var fromEnum))
            return fromEnum;

        return null;
    }

    public static string ToDisplayName(string storageId)
    {
        var id = NormalizeStorageId(storageId) ?? storageId;

        if (id.Length == 2 && id[0] == 'D' && id[1] is >= '0' and <= '9')
            return id[1].ToString();

        if (id.StartsWith("NumPad", StringComparison.Ordinal) && id.Length == 7)
            return $"Num {id[6]}";

        if (TryGetTriggerExtra(id, out var extra))
            return extra.Display;

        return id switch
        {
            "Space" => "Boşluk",
            "Enter" => "Enter",
            "Escape" => "Esc",
            "Left" => "Sol Ok",
            "Right" => "Sağ Ok",
            "Up" => "Yukarı Ok",
            "Down" => "Aşağı Ok",
            "Tab" => "Tab",
            "X1" => "X1",
            "X2" => "X2",
            "MButton" => "Orta Tuş",
            _ => id
        };
    }

    public static bool TryGetVirtualKey(string? storageOrDisplay, out ushort vk)
    {
        vk = 0;
        var id = NormalizeStorageId(storageOrDisplay);
        if (id is null)
            return false;

        if (TryGetTriggerExtra(id, out var extra))
        {
            vk = extra.Vk;
            return true;
        }

        if (id.Length == 1 && id[0] is >= 'A' and <= 'Z')
        {
            vk = (ushort)id[0];
            return true;
        }

        if (id.Length == 2 && id[0] == 'D' && id[1] is >= '0' and <= '9')
        {
            vk = (ushort)('0' + (id[1] - '0')); // VK_0..VK_9 = 0x30..0x39
            return true;
        }

        if (id.StartsWith("NumPad", StringComparison.Ordinal) && id.Length == 7
            && id[6] is >= '0' and <= '9')
        {
            vk = (ushort)(0x60 + (id[6] - '0')); // VK_NUMPAD0
            return true;
        }

        if (id.Length >= 2 && id[0] == 'F'
            && int.TryParse(id[1..], out var fn) && fn is >= 1 and <= 12)
        {
            vk = (ushort)(0x70 + (fn - 1));
            return true;
        }

        vk = id switch
        {
            "Enter" => 0x0D,
            "Tab" => 0x09,
            "Space" => 0x20,
            "Escape" => 0x1B,
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            "X1" => 0x05,
            "X2" => 0x06,
            "MButton" => 0x04,
            _ => (ushort)0
        };

        return vk != 0;
    }

    /// <summary>LL-hook / durum: VK → ekran adı (layout bağımsız).</summary>
    public static string DisplayFromVirtualKey(ushort vk)
    {
        if (vk is >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();

        if (vk is >= 0x30 and <= 0x39)
            return ((char)vk).ToString();

        if (vk is >= 0x60 and <= 0x69)
            return $"Num {(char)('0' + (vk - 0x60))}";

        if (vk is >= 0x70 and <= 0x7B)
            return $"F{vk - 0x6F}";

        return vk switch
        {
            0x0D => "Enter",
            0x09 => "Tab",
            0x20 => "Boşluk",
            0x1B => "Esc",
            0x25 => "Sol Ok",
            0x26 => "Yukarı Ok",
            0x27 => "Sağ Ok",
            0x28 => "Aşağı Ok",
            0x10 or 0xA0 or 0xA1 => "Shift",
            0x11 or 0xA2 or 0xA3 => "Ctrl",
            0x12 or 0xA4 or 0xA5 => "Alt",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x13 => "Pause",
            0x05 => "X1",
            0x06 => "X2",
            0x04 => "Orta Tuş",
            _ => $"VK_0x{vk:X2}"
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

    private static bool TryStorageIdFromKeys(Keys code, out string? storageId)
    {
        storageId = null;

        if (code is >= Keys.A and <= Keys.Z)
        {
            storageId = code.ToString().ToUpperInvariant();
            return true;
        }

        if (code is >= Keys.D0 and <= Keys.D9)
        {
            storageId = $"D{(char)('0' + (code - Keys.D0))}";
            return true;
        }

        if (code is >= Keys.NumPad0 and <= Keys.NumPad9)
        {
            storageId = $"NumPad{(char)('0' + (code - Keys.NumPad0))}";
            return true;
        }

        if (code is >= Keys.F1 and <= Keys.F12)
        {
            storageId = $"F{(code - Keys.F1) + 1}";
            return true;
        }

        storageId = code switch
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

        return storageId is not null;
    }
}
