using System.Text;
using BrowserKeyMacro.Models;
using BrowserKeyMacro.Services;

namespace BrowserKeyMacro;

public partial class MainForm : Form
{
    private readonly AppSettingsService _settingsService = new();
    private readonly BrowserWindowService _browserWindowService = new();
    private readonly ForegroundBrowserService _foregroundService;
    private readonly KeyboardInputService _keyboardService = new();
    private readonly GlobalHotkeyService _hotkeyService = new();

    private AppSettings _settings = new();
    private BrowserWindowInfo? _selectedBrowser;
    private bool _isRunning;
    private bool _isSending;
    private int _sendingGate; // 0 = idle, 1 = sending (Interlocked)
    private string _activeTrigger = "Q";
    private int _pendingTrigger; // 1 = gönderim bitince bir kez daha çalıştır

    public MainForm()
    {
        _foregroundService = new ForegroundBrowserService(_browserWindowService);
        InitializeComponent();
        ApplyBrandingIcons();
        cmbBrowser.SelectedIndexChanged += CmbBrowser_SelectedIndexChanged;
        _hotkeyService.SetUiNotify(this, OnGlobalTriggerFromBackground, OnKeySpyMismatch);
    }

    private void ApplyBrandingIcons()
    {
        var appIcon = AppAssets.TryLoadAppIcon();
        if (appIcon is not null)
        {
            Icon = appIcon;
            _notifyIcon.Icon = (Icon)appIcon.Clone();
        }
        else
        {
            Icon = SystemIcons.Application;
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _notifyIcon.Text = "Browser Key Macro";

        var logo = AppAssets.TryLoadLogoImage();
        if (logo is not null)
            picLogo.Image = logo;
    }

    private void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            _settings = _settingsService.Load();
            ApplySettingsToUi(_settings);
            RefreshBrowserList(selectFromSettings: true);
            SetStatus("Hazır");
            UpdateRunningUi(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Ayarlar yüklenirken hata oluştu:\n{ex.Message}", "Hata",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Ayar yükleme hatası");
        }
    }

    private void ApplySettingsToUi(AppSettings settings)
    {
        txtTrigger.Text = KeyboardInputService.NormalizeKeyName(settings.Trigger) ?? "Q";
        txtIntermediate.Text = KeyboardInputService.NormalizeKeyName(settings.IntermediateKey) ?? string.Empty;
        txtKey1.Text = KeyboardInputService.NormalizeKeyName(settings.Key1) ?? string.Empty;
        txtKey2.Text = KeyboardInputService.NormalizeKeyName(settings.Key2) ?? string.Empty;
        txtKey3.Text = KeyboardInputService.NormalizeKeyName(settings.Key3) ?? string.Empty;
        txtKey4.Text = KeyboardInputService.NormalizeKeyName(settings.Key4) ?? string.Empty;
        nudDelay.Value = Math.Clamp(settings.DelayMs, 10, 5000);
        chkBringFront.Checked = settings.BringBrowserToFront;
    }

    private AppSettings CaptureSettingsFromUi()
    {
        var selected = cmbBrowser.SelectedItem as BrowserWindowInfo;
        return new AppSettings
        {
            SelectedBrowserProcess = selected?.ProcessName
                ?? _selectedBrowser?.ProcessName
                ?? _settings.SelectedBrowserProcess,
            SelectedBrowserTitle = selected?.Title
                ?? _selectedBrowser?.Title
                ?? _settings.SelectedBrowserTitle,
            DelayMs = (int)nudDelay.Value,
            BringBrowserToFront = chkBringFront.Checked,
            Trigger = string.IsNullOrWhiteSpace(txtTrigger.Text) ? "Q" : txtTrigger.Text.Trim(),
            IntermediateKey = txtIntermediate.Text.Trim(),
            Key1 = txtKey1.Text.Trim(),
            Key2 = txtKey2.Text.Trim(),
            Key3 = txtKey3.Text.Trim(),
            Key4 = txtKey4.Text.Trim()
        };
    }

    private void SaveSettingsSafe()
    {
        try
        {
            _settings = CaptureSettingsFromUi();
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Ayarlar kaydedilemedi:\n{ex.Message}", "Hata",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CmbBrowser_SelectedIndexChanged(object? sender, EventArgs e)
    {
        _selectedBrowser = cmbBrowser.SelectedItem as BrowserWindowInfo;
        LogSelectedBrowser("Seçim değişti");
    }

    private void LogSelectedBrowser(string reason)
    {
        var s = _selectedBrowser ?? cmbBrowser.SelectedItem as BrowserWindowInfo;
        if (s is null)
        {
            MacroDebugLog.Write($"{reason}: seçili tarayıcı yok");
            return;
        }

        MacroDebugLog.Write(
            $"{reason}: HWND=0x{s.Handle.ToInt64():X} Process={s.ProcessName} Title=\"{s.Title}\" PID={s.ProcessId}");
    }

    private void BtnRefresh_Click(object? sender, EventArgs e)
    {
        RefreshBrowserList(selectFromSettings: false);
    }

    private void RefreshBrowserList(bool selectFromSettings)
    {
        try
        {
            var previous = cmbBrowser.SelectedItem as BrowserWindowInfo;
            var windows = _browserWindowService.GetOpenBrowserWindows();

            cmbBrowser.BeginUpdate();
            cmbBrowser.DataSource = null;
            cmbBrowser.DisplayMember = nameof(BrowserWindowInfo.DisplayText);
            cmbBrowser.ValueMember = nameof(BrowserWindowInfo.Handle);
            cmbBrowser.DataSource = windows.ToList();
            cmbBrowser.EndUpdate();

            if (windows.Count == 0)
            {
                _selectedBrowser = null;
                SetStatus("Açık tarayıcı penceresi bulunamadı.");
                return;
            }

            BrowserWindowInfo? match = null;
            if (selectFromSettings)
            {
                match = _browserWindowService.FindMatchingWindow(
                    _settings.SelectedBrowserProcess,
                    _settings.SelectedBrowserTitle);
            }
            else if (previous is not null)
            {
                match = windows.FirstOrDefault(w => w.Handle == previous.Handle)
                    ?? windows.FirstOrDefault(w =>
                        w.ProcessName.Equals(previous.ProcessName, StringComparison.OrdinalIgnoreCase)
                        && w.Title.Equals(previous.Title, StringComparison.OrdinalIgnoreCase))
                    ?? windows.FirstOrDefault(w =>
                        w.ProcessName.Equals(previous.ProcessName, StringComparison.OrdinalIgnoreCase));
            }

            if (match is not null)
            {
                var index = windows.ToList().FindIndex(w => w.Handle == match.Handle);
                if (index >= 0)
                    cmbBrowser.SelectedIndex = index;
            }
            else if (cmbBrowser.Items.Count > 0)
            {
                cmbBrowser.SelectedIndex = 0;
            }

            _selectedBrowser = cmbBrowser.SelectedItem as BrowserWindowInfo;
            LogSelectedBrowser("Liste yenilendi");
            if (!_isRunning)
                SetStatus("Hazır");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Tarayıcı listesi yenilenemedi:\n{ex.Message}", "Hata",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void KeyCapture_Enter(object? sender, EventArgs e)
    {
        if (sender is TextBox tb)
            tb.SelectAll();
    }

    private void KeyCapture_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode is Keys.Back or Keys.Delete)
        {
            if (!ReferenceEquals(tb, txtTrigger))
                tb.Text = string.Empty;
            return;
        }

        var mapped = KeyboardInputService.FromKeys(e.KeyCode);
        if (mapped is null)
            return;

        tb.Text = mapped;
    }

    private void TriggerCapture_MouseDown(object? sender, MouseEventArgs e)
    {
        if (sender is not TextBox)
            return;

        var mapped = KeyboardInputService.FromMouseButtons(e.Button);
        if (mapped is null)
            return;

        txtTrigger.Text = mapped;
    }

    private void BtnStart_Click(object? sender, EventArgs e)
    {
        if (_isRunning)
            return;

        try
        {
            _selectedBrowser = cmbBrowser.SelectedItem as BrowserWindowInfo;
            if (_selectedBrowser is null)
            {
                MessageBox.Show(this, "Lütfen bir tarayıcı penceresi seçin.", "Uyarı",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var trigger = KeyboardInputService.NormalizeKeyName(txtTrigger.Text) ?? "Q";
            txtTrigger.Text = trigger;

            var mainKeys = _keyboardService.NormalizeSequence(new[]
            {
                txtKey1.Text, txtKey2.Text, txtKey3.Text, txtKey4.Text
            });

            if (mainKeys.Count == 0)
            {
                MessageBox.Show(this, "En az bir tuş tanımlayın.", "Uyarı",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var intermediate = KeyboardInputService.NormalizeKeyName(txtIntermediate.Text);
            txtIntermediate.Text = intermediate ?? string.Empty;

            // AHK gibi: hedef her zaman öne alınacak
            chkBringFront.Checked = true;

            SaveSettingsSafe();
            LogSelectedBrowser("Makro başlatıldı");
            _hotkeyService.SetUiNotify(this, OnGlobalTriggerFromBackground, OnKeySpyMismatch);
            _hotkeyService.SetNotifyWindow(Handle);

            if (!_hotkeyService.TryRegister(trigger, out var win32Error))
            {
                SetStatus($"{trigger} tuşu kaydedilemedi. Win32 hata: {win32Error}");
                UpdateTrayText("Tetikleyici kaydı başarısız");
                return;
            }

            _activeTrigger = trigger;
            _isRunning = true;
            UpdateRunningUi(true);
            _statusTimer.Start();
            Interlocked.Exchange(ref _pendingTrigger, 0);
            SetStatus($"Tetikleyici aktif: {trigger} — oyundayken '{trigger}' bas (v1.17)");
            UpdateTrayText($"Tetikleyici aktif: {trigger}");
            MacroDebugLog.Write($"Hazır: oyundayken '{trigger}' bas. Yanlış tuşlar durum kartında görünür.");
        }
        catch (Exception ex)
        {
            _isRunning = false;
            UpdateRunningUi(false);
            _hotkeyService.Unregister();
            MessageBox.Show(this, $"Makro başlatılamadı:\n{ex.Message}", "Hata",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Başlatma hatası");
        }
    }

    private void BtnStop_Click(object? sender, EventArgs e) => StopMacro("Hazır");

    private void BtnTest_Click(object? sender, EventArgs e) =>
        ExecuteTriggerSameAsButton("button");

    /// <summary>TETİKLE butonu ve global tetikleyici — tek ortak yol.</summary>
    private void ExecuteTriggerSameAsButton(string source)
    {
        MacroDebugLog.Write($"EXECUTE TETİKLE yolu source={source}");
        RunSequenceOnce(isTest: !_isRunning, requireRunning: false);
    }

    private void StopMacro(string status)
    {
        try
        {
            _statusTimer.Stop();
            _hotkeyService.Unregister();
        }
        catch
        {
            // ignore
        }

        _isRunning = false;
        _isSending = false;
        Interlocked.Exchange(ref _sendingGate, 0);
        UpdateRunningUi(false);
        SetStatus(status);
        UpdateTrayText(status);
    }

    /// <summary>Raw Input / poll-fallback → UI thread BeginInvoke. Oyundayken burası çalışmalı.</summary>
    private void OnGlobalTriggerFromBackground()
    {
        MacroDebugLog.Write("Trigger received (BeginInvoke)");
        var fg = _foregroundService.GetForegroundSnapshot();
        MacroDebugLog.Write(
            $"Trigger UI fg HWND=0x{fg?.Handle.ToInt64() ?? 0:X} Process={fg?.ProcessName ?? "-"} Title=\"{fg?.Title ?? "-"}\"");

        if (!_isRunning)
        {
            _hotkeyService.LogDiagnostic("Trigger ignored (makro kapalı)", fg);
            return;
        }

        // Gönderim sürerken yok sayma — bir sonraki tur için kuyruğa al
        if (_isSending || Interlocked.CompareExchange(ref _sendingGate, 0, 0) == 1)
        {
            Interlocked.Exchange(ref _pendingTrigger, 1);
            MacroDebugLog.Write("Trigger queued (gönderim sürüyor — bitince çalışacak)");
            SetStatus($"Tetikleyici kuyruğa alındı ({_activeTrigger}) — gönderim bitince");
            return;
        }

        var key = string.IsNullOrEmpty(_hotkeyService.CurrentKeyName)
            ? _activeTrigger
            : _hotkeyService.CurrentKeyName;

        SetStatus($"Global tetikleyici alındı: {key}");
        UpdateTrayText($"Global tetikleyici alındı: {key}");
        try { System.Media.SystemSounds.Beep.Play(); } catch { /* ignore */ }
        ExecuteTriggerSameAsButton("hotkey");
    }

    /// <summary>Oyunda tetikleyici dışında bir tuş basıldı — kullanıcıya göster.</summary>
    private void OnKeySpyMismatch(ushort vk, string name)
    {
        if (!_isRunning || _isSending)
            return;
        SetStatus($"Algılanan: {name} — tetikleyici '{_activeTrigger}' değil (oyunda {_activeTrigger} bas)");
    }

    /// <summary>
    /// Test ve tetikleyici aynı yol: WinActivate + SendInput.
    /// </summary>
    private void RunSequenceOnce(bool isTest, bool requireRunning)
    {
        if (requireRunning && !_isRunning)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => RunSequenceOnce(isTest, requireRunning)));
            return;
        }

        if (Interlocked.CompareExchange(ref _sendingGate, 1, 0) != 0)
        {
            SetStatus("Gönderim devam ediyor…");
            return;
        }

        _selectedBrowser = cmbBrowser.SelectedItem as BrowserWindowInfo ?? _selectedBrowser;
        if (_selectedBrowser is null)
        {
            Interlocked.Exchange(ref _sendingGate, 0);
            SetStatus("Seçilen tarayıcı yok.");
            return;
        }

        var sequence = _keyboardService.BuildSequenceWithIntermediate(
            new[] { txtKey1.Text, txtKey2.Text, txtKey3.Text, txtKey4.Text },
            txtIntermediate.Text);

        if (sequence.Count == 0)
        {
            Interlocked.Exchange(ref _sendingGate, 0);
            SetStatus("Tuş sırası boş.");
            return;
        }

        var selected = _selectedBrowser;
        var delay = (int)nudDelay.Value;
        _isSending = true;
        MacroDebugLog.Write("Macro execution started");
        if (isTest)
            SetStatus("Test: hedef öne alınıyor…");

        _ = Task.Run(() =>
        {
            try
            {
                ExecuteMacroOnce(selected, bringFront: true, delay, sequence, isTest);
            }
            finally
            {
                _isSending = false;
                Interlocked.Exchange(ref _sendingGate, 0);
                MacroDebugLog.Write("Macro execution completed");

                // Kuyruktaki tetikleyiciyi bir kez çalıştır
                if (Interlocked.Exchange(ref _pendingTrigger, 0) == 1 && _isRunning)
                {
                    MacroDebugLog.Write("Pending trigger flush");
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!_isRunning || _isSending)
                                return;
                            SetStatus($"Global tetikleyici alındı: {_activeTrigger} (kuyruk)");
                            ExecuteTriggerSameAsButton("hotkey-queued");
                        }));
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        });
    }

    private void ExecuteMacroOnce(
        BrowserWindowInfo? selected,
        bool bringFront,
        int delay,
        IReadOnlyList<string> sequence,
        bool isTest)
    {
        try
        {
            if (selected is null)
            {
                UiSetStatus("Seçilen tarayıcı tanımlı değil.");
                return;
            }

            var refreshed = _browserWindowService.RefreshSelectedWindow(selected) ?? selected;
            _selectedBrowser = refreshed;

            MacroDebugLog.Write(
                $"{(isTest ? "TEST" : "HOTKEY")} selected HWND=0x{refreshed.Handle.ToInt64():X} " +
                $"Process={refreshed.ProcessName} Title=\"{refreshed.Title}\"");

            var fgBefore = _foregroundService.GetForegroundSnapshot();
            MacroDebugLog.Write(
                $"GetForegroundWindow HWND=0x{fgBefore?.Handle.ToInt64():X} " +
                $"Process={fgBefore?.ProcessName} Title=\"{fgBefore?.Title}\"");

            // Oyundayken zaten ön plandaysa BringToFront atla — hemen gönder
            var evalBefore = _foregroundService.Evaluate(refreshed);
            if (!evalBefore.IsMatch)
            {
                UiSetStatus($"Hedef öne alınıyor: {refreshed.ProcessName}…");
                if (!_foregroundService.TryBringToFront(refreshed))
                {
                    var afterBring = _foregroundService.Evaluate(refreshed);
                    if (!afterBring.IsMatch)
                    {
                        var fgFail = _foregroundService.GetForegroundSnapshot();
                        _hotkeyService.LogDiagnostic("Tetikleme başarısız (öne alma)", fgFail);
                        UiSetStatus(afterBring.WindowClosed
                            ? afterBring.Message
                            : $"Seçilen hedef ön planda değil ({afterBring.ForegroundProcessName ?? refreshed.ProcessName}). Tuş gönderilmedi.");
                        return;
                    }
                }

                Thread.Sleep(80);
            }
            else
            {
                MacroDebugLog.Write("Hedef zaten ön planda — BringToFront atlandı");
            }

            var eval = _foregroundService.Evaluate(refreshed);
            if (!eval.IsMatch)
            {
                var fgFail = _foregroundService.GetForegroundSnapshot();
                _hotkeyService.LogDiagnostic(
                    eval.WindowClosed
                        ? "Tetikleme başarısız (hedef kapalı)"
                        : "Tetikleme başarısız (hedef ön planda değil)",
                    fgFail);
                UiSetStatus(eval.WindowClosed
                    ? eval.Message
                    : $"Seçilen hedef ön planda değil ({eval.ForegroundProcessName ?? "?"}). Tuş gönderilmedi.");
                return;
            }

            var statusParts = new List<string>();
            var abortedFocus = false;
            var abortMessage = "Seçilen işlem ön planda değil, tuş gönderilmedi";

            var results = _keyboardService.SendSequenceDetailed(
                sequence,
                delay,
                canContinue: () =>
                {
                    var check = _foregroundService.Evaluate(refreshed);
                    if (check.IsMatch)
                        return true;

                    abortedFocus = true;
                    abortMessage = check.WindowClosed
                        ? check.Message
                        : $"{check.Message} — tuş gönderilmedi";
                    return false;
                },
                onKeySent: result =>
                {
                    statusParts.Add(result.Success
                        ? $"{result.KeyName} gönderildi (SendInput: {result.DownSent + result.UpSent})"
                        : $"{result.KeyName} HATA Win32={result.LastWin32Error}");

                    var preview = string.Join(" → ", statusParts.TakeLast(4));
                    UiSetStatus(preview);
                });

            if (abortedFocus)
            {
                UiSetStatus(abortMessage);
                return;
            }

            var failed = results.FirstOrDefault(r => !r.Success);
            if (failed is not null)
            {
                UiSetStatus(
                    $"{failed.KeyName} başarısız | SendInput DOWN={failed.DownSent} UP={failed.UpSent} Win32={failed.LastWin32Error}");
                return;
            }

            if (isTest)
            {
                var summary = new StringBuilder();
                summary.Append("Test tamamlandı");
                summary.Append($" ({results.Count} tuş)");
                if (results.Count > 0)
                {
                    summary.Append(" | ");
                    summary.Append(string.Join(", ", results.Select(r => $"{r.KeyName} OK")));
                }

                UiSetStatus(summary.ToString());
            }
            else
            {
                UiSetStatus("Makro tamamlandı");
            }
        }
        catch (Exception ex)
        {
            MacroDebugLog.Write($"Gönderim hatası: {ex}");
            UiSetStatus($"Gönderim hatası: {ex.Message}");
        }
    }

    private void UiSetStatus(string text)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UiSetStatus(text)));
            return;
        }

        SetStatus(text);
        UpdateTrayText(text);
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isRunning || _isSending)
            return;

        var selected = _selectedBrowser;
        if (selected is null)
            return;

        var eval = _foregroundService.Evaluate(selected);
        if (eval.WindowClosed)
        {
            SetStatus("Seçilen tarayıcı kapalı.");
            UpdateTrayText("Tarayıcı kapalı");
        }
        else if (!eval.IsMatch
                 && (lblStatus.Text is "Makro aktif" or "Makro tamamlandı" or "Seçilen tarayıcı aktif."
                     || lblStatus.Text.StartsWith("Makro tamamlandı", StringComparison.Ordinal)))
        {
            SetStatus("Seçilen tarayıcı aktif değil.");
            UpdateTrayText("Tarayıcı aktif değil");
        }
        else if (eval.IsMatch && lblStatus.Text.Contains("aktif değil", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus($"Tetikleyici aktif: {_activeTrigger}");
            UpdateTrayText($"Tetikleyici aktif: {_activeTrigger}");
        }
    }

    private void UpdateRunningUi(bool running)
    {
        btnStart.Enabled = !running;
        btnStop.Enabled = running;
        cmbBrowser.Enabled = !running;
        btnRefresh.Enabled = !running;
        txtTrigger.Enabled = !running;
        txtIntermediate.Enabled = !running;
        txtKey1.Enabled = !running;
        txtKey2.Enabled = !running;
        txtKey3.Enabled = !running;
        txtKey4.Enabled = !running;
        nudDelay.Enabled = !running;
        chkBringFront.Enabled = !running;
        // Test her zaman kullanılabilir; eşzamanlı gönderim _sendingGate ile engellenir
        btnTest.Enabled = true;
    }

    private void SetStatus(string text)
    {
        lblStatus.Text = text;
    }

    private void UpdateTrayText(string status)
    {
        var text = $"Browser Key Macro — {status}";
        if (text.Length > 63)
            text = text[..63];
        _notifyIcon.Text = text;
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        // Küçültünce görev çubuğunda kalsın; gizleme / tepsiye kaçırma yok
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
    {
        if (!Visible)
            Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        // X ile gerçekten kapanır (arka planda bırakılmaz)
        SaveSettingsSafe();
        StopMacro("Durduruldu");
        _notifyIcon.Visible = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hotkeyService.SetNotifyWindow(Handle);
        _hotkeyService.SetUiNotify(this, OnGlobalTriggerFromBackground, OnKeySpyMismatch);
    }

    protected override void WndProc(ref Message m)
    {
        // Raw Input WM_INPUT → GlobalHotkeyService (hwndTarget = bu form)
        _ = _hotkeyService.ProcessMessage(ref m);
        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_notifyIcon.ContextMenuStrip is null)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Göster", null, (_, _) => NotifyIcon_DoubleClick(null, EventArgs.Empty));
            menu.Items.Add("Durdur", null, (_, _) =>
            {
                if (_isRunning)
                    StopMacro("Hazır");
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (_, _) => Close());
            _notifyIcon.ContextMenuStrip = menu;
        }
    }
}
