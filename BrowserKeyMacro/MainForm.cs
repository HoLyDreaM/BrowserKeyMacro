using System.Text;
using BrowserKeyMacro.Controls;
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
    private long _lastKeyStatusTick;
    private int _captureSuspendDepth;
    private bool _diagnosticCapture;
    private bool _loadingTrigger;
    private readonly List<string> _sendLines = new();

    public MainForm()
    {
        _foregroundService = new ForegroundBrowserService(_browserWindowService);
        InitializeComponent();
        var version = typeof(MainForm).Assembly.GetName().Version;
        var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
        Text = $"Browser Key Macro v{version?.Major}.{version?.Minor} | EXE: {exePath}";
        MacroDebugLog.Write($"APP START | Version={version} | EXE={exePath}");
        ApplyBrandingIcons();
        cmbBrowser.SelectedIndexChanged += CmbBrowser_SelectedIndexChanged;
        WireKeyCaptureBoxes();
        FillTriggerList();
        _hotkeyService.SetUiNotify(this, OnGlobalTriggerFromBackground, OnKeySeen);
    }

    private sealed record TriggerItem(string Id, string Display)
    {
        public override string ToString() => Display;
    }

    private void FillTriggerList()
    {
        cmbTrigger.BeginUpdate();
        cmbTrigger.Items.Clear();
        foreach (var id in KeyBindingMapper.TriggerChoices)
            cmbTrigger.Items.Add(new TriggerItem(id, KeyBindingMapper.ToDisplayName(id)));
        cmbTrigger.EndUpdate();
        cmbTrigger.SelectedIndexChanged += CmbTrigger_SelectedIndexChanged;
    }

    private string SelectedTriggerId =>
        (cmbTrigger.SelectedItem as TriggerItem)?.Id ?? KeyBindingMapper.TriggerChoices[0];

    private void SelectTrigger(string? stored)
    {
        var id = KeyBindingMapper.NormalizeStorageId(stored);
        var index = id is null ? -1 : KeyBindingMapper.TriggerChoices.ToList().IndexOf(id);
        if (index < 0)
        {
            MacroDebugLog.Write($"Trigger '{stored}' listede yok (harf/rakam tetik kaldırıldı) → {KeyBindingMapper.TriggerChoices[0]}");
            index = 0;
        }

        _loadingTrigger = true;
        try
        {
            cmbTrigger.SelectedIndex = index;
        }
        finally
        {
            _loadingTrigger = false;
        }

        _settings.Trigger = SelectedTriggerId;
    }

    private void CmbTrigger_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loadingTrigger)
            return;

        _settings.Trigger = SelectedTriggerId;
        MacroDebugLog.Write($"SET KEY | Field=Trigger | Key={_settings.Trigger} | Source=cmbTrigger (liste)");
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            MacroDebugLog.Write($"INI kayıt hatası: {ex.Message}");
        }

        SetStatus($"Tetikleyici = {KeyBindingMapper.ToDisplayName(_settings.Trigger)}");
    }

    private void WireKeyCaptureBoxes()
    {
        foreach (var box in KeyCaptureBoxes())
        {
            box.CaptureStarted += KeyCaptureBox_CaptureStarted;
            box.CaptureEnded += KeyCaptureBox_CaptureEnded;
            box.CaptureMessage += (_, msg) => SetStatus(msg);
            box.KeyAssigned += KeyCaptureBox_KeyAssigned;
        }

        txtIntermediate.FieldName = "IntermediateKey";
        txtKey1.FieldName = "Key1";
        txtKey2.FieldName = "Key2";
        txtKey3.FieldName = "Key3";
        txtKey4.FieldName = "Key4";
    }

    /// <summary>Model ve INI yalnız buradan güncellenir (kaynak: KeyCaptureTextBox.SetCapturedKey).</summary>
    private void KeyCaptureBox_KeyAssigned(object? sender, KeyAssignedEventArgs e)
    {
        if (sender is not KeyCaptureTextBox box)
            return;

        var value = e.StorageId ?? string.Empty;
        switch (box.FieldName)
        {
            case "IntermediateKey": _settings.IntermediateKey = value; break;
            case "Key1": _settings.Key1 = value; break;
            case "Key2": _settings.Key2 = value; break;
            case "Key3": _settings.Key3 = value; break;
            case "Key4": _settings.Key4 = value; break;
        }

        if (e.Source == "INI.Load")
            return;

        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            MacroDebugLog.Write($"INI kayıt hatası: {ex.Message}");
        }

        var display = e.Key == Keys.None ? "(boş)" : KeyBindingMapper.ToDisplayName(value);
        if (_diagnosticCapture && box == txtKey1)
        {
            _diagnosticCapture = false;
            SetStatus($"Tanı: KeyCode={e.Key} | KeyValue={e.KeyValue}{Environment.NewLine}" +
                      $"Atanan: {display} — macro-debug.log içinde SET KEY satırına bak");
        }
        else
        {
            SetStatus($"{box.FieldName} = {display}  (KeyCode={e.Key} | KeyValue={e.KeyValue})");
        }
    }

    private void BtnDiagnostic_Click(object? sender, EventArgs e)
    {
        if (_isRunning)
        {
            SetStatus("Tanı testi için önce makroyu durdur.");
            return;
        }

        _diagnosticCapture = true;
        MacroDebugLog.Write("DIAG | Tuş Yakalama Tanı Testi başladı (Key1 kutusu)");
        txtKey1.StartCapture();
        SetStatus("Tanı testi: şimdi tek bir tuşa bas (ör. F). Beklenen: KeyCode=F | KeyValue=70");
    }

    private IEnumerable<KeyCaptureTextBox> KeyCaptureBoxes()
    {
        yield return txtIntermediate;
        yield return txtKey1;
        yield return txtKey2;
        yield return txtKey3;
        yield return txtKey4;
    }

    private void KeyCaptureBox_CaptureStarted(object? sender, EventArgs e)
    {
        if (Interlocked.Increment(ref _captureSuspendDepth) == 1)
            _hotkeyService.BeginCaptureSuspend();
    }

    private void KeyCaptureBox_CaptureEnded(object? sender, EventArgs e)
    {
        if (Interlocked.Decrement(ref _captureSuspendDepth) > 0)
            return;

        Interlocked.Exchange(ref _captureSuspendDepth, 0);
        // Makro çalışıyorsa tetik yeniden bağlanır; atama alanı TextBox'a yazmaz
        _hotkeyService.EndCaptureSuspend(restoreIfHadKey: _isRunning);
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
        SelectTrigger(settings.Trigger);
        LoadKey(txtIntermediate, settings.IntermediateKey, Keys.None);
        LoadKey(txtKey1, settings.Key1, Keys.None);
        LoadKey(txtKey2, settings.Key2, Keys.None);
        LoadKey(txtKey3, settings.Key3, Keys.None);
        LoadKey(txtKey4, settings.Key4, Keys.None);
        nudDelay.Value = Math.Clamp(settings.DelayMs, 10, 5000);
        chkBringFront.Checked = settings.BringBrowserToFront;
        chkContinuous.Checked = settings.ContinuousLoop;
    }

    private static void LoadKey(KeyCaptureTextBox box, string? stored, Keys fallback)
    {
        var key = KeyBindingMapper.TryParseKeys(stored, out var parsed) ? parsed : fallback;
        box.SetCapturedKey(key, "INI.Load");
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
            ContinuousLoop = chkContinuous.Checked,
            Trigger = string.IsNullOrEmpty(_settings.Trigger) ? "Q" : _settings.Trigger,
            IntermediateKey = _settings.IntermediateKey,
            Key1 = _settings.Key1,
            Key2 = _settings.Key2,
            Key3 = _settings.Key3,
            Key4 = _settings.Key4
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

            var trigger = SelectedTriggerId;

            var mainKeys = _keyboardService.NormalizeSequence(new[]
            {
                txtKey1.StorageId,
                txtKey2.StorageId,
                txtKey3.StorageId,
                txtKey4.StorageId
            });

            if (mainKeys.Count == 0)
            {
                MessageBox.Show(this, "En az bir tuş tanımlayın.", "Uyarı",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveSettingsSafe();
            LogSelectedBrowser("Makro başlatıldı");
            _hotkeyService.SetUiNotify(this, OnGlobalTriggerFromBackground, OnKeySeen);
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
            var triggerDisplay = KeyBindingMapper.ToDisplayName(trigger);
            if (chkContinuous.Checked)
            {
                SetStatus($"Sürekli döngü aktif — Durdur ile bitir (tetik: {triggerDisplay})");
                UpdateTrayText("Sürekli döngü aktif");
                MacroDebugLog.Write($"Sürekli döngü başladı. trigger={trigger}");
                ExecuteTriggerSameAsButton("continuous-start");
            }
            else
            {
                SetStatus($"Tetikleyici aktif: {triggerDisplay} — basılan tuş burada");
                UpdateTrayText($"Tetikleyici aktif: {triggerDisplay}");
                MacroDebugLog.Write($"Hazır: tetik beklemede. '{trigger}' bas.");
            }
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
        _hotkeyService.SuppressInput(false);
        Interlocked.Exchange(ref _sendingGate, 0);
        UpdateRunningUi(false);
        SetStatus(status);
        UpdateTrayText(status);
    }

    private void OnKeySeen(string name, bool isTrigger)
    {
        if (!_isRunning || _isSending)
            return;

        _lastKeyStatusTick = Environment.TickCount64;
        var triggerLabel = KeyBindingMapper.ToDisplayName(_activeTrigger);
        SetStatus(isTrigger
            ? $"Basılan: {name}  ✓ tetik"
            : $"Basılan: {name}  (tetik: {triggerLabel})");
    }

    /// <summary>Raw Input / poll → UI thread. Oyundayken burası çalışmalı.</summary>
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

        _lastKeyStatusTick = Environment.TickCount64;
        SetStatus($"Tetik algılandı: {key} — gönderiliyor…");
        UpdateTrayText($"Tetik: {key}");
        try { System.Media.SystemSounds.Beep.Play(); } catch { /* ignore */ }
        ExecuteTriggerSameAsButton("hotkey");
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
            new[]
            {
                txtKey1.StorageId,
                txtKey2.StorageId,
                txtKey3.StorageId,
                txtKey4.StorageId
            },
            txtIntermediate.StorageId);

        if (sequence.Count == 0)
        {
            Interlocked.Exchange(ref _sendingGate, 0);
            SetStatus("Tuş sırası boş.");
            return;
        }

        var selected = _selectedBrowser;
        var delay = (int)nudDelay.Value;
        var continuous = chkContinuous.Checked && !isTest;
        _isSending = true;
        _hotkeyService.SuppressInput(true);
        MacroDebugLog.Write(
            $"Macro execution started seq=[{string.Join(',', sequence)}] delay={delay}ms continuous={continuous}");
        SetStatus(isTest
            ? $"Test gönderiliyor: {string.Join(" → ", sequence)}"
            : continuous
                ? $"Döngü gönderiliyor: {string.Join(" → ", sequence)}"
                : $"Gönderiliyor: {string.Join(" → ", sequence)}");

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
                _hotkeyService.SuppressInput(false);
                MacroDebugLog.Write("Macro execution completed");
            }

            if (continuous && _isRunning)
            {
                MacroDebugLog.Write($"Continuous loop: {delay}ms sonra sonraki tur");
                var waited = 0;
                while (waited < delay && _isRunning)
                {
                    Thread.Sleep(Math.Min(50, delay - waited));
                    waited += 50;
                }

                if (!_isRunning)
                    return;

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (!_isRunning || _isSending)
                            return;
                        RunSequenceOnce(isTest: false, requireRunning: true);
                    }));
                }
                catch
                {
                    // ignore
                }

                return;
            }

            // Kuyruktaki tetikleyiciyi bir kez çalıştır (sürekli döngü kapalıyken)
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

            // Seçilen HWND önde değilse öne al (aynı Edge başka pencere = yine öne al)
            var evalBefore = _foregroundService.Evaluate(refreshed);
            if (!evalBefore.IsHwndMatch)
            {
                UiSetStatus($"Hedef öne alınıyor: {refreshed.ProcessName}…");
                if (!_foregroundService.TryBringToFront(refreshed))
                {
                    var afterBring = _foregroundService.Evaluate(refreshed);
                    if (!afterBring.IsHwndMatch && !afterBring.IsMatch)
                    {
                        var fgFail = _foregroundService.GetForegroundSnapshot();
                        _hotkeyService.LogDiagnostic("Tetikleme başarısız (öne alma)", fgFail);
                        UiSetStatus(afterBring.Message);
                        return;
                    }
                }

                Thread.Sleep(80);
            }
            else
            {
                MacroDebugLog.Write(
                    $"Hedef HWND ön planda — BringToFront atlandı hwnd=0x{refreshed.Handle.ToInt64():X}");
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
                UiSetStatus(eval.Message);
                return;
            }

            // Tetik hâlâ basılıysa bırak — oyun F basılıyken diğer tuşları yutmasın
            var triggerName = string.IsNullOrEmpty(_activeTrigger) ? "F" : _activeTrigger;
            if (_hotkeyService.CurrentVk != 0)
                _keyboardService.ForceKeyUpVk(_hotkeyService.CurrentVk, triggerName);
            else
                _keyboardService.ForceKeyUp(triggerName);

            UiResetSendLines();
            var abortedFocus = false;
            var abortMessage = "Seçilen pencere ön planda değil, tuş gönderilmedi";

            // H5: canContinue process-only (Evaluate zaten process); transient miss → 1 kısa retry.
            // Abort yalnız process değişimi / pencere kapalı — BringToFront sırasında false yapılmaz.
            var results = _keyboardService.SendSequenceDetailed(
                sequence,
                delay,
                canContinue: () =>
                {
                    var check = _foregroundService.Evaluate(refreshed);
                    if (check.IsMatch)
                        return true;

                    if (check.WindowClosed)
                    {
                        abortedFocus = true;
                        abortMessage = check.Message;
                        return false;
                    }

                    // Transient miss: kısa retry (HWND titremesi / kısa focus kaybı)
                    Thread.Sleep(40);
                    check = _foregroundService.Evaluate(refreshed);
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
                    var name = KeyBindingMapper.ToDisplayName(result.KeyName);
                    UiAppendSendLine(result.Success
                        ? $"Gönderildi: {name}"
                        : $"HATA: {name} (Win32={result.LastWin32Error})");
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

            UiAppendSendLine(isTest ? "Test tamamlandı" : "Makro tamamlandı");
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

    private void UiResetSendLines()
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(UiResetSendLines));
            return;
        }

        _sendLines.Clear();
    }

    private void UiAppendSendLine(string line)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UiAppendSendLine(line)));
            return;
        }

        _sendLines.Add(line);
        while (_sendLines.Count > 6)
            _sendLines.RemoveAt(0);

        _lastKeyStatusTick = Environment.TickCount64;
        SetStatus(string.Join(Environment.NewLine, _sendLines));
        UpdateTrayText(line);
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isRunning || _isSending)
            return;

        if (Environment.TickCount64 - _lastKeyStatusTick < 4000)
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
                     || lblStatus.Text.StartsWith("Makro tamamlandı", StringComparison.Ordinal)
                     || lblStatus.Text.StartsWith("Tetikleyici aktif", StringComparison.Ordinal)))
        {
            SetStatus("Seçilen tarayıcı aktif değil — oyuna tıkla.");
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
        cmbTrigger.Enabled = !running;
        txtIntermediate.Enabled = !running;
        txtKey1.Enabled = !running;
        txtKey2.Enabled = !running;
        txtKey3.Enabled = !running;
        txtKey4.Enabled = !running;
        nudDelay.Enabled = !running;
        chkBringFront.Enabled = !running;
        chkContinuous.Enabled = !running;
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
        _hotkeyService.SetUiNotify(this, OnGlobalTriggerFromBackground, OnKeySeen);
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
