using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BrowserKeyMacro.Services;

namespace BrowserKeyMacro.Controls;

public sealed class KeyAssignedEventArgs : EventArgs
{
    public KeyAssignedEventArgs(Keys key, string? storageId, string source, int keyValue)
    {
        Key = key;
        StorageId = storageId;
        Source = source;
        KeyValue = keyValue;
    }

    public Keys Key { get; }
    public string? StorageId { get; }
    public string Source { get; }
    public int KeyValue { get; }
}

/// <summary>
/// Tuş atama kutusu. Değer yalnız <see cref="SetCapturedKey"/> ile değişir;
/// kullanıcı girdisi yalnız <see cref="OnKeyDown"/> içindeki <c>e.KeyCode</c>'dan gelir.
/// </summary>
public sealed class KeyCaptureTextBox : TextBox
{
    private bool _isCapturing;
    private bool _captureCompleted;
    private bool _writeAllowed;
    private int _pendingKeyValue;

    public KeyCaptureTextBox()
    {
        ReadOnly = true;
        TabStop = true;
        ShortcutsEnabled = false;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string FieldName { get; set; } = string.Empty;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Keys CapturedKey { get; private set; } = Keys.None;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? StorageId =>
        CapturedKey != Keys.None && KeyBindingMapper.TryFromKeyCode(CapturedKey, out var id, out _, out _)
            ? id
            : null;

    [DefaultValue(true)]
    public bool AllowClear { get; set; } = true;

    [DefaultValue(false)]
    public bool AllowMouseButtons { get; set; }

    [Browsable(false)]
    public bool IsCapturing => _isCapturing;

    public event EventHandler? CaptureStarted;
    public event EventHandler? CaptureEnded;
    public event EventHandler<string>? CaptureMessage;
    public event EventHandler<KeyAssignedEventArgs>? KeyAssigned;

    /// <summary>Kutuya yazmanın tek yolu dışındaki tüm Text atamaları reddedilir ve loglanır.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            if (_writeAllowed || string.Equals(value ?? string.Empty, base.Text, StringComparison.Ordinal))
            {
                base.Text = value;
                return;
            }

            MacroDebugLog.Write(
                $"SET KEY BLOCKED | Field={FieldName} | Text=\"{value}\" | Source=Text setter (SetCapturedKey dışı)" +
                $"{Environment.NewLine}STACK TRACE:{Environment.NewLine}{Environment.StackTrace}");
        }
    }

    /// <summary>Kutu değerini, ekran adını ve modeli değiştiren TEK metot.</summary>
    public bool SetCapturedKey(Keys key, string source)
    {
        string? storageId = null;
        var display = string.Empty;

        if (key != Keys.None
            && !KeyBindingMapper.TryFromKeyCode(key, out storageId, out display, out var error))
        {
            MacroDebugLog.Write(
                $"SET KEY REJECTED | Field={FieldName} | Key={key} | Value={(int)key} | Source={source} | {error}" +
                $"{Environment.NewLine}STACK TRACE:{Environment.NewLine}{Environment.StackTrace}");
            return false;
        }

        _writeAllowed = true;
        try
        {
            base.Text = display;
        }
        finally
        {
            _writeAllowed = false;
        }

        CapturedKey = key;
        MacroDebugLog.Write(
            $"SET KEY | Field={FieldName} | Key={key} | Value={(int)key} | Source={source}" +
            $"{Environment.NewLine}STACK TRACE:{Environment.NewLine}{Environment.StackTrace}");

        var keyValue = _pendingKeyValue;
        _pendingKeyValue = 0;
        KeyAssigned?.Invoke(this, new KeyAssignedEventArgs(key, storageId, source, keyValue));
        return true;
    }

    /// <summary>Odak zaten kutudayken (tanı testi) yakalamayı yeniden başlatır.</summary>
    public void StartCapture()
    {
        if (!Focused)
            Focus();
        BeginCaptureMode(restart: true);
    }

    protected override void OnEnter(EventArgs e)
    {
        BeginCaptureMode(restart: true);
        base.OnEnter(e);
    }

    protected override void OnLeave(EventArgs e)
    {
        EndCaptureMode();
        base.OnLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (!_isCapturing)
                BeginCaptureMode(restart: true);
        }
        else if (AllowMouseButtons && _isCapturing && !_captureCompleted)
        {
            var mouseKey = e.Button switch
            {
                MouseButtons.XButton1 => Keys.XButton1,
                MouseButtons.XButton2 => Keys.XButton2,
                MouseButtons.Middle => Keys.MButton,
                _ => Keys.None
            };

            if (mouseKey != Keys.None)
            {
                _captureCompleted = true;
                SetCapturedKey(mouseKey, "KeyCaptureTextBox.OnMouseDown");
                EndCaptureMode();
            }
        }

        base.OnMouseDown(e);
    }

    protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
    {
        if (e.KeyCode is Keys.Tab or Keys.Up or Keys.Down or Keys.Left or Keys.Right
            or Keys.Enter or Keys.Escape
            or Keys.ControlKey or Keys.ShiftKey or Keys.Menu
            or Keys.LControlKey or Keys.RControlKey
            or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin)
        {
            e.IsInputKey = true;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        var capturedKey = e.KeyCode;
        MacroDebugLog.Write(
            $"KEYDOWN | Field={FieldName} | KeyCode={capturedKey} | KeyValue={e.KeyValue} | " +
            $"capturing={_isCapturing} | completed={_captureCompleted}");

        if (!_isCapturing || _captureCompleted)
        {
            MacroDebugLog.Write(
                $"SET KEY IGNORED | Field={FieldName} | Key={capturedKey} | Value={(int)capturedKey} | " +
                "Source=KeyCaptureTextBox.OnKeyDown (yakalama kapalı — kutuya tıkla)" +
                $"{Environment.NewLine}STACK TRACE:{Environment.NewLine}{Environment.StackTrace}");
            return;
        }

        if (capturedKey is Keys.Back or Keys.Delete)
        {
            if (!AllowClear)
                return;

            _captureCompleted = true;
            SetCapturedKey(Keys.None, "KeyCaptureTextBox.OnKeyDown (temizle)");
            CaptureMessage?.Invoke(this, "Tuş temizlendi");
            EndCaptureMode();
            return;
        }

        if (KeyBindingMapper.IsModifierOnly(capturedKey))
        {
            CaptureMessage?.Invoke(this, "Tek başına Ctrl/Alt/Shift atanamaz");
            return;
        }

        if (!KeyBindingMapper.TryFromKeyCode(capturedKey, out _, out _, out var error))
        {
            CaptureMessage?.Invoke(this, error ?? $"Bu tuş atanamaz: {capturedKey}");
            return;
        }

        _captureCompleted = true;
        _pendingKeyValue = e.KeyValue;
        SetCapturedKey(capturedKey, "KeyCaptureTextBox.OnKeyDown");
        EndCaptureMode();
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        e.Handled = true;
    }

    private void BeginCaptureMode(bool restart)
    {
        if (_isCapturing && !restart)
            return;

        var wasCapturing = _isCapturing;
        _isCapturing = true;
        _captureCompleted = false;
        SelectAll();
        MacroDebugLog.Write($"CAPTURE START | Field={FieldName}");
        CaptureMessage?.Invoke(this, $"Tuş yakalanıyor ({FieldName}) — bir tuşa basın");
        if (!wasCapturing)
            CaptureStarted?.Invoke(this, EventArgs.Empty);
    }

    private void EndCaptureMode()
    {
        if (!_isCapturing)
            return;

        _isCapturing = false;
        MacroDebugLog.Write($"CAPTURE END | Field={FieldName}");
        CaptureEnded?.Invoke(this, EventArgs.Empty);
    }
}
