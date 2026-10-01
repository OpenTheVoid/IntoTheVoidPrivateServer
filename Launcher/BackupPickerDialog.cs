using IntoTheVoidServer.Accounts;

namespace IntoTheVoidLauncher;

public enum BackupAction
{
    None,
    Restore,
    Delete,
}

/// <summary>
/// 备份选择对话框。
/// <para>选中账号时列出该账号的备份；未选中账号时以"总览"模式列出所有账号的备份（多一列 UID）。</para>
/// </summary>
public sealed class BackupPickerDialog : Form
{
    private readonly ListView _list = new();

    public BackupInfo? Selected { get; private set; }
    public BackupAction Action { get; private set; } = BackupAction.None;

    /// <param name="title">标题里显示的账号名；总览模式传个说明性文字。</param>
    /// <param name="showUid">true = 总览模式，多显示一列 UID（还原时按它定位目标账号）。</param>
    public BackupPickerDialog(string title, List<BackupInfo> backups, bool showUid = false)
    {
        Text = $"选择备份 —— {title}";
        ClientSize = new Size(showUid ? 720 : 620, 400);
        MinimumSize = new Size(showUid ? 620 : 520, 320);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        _list.View = View.Details;
        _list.Dock = DockStyle.Fill;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.Columns.Add("备份时间", 190);
        if (showUid) _list.Columns.Add("UID", 90);
        _list.Columns.Add("备注", 110);
        _list.Columns.Add("响应数", 70, HorizontalAlignment.Right);
        _list.Columns.Add("大小", 90, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => ConfirmRestore();
        layout.Controls.Add(_list, 0, 0);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };
        var btnCancel = new Button { Text = "取消", Width = 88, Height = 32, DialogResult = DialogResult.Cancel };
        var btnRestore = new Button { Text = "还原此备份", Width = 110, Height = 32 };
        var btnDelete = new Button { Text = "删除此备份", Width = 110, Height = 32 };
        btnRestore.Click += (_, _) => ConfirmRestore();
        btnDelete.Click += (_, _) => ConfirmDelete();
        bar.Controls.Add(btnCancel);
        bar.Controls.Add(btnRestore);
        bar.Controls.Add(btnDelete);
        layout.Controls.Add(bar, 0, 1);

        Controls.Add(layout);
        CancelButton = btnCancel;

        foreach (var b in backups)
        {
            var item = new ListViewItem(b.Name) { Tag = b };
            if (showUid) item.SubItems.Add(b.Uid);
            item.SubItems.Add(b.Label);
            item.SubItems.Add(b.FileCount.ToString());
            item.SubItems.Add(FormatSize(b.SizeBytes));
            if (b.FileCount == 0) item.ForeColor = Color.FromArgb(150, 90, 0);   // 不含响应的残缺备份，标个色
            _list.Items.Add(item);
        }

        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
    }

    private BackupInfo? Pick()
    {
        if (_list.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "请先选中一个备份", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        return (BackupInfo)_list.SelectedItems[0].Tag!;
    }

    private void ConfirmRestore()
    {
        var b = Pick();
        if (b == null) return;
        var ans = MessageBox.Show(this,
            $"用备份「{b.Name}」（UID {b.Uid}，{b.FileCount} 个响应）覆盖该账号当前存档？\n\n" +
            "还原前会自动把当前存档备份为 before_restore。",
            "还原存档", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ans != DialogResult.Yes) return;
        Selected = b;
        Action = BackupAction.Restore;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ConfirmDelete()
    {
        var b = Pick();
        if (b == null) return;
        var ans = MessageBox.Show(this,
            $"确定删除备份「{b.Name}」（UID {b.Uid}）吗？此操作不可撤销。",
            "删除备份", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (ans != DialogResult.Yes) return;
        Selected = b;
        Action = BackupAction.Delete;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F2} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B",
    };
}
