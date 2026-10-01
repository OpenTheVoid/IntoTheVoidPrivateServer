using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntoTheVoidServer.Accounts;

/// <summary>
/// 单个账号记录。
///
/// 密码存储策略（v2）：
///   客户端登录时会把密码用 <b>私服公钥</b> 做 RSA/PKCS#1 v1.5 加密后随 /login 上传
///   （由 BepInEx 插件 UseCustomServer 在 RSACryptoServiceProvider.Encrypt 处把
///    原本用游戏内置 APP_PUB 加密的结果改用私服公钥重新加密）；
///   服务端用 <c>pw_rsa_private_key.txt</c> 里的私钥解密拿到明文，
///   再与这里保存的 PBKDF2-SHA256(明文, 盐) 做定长比对。
///
///   为什么不能直接比对密文：RSA PKCS#1 v1.5 加密带随机填充，同一密码每次密文都不同。
/// </summary>
public sealed class AccountRecord
{
    public string Username { get; set; } = "";

    /// <summary>纯数字 uid。客户端 RoomManagerDemo.OnEnter 会 int.Parse(uid)，非数字会导致卡载入。</summary>
    public string Uid { get; set; } = "";

    /// <summary>Base64(PBKDF2-SHA256(password, Salt, Iterations, 32))。</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>Base64(16 字节随机盐)。</summary>
    public string Salt { get; set; } = "";

    /// <summary>旧版遗留字段（已被 RSA 密文等值比对方案废弃）。保留用于识别旧账号并在登录时给出准确诊断。</summary>
    public string? PasswordCipher { get; set; }

    /// <summary>登录凭证。账号服务器校验密码后下发，客户端本地缓存用于免密登录。</summary>
    public string Token { get; set; } = "";

    public string CreatedAt { get; set; } = "";
    public string LastLoginAt { get; set; } = "";
}

internal sealed class AccountFile
{
    public int Version { get; set; } = 2;
    public List<AccountRecord> Accounts { get; set; } = new();
}

/// <summary>
/// 账号库（服务端 / 登录器共用；纯 .NET，不依赖 ASP.NET 或 Serilog）。
///
/// 落盘位置：&lt;root&gt;/Data/accounts.json —— root 即 IntoTheVoidServer 的内容根目录。
/// 服务端在每次登录请求时 Reload()，因此登录器在服务端运行期间新增/删除账号也能立刻生效。
/// </summary>
public static class AccountStore
{
    /// <summary>日志回调（服务端接 Serilog，登录器接 UI）。默认丢弃。</summary>
    public static Action<string>? Log;

    private static readonly object Gate = new();
    private static readonly List<AccountRecord> Accounts_ = new();
    private static string _root = "";
    private static string FilePath => Path.Combine(_root, "Data", "accounts.json");

    /// <summary>uid 分配起点，避开原存档 uid 34184063。</summary>
    private const int UidBase = 34184064;

    // ------------------------------------------------------------------
    // 账号名规则
    //
    // 必须是**纯数字**：游戏内账号输入框是手机号输入框
    // （OfficialLoginPanel 里的 PasswordLoginGroup/PhoneNumberInputBG），
    // 字母根本敲不进去 —— 若登录器放行字母，就会建出"游戏里输不进来"的死账号。
    // 上限 11 位对齐手机号长度，避免超出游戏输入框可容纳的字符数。
    // ------------------------------------------------------------------

    /// <summary>账号名最短位数。</summary>
    public const int UsernameMinLen = 3;

    /// <summary>账号名最长位数（对齐手机号 11 位）。</summary>
    public const int UsernameMaxLen = 11;

    /// <summary>账号名规则说明（登录器 UI 直接复用）。</summary>
    public const string UsernameRuleText = "账号需为 3-11 位数字（游戏内账号框只能输入数字）";

    // ------------------------------------------------------------------
    // 密码参数
    // ------------------------------------------------------------------

    /// <summary>PBKDF2 迭代次数（改动会让旧账号哈希失效，不要随意调整）。</summary>
    public const int Pbkdf2Iterations = 100_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>私服密码密钥对中的私钥文件名（PKCS#8 Base64），用于解密客户端上传的密码密文。</summary>
    public const string PasswordKeyFileName = "pw_rsa_private_key.txt";

    /// <summary>私钥查找目录覆盖（默认进程目录）。置空则回落到 <see cref="AppContext.BaseDirectory"/>，再回落到 &lt;root&gt;。</summary>
    public static string? PasswordKeyPathOverride;

    /// <summary>
    /// 兼容开关：当密码密文无法用私服私钥解密时（典型情形＝BepInEx 插件未部署，客户端仍在用游戏内置
    /// 公钥加密），是否放行登录。true = 放行并打印显式告警（默认，保证插件未装也能进游戏）；
    /// false = 拒绝（105003034），即"必须装插件才能登录"的严格模式。
    /// 可在 &lt;root&gt;/Data/server_settings.json 里用 {"allowUndecryptablePassword": false} 覆盖。
    /// </summary>
    public static bool AllowUndecryptablePassword = true;

    public static void Initialize(string root)
    {
        _root = root;
        LoadSettings();
        Reload();
    }

    /// <summary>读取 Data/server_settings.json（缺失/损坏时保持默认值）。</summary>
    private static void LoadSettings()
    {
        try
        {
            var path = Path.Combine(_root, "Data", "server_settings.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("allowUndecryptablePassword", out var v) &&
                (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
            {
                AllowUndecryptablePassword = v.GetBoolean();
                Log?.Invoke($"[Account] 配置: allowUndecryptablePassword={AllowUndecryptablePassword}");
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[Account] 读取 server_settings.json 失败（使用默认值）: {ex.Message}");
        }
    }

    public static IReadOnlyList<AccountRecord> Snapshot()
    {
        lock (Gate) return Accounts_.Select(Clone).ToList();
    }

    public static int Count { get { lock (Gate) return Accounts_.Count; } }

    // ------------------------------------------------------------------
    // 持久化
    // ------------------------------------------------------------------

    public static void Reload()
    {
        lock (Gate)
        {
            Accounts_.Clear();
            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path, Encoding.UTF8);
                var file = JsonSerializer.Deserialize<AccountFile>(json);
                if (file?.Accounts != null) Accounts_.AddRange(file.Accounts.Where(a => !string.IsNullOrEmpty(a.Uid)));
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[Account] 读取账号库失败: {ex.Message}");
            }
        }
    }

    private static void SaveLocked()
    {
        try
        {
            var path = FilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var file = new AccountFile { Version = 2, Accounts = Accounts_.ToList() };
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[Account] 写入账号库失败: {ex.Message}");
        }
    }

    private static AccountRecord Clone(AccountRecord a) => new()
    {
        Username = a.Username,
        Uid = a.Uid,
        PasswordHash = a.PasswordHash,
        Salt = a.Salt,
        PasswordCipher = a.PasswordCipher,
        Token = a.Token,
        CreatedAt = a.CreatedAt,
        LastLoginAt = a.LastLoginAt,
    };

    // ------------------------------------------------------------------
    // 查询
    // ------------------------------------------------------------------

    public static AccountRecord? FindByUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        lock (Gate)
        {
            var hit = Accounts_.FirstOrDefault(a =>
                string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
            return hit == null ? null : Clone(hit);
        }
    }

    public static AccountRecord? FindByUid(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return null;
        lock (Gate)
        {
            var hit = Accounts_.FirstOrDefault(a => a.Uid == uid);
            return hit == null ? null : Clone(hit);
        }
    }

    /// <summary>用客户端缓存的 token 换账号（游戏服登录路径）。</summary>
    public static AccountRecord? FindByToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        lock (Gate)
        {
            var hit = Accounts_.FirstOrDefault(a => !string.IsNullOrEmpty(a.Token) && a.Token == token);
            return hit == null ? null : Clone(hit);
        }
    }

    // ------------------------------------------------------------------
    // 密码：哈希 / 校验 / 解密
    // ------------------------------------------------------------------

    /// <summary>为新账号生成随机盐（Base64）。</summary>
    public static string NewSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltBytes));

    /// <summary>计算 PBKDF2-SHA256 口令哈希（Base64）。</summary>
    public static string HashPassword(string plainPassword, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(plainPassword ?? ""),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>定长比对，避免计时侧信道。</summary>
    public static bool VerifyPassword(AccountRecord rec, string? plainPassword)
    {
        if (string.IsNullOrEmpty(rec.PasswordHash) || string.IsNullOrEmpty(rec.Salt)) return false;
        if (plainPassword == null) return false;
        var expected = Convert.FromBase64String(rec.PasswordHash);
        var actual = Convert.FromBase64String(HashPassword(plainPassword, rec.Salt));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>把明文口令写入记录（盐随机生成）。</summary>
    public static void SetPassword(AccountRecord rec, string plainPassword)
    {
        rec.Salt = NewSalt();
        rec.PasswordHash = HashPassword(plainPassword, rec.Salt);
        rec.PasswordCipher = null; // 彻底弃用旧方案
    }

    private static string? _cachedKey;
    private static bool _keyLoaded;

    /// <summary>定位并加载私服密码私钥（PKCS#8 Base64）。找不到返回 null。</summary>
    private static string? PasswordPrivateKey()
    {
        if (_keyLoaded) return _cachedKey;
        _keyLoaded = true;

        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(PasswordKeyPathOverride)) candidates.Add(PasswordKeyPathOverride!);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, PasswordKeyFileName));
        if (!string.IsNullOrEmpty(_root)) candidates.Add(Path.Combine(_root, PasswordKeyFileName));

        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    _cachedKey = File.ReadAllText(path).Trim();
                    Log?.Invoke($"[Account] 密码私钥已加载: {path}");
                    return _cachedKey;
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[Account] 读取密码私钥失败 {path}: {ex.Message}");
            }
        }

        Log?.Invoke($"[Account] 未找到密码私钥 {PasswordKeyFileName}，尝试过: {string.Join(" | ", candidates)}");
        return null;
    }

    /// <summary>强制重新加载私钥（密钥轮换后调用）。</summary>
    public static void InvalidatePasswordKey()
    {
        _keyLoaded = false;
        _cachedKey = null;
    }

    /// <summary>
    /// 用私服私钥解密客户端上传的密码密文（Base64(RSA/PKCS#1 v1.5)）。
    /// 解密失败（插件未部署、密文损坏、密钥不匹配）返回 null。
    /// </summary>
    public static string? DecryptPassword(string? base64Cipher)
    {
        if (string.IsNullOrWhiteSpace(base64Cipher)) return null;

        var key = PasswordPrivateKey();
        if (string.IsNullOrEmpty(key)) return null;

        try
        {
            var cipher = Convert.FromBase64String(base64Cipher.Trim());
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(key!), out _);
            var plain = rsa.Decrypt(cipher, RSAEncryptionPadding.Pkcs1);
            // 客户端用 Encoding.ASCII.GetBytes 取字节，这里按 ASCII 还原
            return Encoding.ASCII.GetString(plain);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[Account] 密码密文解密失败: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------
    // 校验规则（登录器建号时使用；与客户端 IsValidPassword 兼容）
    // ------------------------------------------------------------------

    /// <summary>账号名 = 纯数字、<see cref="UsernameMinLen"/>-<see cref="UsernameMaxLen"/> 位。</summary>
    public static bool IsValidUsername(string? u)
    {
        if (string.IsNullOrWhiteSpace(u)) return false;
        if (u.Length < UsernameMinLen || u.Length > UsernameMaxLen) return false;
        return u.All(c => c >= '0' && c <= '9');
    }

    public static bool IsValidPassword(string? p)
    {
        if (string.IsNullOrEmpty(p)) return false;
        if (p.Length < 6 || p.Length > 32) return false;
        // 客户端用 Encoding.ASCII 取字节加密，非 ASCII 字符会被替换为 '?'，必须拒绝
        return p.All(c => c > 32 && c < 127);
    }

    // ------------------------------------------------------------------
    // 增删
    // ------------------------------------------------------------------

    /// <summary>
    /// 创建账号并初始化其独立存档目录。
    /// </summary>
    /// <param name="templateResponsesDir">
    /// 非空时以该目录（内含 *.bin 的 responses 目录）作为存档模板复制；为空则创建全新空档。
    /// </param>
    public static AccountRecord? Create(string username, string password, string? templateResponsesDir, out string? error)
        => Create(username, password, templateResponsesDir, null, out error);

    /// <summary>
    /// 创建账号并初始化其独立存档目录。
    /// </summary>
    /// <param name="templateResponsesDir">
    /// 非空时以该目录（内含 *.bin 的 responses 目录）作为存档模板复制；为空则创建全新空档。
    /// </param>
    /// <param name="preferredUid">
    /// 非空、未被任何账号占用、且 <c>Data/saves/&lt;uid&gt;</c> 已存在时，直接沿用该存档（原样保留，
    /// 不复制不改动）——用于让新账号继续使用升级前的原档（uid 34184063）。条件不满足则自动分配新 uid。
    /// </param>
    public static AccountRecord? Create(string username, string password, string? templateResponsesDir, string? preferredUid, out string? error)
    {
        error = null;
        username = (username ?? "").Trim();

        if (!IsValidUsername(username)) { error = UsernameRuleText; return null; }
        if (!IsValidPassword(password)) { error = "密码需为 6-32 位可见 ASCII 字符（不含空格/中文）"; return null; }

        lock (Gate)
        {
            if (Accounts_.Any(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"账号「{username}」已存在";
                return null;
            }

            var adopt = !string.IsNullOrEmpty(preferredUid)
                        && !Accounts_.Any(a => a.Uid == preferredUid)
                        && PlayerSaveStore.Exists(_root, preferredUid!);

            var rec = new AccountRecord
            {
                Username = username,
                Uid = adopt ? preferredUid! : AllocateUidLocked(),
                Token = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };
            SetPassword(rec, password);

            try
            {
                if (adopt) PlayerSaveStore.EnsureSave(_root, rec.Uid);
                else PlayerSaveStore.CreateSave(_root, rec.Uid, templateResponsesDir);
            }
            catch (Exception ex)
            {
                error = $"创建存档目录失败: {ex.Message}";
                return null;
            }

            Accounts_.Add(rec);
            SaveLocked();
            Log?.Invoke($"[Account] 已创建账号 {rec.Username} (uid={rec.Uid})" +
                        (adopt ? " [沿用原档]" : string.IsNullOrEmpty(templateResponsesDir) ? " [空档]" : " [继承模板]"));
            return Clone(rec);
        }
    }

    public static bool Delete(string username, bool deleteSave, out string? error)
    {
        error = null;
        lock (Gate)
        {
            var hit = Accounts_.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
            if (hit == null) { error = "账号不存在"; return false; }

            // 原档保护：uid 34184063 是升级前那份全局存档，账号可以删，存档目录不动
            if (deleteSave && hit.Uid == PlayerSaveStore.LegacyUid)
            {
                deleteSave = false;
                Log?.Invoke($"[Account] 原档保护：UID {hit.Uid} 的存档目录已保留（未删除）");
            }

            Accounts_.Remove(hit);
            SaveLocked();

            if (deleteSave)
            {
                try { PlayerSaveStore.DeleteSave(_root, hit.Uid); }
                catch (Exception ex) { error = $"账号已删除，但存档目录清理失败: {ex.Message}"; return true; }
            }
            Log?.Invoke($"[Account] 已删除账号 {hit.Username} (uid={hit.Uid})");
            return true;
        }
    }

    /// <summary>
    /// 登录校验（账号服路径）。
    ///
    /// 判定顺序：
    ///   1. 账号名不合法 → 105003001
    ///   2. 账号不存在 / 无密码哈希 → 105003034
    ///   3. 密文能用私服私钥解出明文 → 比对 PBKDF2 哈希；不符 → 105003034
    ///   4. 密文解不开（插件未部署）→ 依 <see cref="AllowUndecryptablePassword"/> 决定放行或拒绝
    /// </summary>
    public static AccountRecord? Authenticate(string? username, string? passwordCipher, out int errorCode, out string? failReason)
    {
        errorCode = 0;
        failReason = null;

        // 这里**只判空**，不套用 IsValidUsername：账号名规则收紧（改纯数字）之后，
        // 历史上建过的字母账号仍应能通过鉴权，不能因为建号规则变化就连带拒登。
        if (string.IsNullOrWhiteSpace(username)) { errorCode = 105003001; failReason = "账号不合法"; return null; }

        var rec = FindByUsername(username);
        if (rec == null) { errorCode = 105003034; failReason = "账号不存在"; return null; }

        if (string.IsNullOrEmpty(rec.PasswordHash) || string.IsNullOrEmpty(rec.Salt))
        {
            errorCode = 105003034;
            failReason = string.IsNullOrEmpty(rec.PasswordCipher)
                ? "账号无密码（数据异常）"
                : "旧版密码格式（密文比对方案已废弃），请用登录器重建该账号";
            return null;
        }

        var plain = DecryptPassword(passwordCipher);
        if (plain == null)
        {
            if (AllowUndecryptablePassword)
            {
                Log?.Invoke($"[Account] ⚠ 宽松模式：{rec.Username} 的密码密文无法解密（BepInEx 插件可能未部署），本次放行登录");
            }
            else
            {
                errorCode = 105003034;
                failReason = "密码密文无法解密：客户端插件未部署或密钥不匹配（严格模式已拒绝）";
                return null;
            }
        }
        else if (!VerifyPassword(rec, plain))
        {
            errorCode = 105003034;
            failReason = "密码错误";
            return null;
        }

        TouchLogin(rec.Username, out var fresh);
        return fresh;
    }

    private static void TouchLogin(string username, out AccountRecord? updated)
    {
        lock (Gate)
        {
            var hit = Accounts_.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
            if (hit == null) { updated = null; return; }
            hit.LastLoginAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            // 老账号（本次升级前创建）可能没有 token，补发一个
            if (string.IsNullOrEmpty(hit.Token)) hit.Token = Guid.NewGuid().ToString("N");
            SaveLocked();
            updated = Clone(hit);
        }
    }

    private static string AllocateUidLocked()
    {
        var used = new HashSet<string>(Accounts_.Select(a => a.Uid), StringComparer.Ordinal);
        try
        {
            var savesDir = PlayerSaveStore.SavesRootOf(_root);
            if (Directory.Exists(savesDir))
                foreach (var d in Directory.GetDirectories(savesDir))
                    used.Add(Path.GetFileName(d));
        }
        catch { /* 目录不可读时忽略，仅按已登记账号去重 */ }

        var n = UidBase;
        while (used.Contains(n.ToString())) n++;
        return n.ToString();
    }

    /// <summary>把一个外部存档目录（如原 uid 34184063 的存档）登记为一个新账号。</summary>
    public static AccountRecord? Import(string username, string password, string sourceUid, out string? error)
    {
        error = null;
        var src = PlayerSaveStore.SaveDirOf(_root, sourceUid);
        if (!Directory.Exists(src)) { error = $"待导入的存档不存在: {src}"; return null; }
        return Create(username, password, Path.Combine(src, "responses"), out error);
    }
}
