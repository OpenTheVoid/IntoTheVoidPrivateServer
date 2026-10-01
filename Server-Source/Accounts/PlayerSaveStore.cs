using System.IO.Compression;
using System.Text;

namespace IntoTheVoidServer.Accounts;

public sealed class BackupInfo
{
    public string Dir { get; set; } = "";
    /// <summary>该备份属于哪个账号（UID）。登录器"全部备份总览"靠它定位还原目标。</summary>
    public string Uid { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>人类可读备注（新式取 <c>时间戳_备注</c> 的备注段；旧式命名无法解析时为固定说明）。</summary>
    public string Label { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
}

/// <summary>
/// 每账号独立存档的管理（服务端 / 登录器共用；纯 .NET）。
///
/// 目录布局（root = IntoTheVoidServer 内容根目录）：
/// <code>
///   Data/
///     accounts.json                 账号库
///     saves/&lt;uid&gt;/
///       responses/*.bin             捕获响应（玩家背包/任务/等级……）
///       responses/pushes/*.bin      登录时的推送
///       gamestate.json              货币等可变状态
///     backups/&lt;uid&gt;/&lt;时间戳&gt;_&lt;备注&gt;/  存档备份
///     responses/                    升级前的全局存档（仅作模板/迁移来源，保留不动）
/// </code>
///
/// 升级前的全局存档会在首次启动时迁移到 <c>saves/34184063</c>，作为"原档"保留，
/// 可被登录器用作新建账号的模板。
/// </summary>
public static class PlayerSaveStore
{
    /// <summary>升级前那份全局存档对应的 uid。</summary>
    public const string LegacyUid = "34184063";

    public static Action<string>? Log;

    /// <summary>新账号默认货币（与服务端 GameState.InitializeDefaults 一致）。</summary>
    public const string DefaultGameStateJson = "{\r\n  \"1\": 999999,\r\n  \"2\": 0,\r\n  \"14\": 0,\r\n  \"21\": 0\r\n}";

    private static void L(string msg) => Log?.Invoke(msg);

    public static string SavesRootOf(string root) => Path.Combine(root, "Data", "saves");
    public static string BackupsRootOf(string root) => Path.Combine(root, "Data", "backups");
    public static string SaveDirOf(string root, string uid) => Path.Combine(SavesRootOf(root), uid);
    public static string ResponsesDirOf(string root, string uid) => Path.Combine(SaveDirOf(root, uid), "responses");
    public static string GameStateFileOf(string root, string uid) => Path.Combine(SaveDirOf(root, uid), "gamestate.json");

    public static bool Exists(string root, string uid) => Directory.Exists(SaveDirOf(root, uid));

    /// <summary>
    /// 创建账号存档目录。
    /// </summary>
    /// <param name="templateResponsesDir">
    /// 非空 → 复制该目录下的 *.bin 作为初始存档（继承进度）；为空 → 全新空档（无捕获响应，
    /// 客户端请求全部走服务端默认响应）。
    /// </param>
    public static void CreateSave(string root, string uid, string? templateResponsesDir)
    {
        var dir = SaveDirOf(root, uid);
        var respDir = ResponsesDirOf(root, uid);
        Directory.CreateDirectory(respDir);

        if (!string.IsNullOrEmpty(templateResponsesDir) && Directory.Exists(templateResponsesDir))
        {
            CopyResponses(templateResponsesDir, respDir);
            L($"[Save] uid={uid} 已继承模板存档 {templateResponsesDir}");
        }
        else
        {
            L($"[Save] uid={uid} 使用全新空档");
        }

        var gs = GameStateFileOf(root, uid);
        if (!File.Exists(gs))
            File.WriteAllText(gs, DefaultGameStateJson, new UTF8Encoding(false));
    }

    /// <summary>
    /// 确保存档目录存在（用于"沿用既有存档"，如原档 uid 34184063）。
    /// 与 <see cref="CreateSave"/> 不同：**绝不覆盖或清空**已存在的响应文件，只补目录与 gamestate.json。
    /// </summary>
    public static void EnsureSave(string root, string uid)
    {
        Directory.CreateDirectory(ResponsesDirOf(root, uid));

        var gs = GameStateFileOf(root, uid);
        if (!File.Exists(gs))
            File.WriteAllText(gs, DefaultGameStateJson, new UTF8Encoding(false));

        L($"[Save] uid={uid} 沿用既有存档（未改动，{GetResponseCount(root, uid)} 项数据）");
    }

    private static void CopyResponses(string from, string to)    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from, "*.bin", SearchOption.TopDirectoryOnly))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);

        var fromPushes = Path.Combine(from, "pushes");
        if (Directory.Exists(fromPushes))
        {
            var toPushes = Path.Combine(to, "pushes");
            Directory.CreateDirectory(toPushes);
            foreach (var f in Directory.GetFiles(fromPushes, "*.bin", SearchOption.TopDirectoryOnly))
                File.Copy(f, Path.Combine(toPushes, Path.GetFileName(f)), overwrite: true);
        }
    }

    public static void DeleteSave(string root, string uid)
    {
        var dir = SaveDirOf(root, uid);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public static long GetSize(string root, string uid)
    {
        var dir = SaveDirOf(root, uid);
        if (!Directory.Exists(dir)) return 0;
        try
        {
            return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    public static int GetResponseCount(string root, string uid)
    {
        var dir = ResponsesDirOf(root, uid);
        if (!Directory.Exists(dir)) return 0;
        try { return Directory.GetFiles(dir, "*.bin", SearchOption.TopDirectoryOnly).Length; }
        catch { return 0; }
    }

    // ------------------------------------------------------------------
    // 备份 / 还原
    // ------------------------------------------------------------------

    /// <summary>把某账号的存档复制一份到 backups/&lt;uid&gt;/&lt;时间戳&gt;_&lt;备注&gt;/。返回备份目录。</summary>
    public static string? Backup(string root, string uid, string? label = null)
    {
        var src = SaveDirOf(root, uid);
        if (!Directory.Exists(src)) return null;

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeLabel = string.IsNullOrWhiteSpace(label) ? "manual" : Sanitize(label!);
        var dest = Path.Combine(BackupsRootOf(root), uid, $"{stamp}_{safeLabel}");
        Directory.CreateDirectory(dest);

        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, overwrite: true);
        }

        File.WriteAllText(Path.Combine(dest, "_backup_info.txt"),
            $"uid={uid}\r\ncreated={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nlabel={safeLabel}\r\n", new UTF8Encoding(false));

        L($"[Save] uid={uid} 已备份 -> {dest}");
        return dest;
    }

    /// <summary>
    /// 列出某账号的所有备份。同时兼容两种存放方式：
    /// <list type="bullet">
    /// <item>新式：<c>backups/&lt;uid&gt;/&lt;时间戳&gt;_&lt;备注&gt;/</c>（<see cref="Backup"/> 生成）</item>
    /// <item>旧式：<c>backups/account_&lt;uid&gt;_&lt;时间戳&gt;/</c> 或 <c>backups/&lt;uid&gt;_&lt;时间戳&gt;/</c>
    /// （早期手工备份，扁平放在 backups 根下）</item>
    /// </list>
    /// 旧式命名只做识别、不迁移不改名，避免动到用户已有的历史备份。
    /// </summary>
    public static List<BackupInfo> ListBackups(string root, string uid)
    {
        var result = new List<BackupInfo>();
        var backupsRoot = BackupsRootOf(root);

        // 新式：backups/<uid>/<时间戳>_<备注>/
        var dir = Path.Combine(backupsRoot, uid);
        foreach (var d in SafeGetDirectories(dir))
            result.Add(MakeBackupInfo(d, uid, ParseLabel(Path.GetFileName(d))));

        // 旧式：backups/account_<uid>_<时间戳>/ 之类扁平目录
        foreach (var d in SafeGetDirectories(backupsRoot))
        {
            var name = Path.GetFileName(d);
            if (string.Equals(name, uid, StringComparison.OrdinalIgnoreCase)) continue;   // 新式容器自身
            if (!TryParseLegacyBackupName(name, out var legacyUid, out var legacyLabel)) continue;
            if (!string.Equals(legacyUid, uid, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(MakeBackupInfo(d, legacyUid, legacyLabel));
        }

        return result.OrderByDescending(b => b.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 列出 <c>backups/</c> 下<b>所有</b>账号的全部备份（未选中账号时的"总览"用）。
    /// 每条都带 <see cref="BackupInfo.Uid"/>，可直接作为还原目标。
    /// </summary>
    public static List<BackupInfo> ListAllBackups(string root)
    {
        var result = new List<BackupInfo>();
        var backupsRoot = BackupsRootOf(root);

        foreach (var d in SafeGetDirectories(backupsRoot))
        {
            var name = Path.GetFileName(d);

            if (IsUidName(name))
            {
                // 新式容器：backups/<uid>/<时间戳>_<备注>/
                foreach (var sub in SafeGetDirectories(d))
                    result.Add(MakeBackupInfo(sub, name, ParseLabel(Path.GetFileName(sub))));
                continue;
            }

            // 旧式扁平备份
            if (TryParseLegacyBackupName(name, out var uid, out var label))
                result.Add(MakeBackupInfo(d, uid, label));
        }

        return result.OrderByDescending(b => b.Name, StringComparer.Ordinal).ToList();
    }

    private static BackupInfo MakeBackupInfo(string dir, string uid, string label)
    {
        string[] files;
        try { files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories); }
        catch { files = Array.Empty<string>(); }

        return new BackupInfo
        {
            Dir = dir,
            Uid = uid,
            Name = Path.GetFileName(dir),
            Label = label,
            CreatedAt = Directory.GetCreationTime(dir).ToString("yyyy-MM-dd HH:mm:ss"),
            SizeBytes = files.Sum(f => new FileInfo(f).Length),
            FileCount = files.Count(f => f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)),
        };
    }

    private static IEnumerable<string> SafeGetDirectories(string dir)
    {
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        try { return Directory.GetDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>"20260926_134355_manual" → "manual"（无备注段时给个默认说明）。</summary>
    private static string ParseLabel(string backupDirName)
    {
        var parts = backupDirName.Split('_', 3);
        var label = parts.Length >= 3 ? parts[2] : "";
        return string.IsNullOrWhiteSpace(label) ? "手动备份" : label;
    }

    private static bool IsUidName(string name) => name.Length >= 6 && name.All(char.IsDigit);

    /// <summary>识别早期手工备份命名：<c>account_&lt;uid&gt;_&lt;时间戳&gt;[_备注]</c> 或 <c>&lt;uid&gt;_&lt;时间戳&gt;[_备注]</c>。</summary>
    private static bool TryParseLegacyBackupName(string name, out string uid, out string label)
    {
        uid = "";
        label = "";
        var m = LegacyBackupNameRe.Match(name);
        if (!m.Success) return false;

        uid = m.Groups["uid"].Value;
        label = m.Groups["label"].Success && m.Groups["label"].Value.Length > 0
            ? m.Groups["label"].Value
            : "历史备份（旧命名）";
        return true;
    }

    private static readonly System.Text.RegularExpressions.Regex LegacyBackupNameRe =
        new(@"^(?:account_)?(?<uid>\d{6,})_(?<stamp>\d{8}[-_]\d{6})(?:_(?<label>.+))?$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>用备份覆盖当前存档（覆盖前会自动再备份一次当前状态）。</summary>
    public static bool Restore(string root, string uid, string backupDir, out string? error)
    {
        error = null;
        if (!Directory.Exists(backupDir)) { error = "备份目录不存在"; return false; }

        try
        {
            Backup(root, uid, "before_restore");

            var dest = SaveDirOf(root, uid);
            Directory.CreateDirectory(ResponsesDirOf(root, uid));

            var restored = 0;
            foreach (var f in Directory.GetFiles(backupDir, "*.bin", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(backupDir, f);
                if (rel.StartsWith("_")) continue;
                var target = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(f, target, overwrite: true);
                restored++;
            }

            var gs = Path.Combine(backupDir, "gamestate.json");
            if (File.Exists(gs))
                File.Copy(gs, GameStateFileOf(root, uid), overwrite: true);

            L($"[Save] uid={uid} 已从备份还原 {restored} 个响应: {backupDir}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool DeleteBackup(string backupDir, out string? error)
    {
        error = null;
        try
        {
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, recursive: true);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static string BackupAsZip(string root, string uid, string zipPath)
    {
        var src = SaveDirOf(root, uid);
        if (Directory.Exists(zipPath)) File.Delete(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        ZipFile.CreateFromDirectory(src, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }

    // ------------------------------------------------------------------
    // 迁移升级前的全局存档
    // ------------------------------------------------------------------

    /// <summary>
    /// 把升级前的全局存档 Data/responses + Data/gamestate.json 迁移为 saves/34184063，
    /// 使其成为一个可被导入/用作模板的普通账号存档。幂等：目标已存在则跳过。
    /// </summary>
    public static bool MigrateLegacySave(string root)
    {
        var legacyResponses = Path.Combine(root, "Data", "responses");
        var legacyState = Path.Combine(root, "Data", "gamestate.json");
        if (!Directory.Exists(legacyResponses)) return false;
        if (Directory.GetFiles(legacyResponses, "*.bin", SearchOption.TopDirectoryOnly).Length == 0) return false;

        var target = SaveDirOf(root, LegacyUid);
        if (Directory.Exists(target)) return false;

        try
        {
            Directory.CreateDirectory(ResponsesDirOf(root, LegacyUid));
            CopyResponses(legacyResponses, ResponsesDirOf(root, LegacyUid));

            File.WriteAllText(GameStateFileOf(root, LegacyUid),
                File.Exists(legacyState) ? File.ReadAllText(legacyState) : DefaultGameStateJson,
                new UTF8Encoding(false));

            L($"[Save] 已迁移升级前存档 -> {target}");
            return true;
        }
        catch (Exception ex)
        {
            L($"[Save] 迁移升级前存档失败: {ex.Message}");
            return false;
        }
    }

    private static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(bad.Contains(c) ? '_' : c);
        return sb.ToString();
    }
}
