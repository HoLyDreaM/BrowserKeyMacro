namespace BrowserKeyMacro;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _hotkeyService?.Dispose();
            _notifyIcon?.Dispose();
            _statusTimer?.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        picLogo = new PictureBox();
        lblTitle = new Label();
        lblBrowser = new Label();
        cmbBrowser = new ComboBox();
        btnRefresh = new Button();
        lblTrigger = new Label();
        txtTrigger = new TextBox();
        lblIntermediate = new Label();
        txtIntermediate = new TextBox();
        lblSequence = new Label();
        txtKey1 = new TextBox();
        txtKey2 = new TextBox();
        txtKey3 = new TextBox();
        txtKey4 = new TextBox();
        lblDelay = new Label();
        nudDelay = new NumericUpDown();
        chkBringFront = new CheckBox();
        btnStart = new Button();
        btnStop = new Button();
        btnTest = new Button();
        pnlStatus = new Panel();
        lblStatusTitle = new Label();
        lblStatus = new Label();
        _notifyIcon = new NotifyIcon(components);
        _statusTimer = new System.Windows.Forms.Timer(components);
        ((System.ComponentModel.ISupportInitialize)nudDelay).BeginInit();
        ((System.ComponentModel.ISupportInitialize)picLogo).BeginInit();
        pnlStatus.SuspendLayout();
        SuspendLayout();
        //
        // picLogo
        //
        picLogo.BackColor = Color.Transparent;
        picLogo.Location = new Point(24, 14);
        picLogo.Name = "picLogo";
        picLogo.Size = new Size(36, 36);
        picLogo.SizeMode = PictureBoxSizeMode.Zoom;
        picLogo.TabStop = false;
        //
        // lblTitle
        //
        lblTitle.AutoSize = true;
        lblTitle.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(180, 245, 230);
        lblTitle.Location = new Point(68, 18);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new Size(220, 30);
        lblTitle.Text = "Browser Key Macro";
        //
        // lblBrowser
        //
        lblBrowser.AutoSize = true;
        lblBrowser.ForeColor = Color.FromArgb(180, 185, 195);
        lblBrowser.Location = new Point(26, 68);
        lblBrowser.Name = "lblBrowser";
        lblBrowser.Size = new Size(120, 15);
        lblBrowser.Text = "Tarayıcı penceresi";
        //
        // cmbBrowser
        //
        cmbBrowser.BackColor = Color.FromArgb(45, 48, 58);
        cmbBrowser.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbBrowser.FlatStyle = FlatStyle.Flat;
        cmbBrowser.ForeColor = Color.FromArgb(235, 235, 240);
        cmbBrowser.FormattingEnabled = true;
        cmbBrowser.Location = new Point(26, 88);
        cmbBrowser.Name = "cmbBrowser";
        cmbBrowser.Size = new Size(430, 23);
        cmbBrowser.TabIndex = 0;
        //
        // btnRefresh
        //
        btnRefresh.BackColor = Color.FromArgb(60, 64, 78);
        btnRefresh.FlatAppearance.BorderSize = 0;
        btnRefresh.FlatStyle = FlatStyle.Flat;
        btnRefresh.ForeColor = Color.FromArgb(235, 235, 240);
        btnRefresh.Location = new Point(468, 86);
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Size = new Size(90, 28);
        btnRefresh.TabIndex = 1;
        btnRefresh.Text = "Yenile";
        btnRefresh.UseVisualStyleBackColor = false;
        btnRefresh.Click += BtnRefresh_Click;
        //
        // lblTrigger
        //
        lblTrigger.AutoSize = true;
        lblTrigger.ForeColor = Color.FromArgb(180, 185, 195);
        lblTrigger.Location = new Point(26, 130);
        lblTrigger.Name = "lblTrigger";
        lblTrigger.Size = new Size(75, 15);
        lblTrigger.Text = "Tetikleyici tuş";
        //
        // txtTrigger
        //
        txtTrigger.BackColor = Color.FromArgb(45, 48, 58);
        txtTrigger.BorderStyle = BorderStyle.FixedSingle;
        txtTrigger.ForeColor = Color.FromArgb(235, 235, 240);
        txtTrigger.Location = new Point(26, 150);
        txtTrigger.Name = "txtTrigger";
        txtTrigger.PlaceholderText = "Tuş veya X1/X2";
        txtTrigger.ReadOnly = true;
        txtTrigger.Size = new Size(120, 23);
        txtTrigger.TabIndex = 2;
        txtTrigger.TextAlign = HorizontalAlignment.Center;
        txtTrigger.KeyDown += KeyCapture_KeyDown;
        txtTrigger.MouseDown += TriggerCapture_MouseDown;
        txtTrigger.Enter += KeyCapture_Enter;
        //
        // lblIntermediate
        //
        lblIntermediate.AutoSize = true;
        lblIntermediate.ForeColor = Color.FromArgb(180, 185, 195);
        lblIntermediate.Location = new Point(170, 130);
        lblIntermediate.Name = "lblIntermediate";
        lblIntermediate.Size = new Size(48, 15);
        lblIntermediate.Text = "Ara tuş";
        //
        // txtIntermediate
        //
        txtIntermediate.BackColor = Color.FromArgb(45, 48, 58);
        txtIntermediate.BorderStyle = BorderStyle.FixedSingle;
        txtIntermediate.ForeColor = Color.FromArgb(235, 235, 240);
        txtIntermediate.Location = new Point(170, 150);
        txtIntermediate.Name = "txtIntermediate";
        txtIntermediate.PlaceholderText = "Boş = yok";
        txtIntermediate.ReadOnly = true;
        txtIntermediate.Size = new Size(120, 23);
        txtIntermediate.TabIndex = 3;
        txtIntermediate.TextAlign = HorizontalAlignment.Center;
        txtIntermediate.KeyDown += KeyCapture_KeyDown;
        txtIntermediate.Enter += KeyCapture_Enter;
        //
        // lblSequence
        //
        lblSequence.AutoSize = true;
        lblSequence.ForeColor = Color.FromArgb(180, 185, 195);
        lblSequence.Location = new Point(26, 190);
        lblSequence.Name = "lblSequence";
        lblSequence.Size = new Size(95, 15);
        lblSequence.Text = "Tuş sırası (1–4)";
        //
        // txtKey1
        //
        txtKey1.BackColor = Color.FromArgb(45, 48, 58);
        txtKey1.BorderStyle = BorderStyle.FixedSingle;
        txtKey1.ForeColor = Color.FromArgb(235, 235, 240);
        txtKey1.Location = new Point(26, 210);
        txtKey1.Name = "txtKey1";
        txtKey1.PlaceholderText = "Bir tuşa basın";
        txtKey1.ReadOnly = true;
        txtKey1.Size = new Size(120, 23);
        txtKey1.TabIndex = 4;
        txtKey1.TextAlign = HorizontalAlignment.Center;
        txtKey1.KeyDown += KeyCapture_KeyDown;
        txtKey1.Enter += KeyCapture_Enter;
        //
        // txtKey2
        //
        txtKey2.BackColor = Color.FromArgb(45, 48, 58);
        txtKey2.BorderStyle = BorderStyle.FixedSingle;
        txtKey2.ForeColor = Color.FromArgb(235, 235, 240);
        txtKey2.Location = new Point(160, 210);
        txtKey2.Name = "txtKey2";
        txtKey2.PlaceholderText = "Bir tuşa basın";
        txtKey2.ReadOnly = true;
        txtKey2.Size = new Size(120, 23);
        txtKey2.TabIndex = 5;
        txtKey2.TextAlign = HorizontalAlignment.Center;
        txtKey2.KeyDown += KeyCapture_KeyDown;
        txtKey2.Enter += KeyCapture_Enter;
        //
        // txtKey3
        //
        txtKey3.BackColor = Color.FromArgb(45, 48, 58);
        txtKey3.BorderStyle = BorderStyle.FixedSingle;
        txtKey3.ForeColor = Color.FromArgb(235, 235, 240);
        txtKey3.Location = new Point(294, 210);
        txtKey3.Name = "txtKey3";
        txtKey3.PlaceholderText = "Bir tuşa basın";
        txtKey3.ReadOnly = true;
        txtKey3.Size = new Size(120, 23);
        txtKey3.TabIndex = 6;
        txtKey3.TextAlign = HorizontalAlignment.Center;
        txtKey3.KeyDown += KeyCapture_KeyDown;
        txtKey3.Enter += KeyCapture_Enter;
        //
        // txtKey4
        //
        txtKey4.BackColor = Color.FromArgb(45, 48, 58);
        txtKey4.BorderStyle = BorderStyle.FixedSingle;
        txtKey4.ForeColor = Color.FromArgb(235, 235, 240);
        txtKey4.Location = new Point(428, 210);
        txtKey4.Name = "txtKey4";
        txtKey4.PlaceholderText = "Bir tuşa basın";
        txtKey4.ReadOnly = true;
        txtKey4.Size = new Size(120, 23);
        txtKey4.TabIndex = 7;
        txtKey4.TextAlign = HorizontalAlignment.Center;
        txtKey4.KeyDown += KeyCapture_KeyDown;
        txtKey4.Enter += KeyCapture_Enter;
        //
        // lblDelay
        //
        lblDelay.AutoSize = true;
        lblDelay.ForeColor = Color.FromArgb(180, 185, 195);
        lblDelay.Location = new Point(26, 252);
        lblDelay.Name = "lblDelay";
        lblDelay.Size = new Size(95, 15);
        lblDelay.Text = "Gecikme (ms)";
        //
        // nudDelay
        //
        nudDelay.BackColor = Color.FromArgb(45, 48, 58);
        nudDelay.BorderStyle = BorderStyle.FixedSingle;
        nudDelay.ForeColor = Color.FromArgb(235, 235, 240);
        nudDelay.Location = new Point(26, 272);
        nudDelay.Maximum = new decimal(new int[] { 5000, 0, 0, 0 });
        nudDelay.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
        nudDelay.Name = "nudDelay";
        nudDelay.Size = new Size(120, 23);
        nudDelay.TabIndex = 8;
        nudDelay.Value = new decimal(new int[] { 300, 0, 0, 0 });
        //
        // chkBringFront
        //
        chkBringFront.AutoSize = true;
        chkBringFront.Checked = true;
        chkBringFront.Enabled = false;
        chkBringFront.ForeColor = Color.FromArgb(220, 222, 230);
        chkBringFront.Location = new Point(170, 274);
        chkBringFront.Name = "chkBringFront";
        chkBringFront.Size = new Size(220, 19);
        chkBringFront.TabIndex = 9;
        chkBringFront.Text = "Tarayıcıyı öne getir (zorunlu)";
        chkBringFront.UseVisualStyleBackColor = true;
        //
        // btnStart
        //
        btnStart.BackColor = Color.FromArgb(46, 160, 90);
        btnStart.FlatAppearance.BorderSize = 0;
        btnStart.FlatStyle = FlatStyle.Flat;
        btnStart.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
        btnStart.ForeColor = Color.White;
        btnStart.Location = new Point(26, 320);
        btnStart.Name = "btnStart";
        btnStart.Size = new Size(250, 52);
        btnStart.TabIndex = 10;
        btnStart.Text = "▶  Başlat";
        btnStart.UseVisualStyleBackColor = false;
        btnStart.Click += BtnStart_Click;
        //
        // btnStop
        //
        btnStop.BackColor = Color.FromArgb(190, 55, 55);
        btnStop.Enabled = false;
        btnStop.FlatAppearance.BorderSize = 0;
        btnStop.FlatStyle = FlatStyle.Flat;
        btnStop.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
        btnStop.ForeColor = Color.White;
        btnStop.Location = new Point(298, 320);
        btnStop.Name = "btnStop";
        btnStop.Size = new Size(250, 52);
        btnStop.TabIndex = 11;
        btnStop.Text = "■  Durdur";
        btnStop.UseVisualStyleBackColor = false;
        btnStop.Click += BtnStop_Click;
        //
        // btnTest
        //
        btnTest.BackColor = Color.FromArgb(55, 90, 160);
        btnTest.FlatAppearance.BorderSize = 0;
        btnTest.FlatStyle = FlatStyle.Flat;
        btnTest.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        btnTest.ForeColor = Color.White;
        btnTest.Location = new Point(26, 384);
        btnTest.Name = "btnTest";
        btnTest.Size = new Size(522, 36);
        btnTest.TabIndex = 12;
        btnTest.Text = "TETİKLE / Test Et  (gönder)";
        btnTest.UseVisualStyleBackColor = false;
        btnTest.Click += BtnTest_Click;
        //
        // pnlStatus
        //
        pnlStatus.BackColor = Color.FromArgb(40, 43, 54);
        pnlStatus.Controls.Add(lblStatusTitle);
        pnlStatus.Controls.Add(lblStatus);
        pnlStatus.Location = new Point(26, 434);
        pnlStatus.Name = "pnlStatus";
        pnlStatus.Padding = new Padding(14);
        pnlStatus.Size = new Size(522, 96);
        pnlStatus.TabIndex = 13;
        //
        // lblStatusTitle
        //
        lblStatusTitle.AutoSize = true;
        lblStatusTitle.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        lblStatusTitle.ForeColor = Color.FromArgb(150, 155, 170);
        lblStatusTitle.Location = new Point(14, 12);
        lblStatusTitle.Name = "lblStatusTitle";
        lblStatusTitle.Size = new Size(40, 15);
        lblStatusTitle.Text = "Durum";
        //
        // lblStatus
        //
        lblStatus.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        lblStatus.ForeColor = Color.FromArgb(235, 235, 240);
        lblStatus.Location = new Point(14, 34);
        lblStatus.MaximumSize = new Size(490, 52);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(490, 52);
        lblStatus.Text = "Hazır";
        //
        // _notifyIcon
        //
        _notifyIcon.Text = "Browser Key Macro";
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        //
        // _statusTimer
        //
        _statusTimer.Interval = 750;
        _statusTimer.Tick += StatusTimer_Tick;
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(28, 30, 38);
        ClientSize = new Size(574, 556);
        Controls.Add(pnlStatus);
        Controls.Add(btnTest);
        Controls.Add(btnStop);
        Controls.Add(btnStart);
        Controls.Add(chkBringFront);
        Controls.Add(nudDelay);
        Controls.Add(lblDelay);
        Controls.Add(txtKey4);
        Controls.Add(txtKey3);
        Controls.Add(txtKey2);
        Controls.Add(txtKey1);
        Controls.Add(lblSequence);
        Controls.Add(txtIntermediate);
        Controls.Add(lblIntermediate);
        Controls.Add(txtTrigger);
        Controls.Add(lblTrigger);
        Controls.Add(btnRefresh);
        Controls.Add(cmbBrowser);
        Controls.Add(lblBrowser);
        Controls.Add(lblTitle);
        Controls.Add(picLogo);
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Browser Key Macro  v1.17.0";
        FormClosing += MainForm_FormClosing;
        Load += MainForm_Load;
        Resize += MainForm_Resize;
        ((System.ComponentModel.ISupportInitialize)nudDelay).EndInit();
        ((System.ComponentModel.ISupportInitialize)picLogo).EndInit();
        pnlStatus.ResumeLayout(false);
        pnlStatus.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private PictureBox picLogo;
    private Label lblTitle;
    private Label lblBrowser;
    private ComboBox cmbBrowser;
    private Button btnRefresh;
    private Label lblTrigger;
    private TextBox txtTrigger;
    private Label lblIntermediate;
    private TextBox txtIntermediate;
    private Label lblSequence;
    private TextBox txtKey1;
    private TextBox txtKey2;
    private TextBox txtKey3;
    private TextBox txtKey4;
    private Label lblDelay;
    private NumericUpDown nudDelay;
    private CheckBox chkBringFront;
    private Button btnStart;
    private Button btnStop;
    private Button btnTest;
    private Panel pnlStatus;
    private Label lblStatusTitle;
    private Label lblStatus;
    private NotifyIcon _notifyIcon;
    private System.Windows.Forms.Timer _statusTimer;
}
