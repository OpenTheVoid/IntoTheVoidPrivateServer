using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using IntoTheVoidServer.Accounts;

namespace IntoTheVoidLauncher;

public sealed class MainForm : Form
{
    private const string GameExe = "IntoTheVoid.exe";
    private const string ServerExe = "IntoTheVoidServer.exe";
    private const string ServerProcessName = "IntoTheVoidServer";
    private const int GamePort = 30531;

    private readonly TextBox _serverRootBox = new();
    private readonly Button _btnBrowse = new() { Text = "浏览..." };
    private readonly ListView _list = new();
    private readonly Label _status = new();
    private readonly TextBox _log = new();

    private readonly Button _btnNew = new() { Text = "新建账号" };
    private readonly Button _btnDelete = new() { Text = "删除账号" };
    private readonly Button _btnBackup = new() { Text = "备份存档" };
    private readonly Button _btnRestore = new() { Text = "还原存档" };
    private readonly Button _btnOpenSave = new() { Text = "打开存档目录" };
    private readonly Button _btnOpenBackups = new() { Text = "打开备份目录" };

    private readonly Button _btnStartServer = new() { Text = "启动服务端" };
    private readonly Button _btnStopServer = new() { Text = "停止服务端" };
    private readonly Button _btnRefresh = new() { Text = "刷新" };
    private readonly Button _btnLaunch = new() { Text = "▶  启动游戏（自动拉起服务端）" };

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2500 };
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "launcher_settings.json");

    private string _serverRoot = "";
    private string _gameRoot = "";
    private Process? _serverProcess;

    public MainForm()
    {
        Text = "驱入虚空 · 登录器";
        ClientSize = new Size(1040, 700);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(246, 247, 250);

        BuildLayout();
        WireEvents();

        Load += (_, _) => OnLoaded();
        _timer.Tick += (_, _) => UpdateServerStatus();
    }

    // ==================================================================
    // 界面
    // ==================================================================

    private void BuildLayout()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));

        // ---------- 第一行：服务端目录 ----------
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblRoot = new Label
        {
            Text = "服务端目录：",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 4, 0),
        };
        _serverRootBox.Dock = DockStyle.Fill;
        _serverRootBox.Margin = new Padding(4, 6, 8, 6);
        _serverRootBox.PlaceholderText = "…\\IntoTheVoidServer\\publish（含 IntoTheVoidServer.exe 的目录）";
        _btnBrowse.AutoSize = true;
        _btnBrowse.Height = 30;
        _btnBrowse.Margin = new Padding(0, 6, 0, 6);

        top.Controls.Add(lblRoot, 0, 0);
        top.Controls.Add(_serverRootBox, 1, 0);
        top.Controls.Add(_btnBrowse, 2, 0);

        // ---------- 第二行：账号列表 + 操作按钮 ----------
        var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));

        _list.View = View.Details;
        _list.Dock = DockStyle.Fill;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.Margin = new Padding(0, 4, 8, 4);
        _list.Columns.Add("账号", 150);
        _list.Columns.Add("UID", 100);
        _list.Columns.Add("响应数", 70, HorizontalAlignment.Right);
        _list.Columns.Add("存档大小", 90, HorizontalAlignment.Right);
        _list.Columns.Add("创建时间", 150);
        _list.Columns.Add("最后登录", 150);

        var rightBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 4),
            Padding = new Padding(0),
        };
        foreach (var b in new[] { _btnNew, _btnDelete, _btnBackup, _btnRestore, _btnOpenSave, _btnOpenBackups })
        {
            b.Width = 146;
            b.Height = 32;
            b.Margin = new Padding(0, 0, 0, 8);
            rightBar.Controls.Add(b);
        }

        mid.Controls.Add(_list, 0, 0);
        mid.Controls.Add(rightBar, 1, 0);

        // ---------- 第三行：状态 + 服务端按钮 + 日志 ----------
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _status.Text = "就绪";
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.FromArgb(90, 96, 110);
        _status.Margin = new Padding(2, 0, 0, 0);

        var btnBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        _btnStartServer.Width = 110; _btnStartServer.Height = 32;
        _btnStopServer.Width = 110; _btnStopServer.Height = 32;
        _btnRefresh.Width = 80; _btnRefresh.Height = 32;
        _btnLaunch.Width = 320; _btnLaunch.Height = 34;
        _btnLaunch.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        _btnLaunch.BackColor = Color.FromArgb(46, 116, 230);
        _btnLaunch.ForeColor = Color.White;
        _btnLaunch.FlatStyle = FlatStyle.Flat;
        _btnLaunch.FlatAppearance.BorderSize = 0;
        foreach (var b in new Control[] { _btnStartServer, _btnStopServer, _btnRefresh, _btnLaunch })
        {
            b.Margin = new Padding(0, 4, 10, 0);
            btnBar.Controls.Add(b);
        }

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.FromArgb(30, 32, 38);
        _log.ForeColor = Color.FromArgb(200, 208, 220);
        _log.Font = new Font("Consolas", 8.5F);
        _log.Margin = new Padding(0, 4, 0, 0);

        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(btnBar, 0, 1);
        bottom.Controls.Add(_log, 0, 2);

        grid.Controls.Add(top, 0, 0);
        grid.Controls.Add(mid, 0, 1);
        grid.Controls.Add(bottom, 0, 2);
        Controls.Add(grid);
    }

    private void WireEvents()
    {
        _btnBrowse.Click += (_, _) => OnBrowse();
        _serverRootBox.Leave += (_, _) => OnServerRootChanged();
        _btnNew.Click += (_, _) => OnNewAccount();
        _btnDelete.Click += (_, _) => OnDeleteAccount();
        _btnBackup.Click += (_, _) => OnBackup();
        _btnRestore.Click += (_, _) => OnRestore();
        _btnOpenSave.Click += (_, _) => OpenFolder(CurrentSaveDir());
        _btnOpenBackups.Click += (_, _) => OpenFolder(CurrentBackupDir());
        _btnStartServer.Click += (_, _) => StartServer();
        _btnStopServer.Click += (_, _) => StopServer();
        _btnRefresh.Click += (_, _) => { InitializeStores(); RefreshAccounts(); UpdateServerStatus(); };
        _btnLaunch.Click += async (_, _) => await OnLaunchAsync();
        _list.DoubleClick += (_, _) => OnBackup();
        FormClosed += (_, _) => SaveSettings();
    }

    // ==================================================================
    // 生命周期
    // ==================================================================

    private void OnLoaded()
    {
        AccountStore.Log = AppendLog;
        PlayerSaveStore.Log = AppendLog;

        _gameRoot = DetectGameRoot() ?? "";

        var savedRoot = LoadSettings()?.ServerRoot;
        _serverRoot = !string.IsNullOrEmpty(savedRoot) && Directory.Exists(savedRoot)
            ? savedRoot!
            : (DetectServerRoot(_gameRoot) ?? "");
        _serverRootBox.Text = _serverRoot;

        AppendLog($"游戏根目录：{(_gameRoot.Length > 0 ? _gameRoot : "未识别")}");
        AppendLog($"服务端目录：{(_serverRoot.Length > 0 ? _serverRoot : "未识别，请点『浏览...』选择")}");

        InitializeStores();
        RefreshAccounts();
        UpdateServerStatus();
        _timer.Start();
    }

    private void OnBrowse()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "选择服务端目录（内含 IntoTheVoidServer.exe 的目录，通常是 IntoTheVoidServer\\publish）",
            SelectedPath = Directory.Exists(_serverRoot) ? _serverRoot : _gameRoot,
            ShowNewFolderButton = false,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _serverRootBox.Text = dlg.SelectedPath;
            OnServerRootChanged();
        }
    }

    private void OnServerRootChanged()
    {
        var root = _serverRootBox.Text.Trim();
        if (!File.Exists(Path.Combine(root, ServerExe)))
        {
            if (Directory.Exists(root))
                AppendLog($"警告：{root} 下没找到 {ServerExe}，请确认选的是 publish 目录");
        }
        _serverRoot = root;
        SaveSettings();
        InitializeStores();
        RefreshAccounts();
    }

    private void InitializeStores()
    {
        if (string.IsNullOrEmpty(_serverRoot) || !Directory.Exists(_serverRoot)) return;
        try
        {
            PlayerSaveStore.MigrateLegacySave(_serverRoot);
            AccountStore.Initialize(_serverRoot);
        }
        catch (Exception ex)
        {
            AppendLog($"初始化存档/账号库失败：{ex.Message}");
        }
    }

    // ==================================================================
    // 账号列表
    // ==================================================================

    private string CurrentUid =>
        _list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is AccountRecord rec ? rec.Uid : "";

    private string CurrentSaveDir() =>
        string.IsNullOrEmpty(CurrentUid) ? SaveRootPath() : PlayerSaveStore.SaveDirOf(_serverRoot, CurrentUid);

    private string CurrentBackupDir() =>
        string.IsNullOrEmpty(CurrentUid)
            ? Path.Combine(_serverRoot, "Data", "backups")
            : Path.Combine(_serverRoot, "Data", "backups", CurrentUid);

    private string SaveRootPath() => Path.Combine(_serverRoot, "Data", "saves");

    private void RefreshAccounts()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        if (string.IsNullOrEmpty(_serverRoot) || !Directory.Exists(_serverRoot))
        {
            _list.EndUpdate();
            return;
        }

        foreach (var acc in AccountStore.Snapshot().OrderBy(a => a.Uid, StringComparer.Ordinal))
        {
            var item = new ListViewItem(acc.Username) { Tag = acc };
            item.SubItems.Add(acc.Uid);
            item.SubItems.Add(PlayerSaveStore.GetResponseCount(_serverRoot, acc.Uid).ToString());
            item.SubItems.Add(FormatSize(PlayerSaveStore.GetSize(_serverRoot, acc.Uid)));
            item.SubItems.Add(acc.CreatedAt);
            item.SubItems.Add(acc.LastLoginAt);
            _list.Items.Add(item);
        }

        _list.EndUpdate();
        if (_list.Items.Count == 0)
            AppendLog("账号列表为空 —— 点右上『新建账号』创建；想继续用原来的存档，新建时选「沿用原存档」。");
        else
            AppendLog($"账号列表已刷新：共 {_list.Items.Count} 个账号");
    }

    private void OnNewAccount()
    {
        if (!EnsureServerRoot()) return;

        var legacyDir = PlayerSaveStore.ResponsesDirOf(_serverRoot, PlayerSaveStore.LegacyUid);
        var legacyCount = Directory.Exists(legacyDir)
            ? Directory.GetFiles(legacyDir, "*.bin").Length
            : 0;

        using var dlg = new NewAccountDialog(legacyCount);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var template = dlg.Source == SaveSource.CopyLegacy && legacyCount > 0 ? legacyDir : null;
        var preferredUid = dlg.Source == SaveSource.AdoptLegacy && legacyCount > 0
            ? PlayerSaveStore.LegacyUid
            : null;
        var rec = AccountStore.Create(dlg.Username, dlg.Password, template, preferredUid, out var err);
        if (rec == null)
        {
            MessageBox.Show(this, err ?? "创建失败", "新建账号", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var sourceText = preferredUid != null
            ? $"沿用原存档（UID {rec.Uid}，原样保留）"
            : (template == null ? "全新空档" : $"复制原档（{legacyCount} 项）");
        RefreshAccounts();
        MessageBox.Show(this,
            $"账号创建成功！\n\n账号：{rec.Username}\n密码：（你刚才设置的）\nUID：{rec.Uid}\n初始存档：{sourceText}\n\n" +
            "现在启动游戏，在登录界面切到「账号密码登录」输入即可。",
            "新建账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnDeleteAccount()
    {
        if (_list.SelectedItems.Count == 0) { Warn("请先在列表中选中一个账号"); return; }
        var rec = (AccountRecord)_list.SelectedItems[0].Tag!;

        var isLegacy = rec.Uid == PlayerSaveStore.LegacyUid;
        var ans = MessageBox.Show(this,
            isLegacy
                ? $"确定删除账号「{rec.Username}」(UID {rec.Uid}) 吗？\n\n" +
                  $"注意：这是原档 UID，存档目录 Data\\saves\\{rec.Uid} 会被保留（不会删除），只是不再有账号指向它。"
                : $"确定删除账号「{rec.Username}」(UID {rec.Uid}) 吗？\n\n" +
                  "存档目录也会一并删除。删除前建议先点『备份存档』。",
            "删除账号", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (ans != DialogResult.Yes) return;

        if (!AccountStore.Delete(rec.Username, deleteSave: !isLegacy, out var err))
        {
            MessageBox.Show(this, err ?? "删除失败", "删除账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        AppendLog($"已删除账号 {rec.Username}" + (isLegacy ? "（原档存档已保留）" : ""));
        RefreshAccounts();
    }

    private void OnBackup()
    {
        if (!EnsureServerRoot()) return;
        if (string.IsNullOrEmpty(CurrentUid))
        {
            Warn("请先在列表中选中一个账号。\n\n备份的是该账号当前的存档；" +
                 "只想查看/还原已有备份的话，不选账号直接点『还原存档』即可。");
            return;
        }

        var rec = (AccountRecord)_list.SelectedItems[0].Tag!;
        var dir = PlayerSaveStore.Backup(_serverRoot, rec.Uid, "manual");
        if (dir == null) { Warn("备份失败：存档目录不存在"); return; }

        AppendLog($"已备份 {rec.Username} -> {dir}");
        MessageBox.Show(this, $"备份完成\n\n{dir}", "备份存档", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshAccounts();
    }

    private void OnRestore()
    {
        if (!EnsureServerRoot()) return;

        var selectedUid = CurrentUid;
        List<BackupInfo> backups;
        string title;
        bool overview;

        if (string.IsNullOrEmpty(selectedUid))
        {
            // 没选中账号 → 列出所有账号的备份（刚装好还没建号时，也能看到并还原历史备份）
            backups = PlayerSaveStore.ListAllBackups(_serverRoot);
            title = "全部账号";
            overview = true;
            if (backups.Count == 0)
            {
                Warn("还没有任何备份。\n\n请先在列表中选中账号，再点『备份存档』。");
                return;
            }
        }
        else
        {
            var rec = (AccountRecord)_list.SelectedItems[0].Tag!;
            backups = PlayerSaveStore.ListBackups(_serverRoot, rec.Uid);
            title = rec.Username;
            overview = false;
            if (backups.Count == 0) { Warn($"账号「{rec.Username}」还没有任何备份"); return; }
        }

        using var dlg = new BackupPickerDialog(title, backups, overview);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Selected == null) return;

        var target = dlg.Selected;

        if (dlg.Action == BackupAction.Delete)
        {
            if (PlayerSaveStore.DeleteBackup(target.Dir, out var derr))
            {
                AppendLog($"已删除备份 {target.Name}（UID {target.Uid}）");
                MessageBox.Show(this, "备份已删除", "还原存档", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, derr ?? "删除失败", "还原存档", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        if (!PlayerSaveStore.Restore(_serverRoot, target.Uid, target.Dir, out var err))
        {
            MessageBox.Show(this, err ?? "还原失败", "还原存档", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        AppendLog($"已从备份还原 UID {target.Uid}：{target.Name}");
        MessageBox.Show(this,
            $"还原完成（UID {target.Uid}）。\n\n还原前的存档已自动备份为 before_restore。\n若游戏正在运行，请重启游戏使其重新载入存档。",
            "还原存档", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshAccounts();
    }

    // ==================================================================
    // 服务端 / 游戏
    // ==================================================================

    private bool EnsureServerRoot()
    {
        if (string.IsNullOrEmpty(_serverRoot) || !Directory.Exists(_serverRoot))
        {
            Warn("请先设置正确的服务端目录（点右上角『浏览...』）");
            return false;
        }
        return true;
    }

    private void StartServer()
    {
        if (!EnsureServerRoot()) return;
        if (IsPortListening(GamePort)) { AppendLog("服务端已在运行（端口 30531 已监听）"); return; }

        var exe = Path.Combine(_serverRoot, ServerExe);
        if (!File.Exists(exe)) { Warn($"找不到 {exe}"); return; }

        try
        {
            _serverProcess = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = _serverRoot,
                UseShellExecute = true,
            });
            AppendLog($"已启动服务端：{exe}");
            _status.Text = "服务端启动中…（首次启动约需 2-4 秒）";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"启动服务端失败：{ex.Message}", "启动服务端", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopServer()
    {
        var killed = 0;
        foreach (var p in Process.GetProcessesByName(ServerProcessName))
        {
            try { p.Kill(entireProcessTree: true); killed++; } catch { /* 可能已被关闭 */ }
        }
        try { if (_serverProcess is { HasExited: false }) _serverProcess.Kill(true); } catch { }
        AppendLog(killed > 0 ? $"已停止服务端进程（{killed} 个）" : "没有找到正在运行的服务端进程");
        _serverProcess = null;
        UpdateServerStatus();
    }

    private async Task OnLaunchAsync()
    {
        if (!EnsureServerRoot()) return;
        if (string.IsNullOrEmpty(_gameRoot) || !File.Exists(Path.Combine(_gameRoot, GameExe)))
        {
            Warn($"找不到游戏主程序 {GameExe}，无法启动");
            return;
        }

        if (!IsPortListening(GamePort))
        {
            StartServer();
            _btnLaunch.Enabled = false;
            var ok = await WaitForPortAsync(20000);
            _btnLaunch.Enabled = true;
            if (!ok)
            {
                Warn("服务端 20 秒内没有就绪，请查看服务端窗口的报错。游戏仍会尝试启动。");
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(_gameRoot, GameExe),
                WorkingDirectory = _gameRoot,
                UseShellExecute = true,
            });
            AppendLog("已启动游戏客户端");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"启动游戏失败：{ex.Message}", "启动游戏", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static async Task<bool> WaitForPortAsync(int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (IsPortListening(GamePort)) return true;
            await Task.Delay(400);
        }
        return IsPortListening(GamePort);
    }

    private void UpdateServerStatus()
    {
        var tcpUp = IsPortListening(GamePort);
        var procUp = Process.GetProcessesByName(ServerProcessName).Length > 0;
        var running = tcpUp || procUp;

        _btnStartServer.Enabled = !running;
        _btnStopServer.Enabled = running;

        var gameOk = !string.IsNullOrEmpty(_gameRoot) && File.Exists(Path.Combine(_gameRoot, GameExe));
        _btnLaunch.Enabled = gameOk;

        var accounts = _list.Items.Count;
        _status.Text = running
            ? $"● 服务端运行中（端口 {GamePort}{(tcpUp ? "，已监听" : "，进程在但端口未监听")}）   |   账号 {accounts} 个   |   存档根 {SaveRootPath()}"
            : $"○ 服务端未运行   |   账号 {accounts} 个   |   存档根 {SaveRootPath()}";
        _status.ForeColor = running ? Color.FromArgb(22, 140, 70) : Color.FromArgb(90, 96, 110);
    }

    private static bool IsPortListening(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(ep => ep.Port == port);
        }
        catch { return false; }
    }

    // ==================================================================
    // 工具
    // ==================================================================

    private static string? DetectGameRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, GameExe))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? DetectServerRoot(string? gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot)) return null;
        var candidates = new[]
        {
            Path.Combine(gameRoot, "IntoTheVoidServer", "publish"),
            Path.Combine(gameRoot, "IntoTheVoidServer"),
        };
        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, ServerExe)));
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F2} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B",
    };

    private void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Warn($"打开目录失败：{ex.Message}");
        }
    }

    private void Warn(string msg) =>
        MessageBox.Show(this, msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void AppendLog(string msg)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(new Action<string>(AppendLog), msg); } catch { } return; }

        if (_log.Lines.Length > 400)
        {
            var keep = _log.Lines.Skip(_log.Lines.Length - 250).ToArray();
            _log.Lines = keep;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }

    private sealed class LauncherSettings
    {
        public string? ServerRoot { get; set; }
    }

    private LauncherSettings? LoadSettings()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_settingsPath))
                : new LauncherSettings();
        }
        catch { return new LauncherSettings(); }
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(new LauncherSettings { ServerRoot = _serverRootBox.Text.Trim() });
            File.WriteAllText(_settingsPath, json);
        }
        catch { /* 配置保存失败不影响使用 */ }
    }
}
