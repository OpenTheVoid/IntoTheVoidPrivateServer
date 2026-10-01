using IntoTheVoidServer.Accounts;

namespace IntoTheVoidLauncher;

/// <summary>新建账号时初始存档的来源。</summary>
public enum SaveSource
{
    /// <summary>全新空档：没有捕获响应，客户端请求全部走服务端默认响应。</summary>
    Blank,

    /// <summary>沿用原存档（uid 34184063）：账号直接绑定这份存档，原样保留、不复制不改动。</summary>
    AdoptLegacy,

    /// <summary>复制原档到新 uid：原档不动，新账号拿到一份独立的进度副本。</summary>
    CopyLegacy,
}

/// <summary>新建账号对话框：账号 + 密码 + 初始存档来源。</summary>
public sealed class NewAccountDialog : Form
{
    private readonly TextBox _txtUser = new();
    private readonly TextBox _txtPwd = new();
    private readonly TextBox _txtPwd2 = new();
    private readonly ComboBox _cmbSource = new();
    private readonly Label _lblHint = new();
    private bool _fixingUsername;

    public string Username => _txtUser.Text.Trim();
    public string Password => _txtPwd.Text;

    public SaveSource Source => _cmbSource.SelectedIndex switch
    {
        1 => SaveSource.AdoptLegacy,
        2 => SaveSource.CopyLegacy,
        _ => SaveSource.Blank,
    };

    public NewAccountDialog(int legacyResponseCount)
    {
        var hasLegacy = legacyResponseCount > 0;

        Text = "新建账号";
        ClientSize = new Size(470, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(18, 16, 18, 12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(MakeLabel("账号"), 0, 0);
        _txtUser.Dock = DockStyle.Fill;
        _txtUser.Margin = new Padding(0, 6, 0, 6);
        // 账号只允许数字：游戏内账号框是手机号输入框，字母敲不进去，
        // 所以这里直接把非数字输入挡掉（含粘贴），避免建出游戏里输不进来的账号。
        _txtUser.MaxLength = AccountStore.UsernameMaxLen;
        _txtUser.PlaceholderText = $"{AccountStore.UsernameMinLen}-{AccountStore.UsernameMaxLen} 位数字";
        _txtUser.KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar)) return;              // 退格/删除等放行
            if (e.KeyChar < '0' || e.KeyChar > '9') e.Handled = true;
        };
        _txtUser.TextChanged += (_, _) => SanitizeUsername();
        layout.Controls.Add(_txtUser, 1, 0);

        layout.Controls.Add(MakeLabel("密码"), 0, 1);
        _txtPwd.Dock = DockStyle.Fill;
        _txtPwd.Margin = new Padding(0, 6, 0, 6);
        _txtPwd.UseSystemPasswordChar = true;
        _txtPwd.PlaceholderText = "6-32 位，仅英文/数字/符号";
        layout.Controls.Add(_txtPwd, 1, 1);

        layout.Controls.Add(MakeLabel("确认密码"), 0, 2);
        _txtPwd2.Dock = DockStyle.Fill;
        _txtPwd2.Margin = new Padding(0, 6, 0, 6);
        _txtPwd2.UseSystemPasswordChar = true;
        layout.Controls.Add(_txtPwd2, 1, 2);

        layout.Controls.Add(MakeLabel("初始存档"), 0, 3);
        _cmbSource.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSource.Dock = DockStyle.Fill;
        _cmbSource.Margin = new Padding(0, 6, 0, 6);
        if (hasLegacy)
        {
            _cmbSource.Items.Add($"沿用原存档（UID {PlayerSaveStore.LegacyUid}，{legacyResponseCount} 项数据，原样保留）");
            _cmbSource.Items.Add($"复制原档到新 UID（原档不动，新账号拿一份独立副本）");
        }
        _cmbSource.Items.Add("全新空档（从零开始）");
        // 默认：有原档就沿用原档，否则全新空档
        _cmbSource.SelectedIndex = hasLegacy ? 0 : _cmbSource.Items.Count - 1;
        _cmbSource.SelectedIndexChanged += (_, _) => UpdateHint();
        layout.Controls.Add(_cmbSource, 1, 3);

        _lblHint.Dock = DockStyle.Fill;
        _lblHint.ForeColor = Color.FromArgb(110, 116, 130);
        layout.Controls.Add(_lblHint, 0, 4);
        layout.SetColumnSpan(_lblHint, 2);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };
        var btnCancel = new Button { Text = "取消", Width = 88, Height = 32, DialogResult = DialogResult.Cancel };
        var btnOk = new Button { Text = "创建", Width = 88, Height = 32 };
        btnOk.Click += (_, _) => OnOk();
        bar.Controls.Add(btnCancel);
        bar.Controls.Add(btnOk);
        layout.Controls.Add(bar, 0, 5);
        layout.SetColumnSpan(bar, 2);

        Controls.Add(layout);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        UpdateHint();
    }

    private void UpdateHint()
    {
        _lblHint.Text = Source switch
        {
            SaveSource.AdoptLegacy =>
                "推荐：账号直接绑定原存档，进游戏就是你现在的进度。\n原档不被复制也不被清空；随时可在主界面『备份存档』留底。",
            SaveSource.CopyLegacy =>
                $"把原档复制一份到新 UID。原档 {PlayerSaveStore.LegacyUid} 保持不动，\n新账号从这份进度开始独立发展。",
            _ =>
                "全新空档：从零开始（没有捕获响应，任务/背包走服务端默认值）。\n每个账号都有独立存档，互不影响。",
        };
    }

    /// <summary>
    /// 兜底清洗：把非数字字符滤掉（覆盖输入法、粘贴等绕过 KeyPress 的途径），并截断到最大长度。
    /// </summary>
    private void SanitizeUsername()
    {
        if (_fixingUsername) return;

        var raw = _txtUser.Text;
        var clean = new string(raw.Where(c => c >= '0' && c <= '9').ToArray());
        if (clean.Length > AccountStore.UsernameMaxLen)
            clean = clean.Substring(0, AccountStore.UsernameMaxLen);
        if (clean == raw) return;

        _fixingUsername = true;
        var caret = _txtUser.SelectionStart;
        try
        {
            _txtUser.Text = clean;
            _txtUser.SelectionStart = Math.Min(caret, clean.Length);
        }
        finally
        {
            _fixingUsername = false;
        }
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 10, 0, 0),
    };

    private void OnOk()
    {
        if (!AccountStore.IsValidUsername(Username))
        {
            MessageBox.Show(this, AccountStore.UsernameRuleText, "新建账号",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtUser.Focus();
            return;
        }
        if (!AccountStore.IsValidPassword(Password))
        {
            MessageBox.Show(this, "密码需为 6-32 位可见 ASCII 字符（不含空格和中文）", "新建账号",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtPwd.Focus();
            return;
        }
        if (Password != _txtPwd2.Text)
        {
            MessageBox.Show(this, "两次输入的密码不一致", "新建账号",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtPwd2.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
