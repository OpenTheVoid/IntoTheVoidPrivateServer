using IntoTheVoidServer.Accounts;
using IntoTheVoidServer.Pomelo;
using IntoTheVoidServer.Router;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntoTheVoidServer.Http;

[ApiController]
[Route("")]
public class GameApiController : ControllerBase
{
    private const string ServerIp = "127.0.0.1";
    private const int ServerPort = 30531;

    /// <summary>短信/实名等辅助端点的占位 token。</summary>
    private static readonly string SessionToken = Guid.NewGuid().ToString("N");

    private static readonly string? RsaPrivateKeyBase64 = LoadPrivateKey();

    private readonly IWebHostEnvironment _env;
    private readonly PlayerSessionManager _session;

    public GameApiController(IWebHostEnvironment env, PlayerSessionManager session)
    {
        _env = env;
        _session = session;
    }

    private static string? LoadPrivateKey()
    {
        var keyPath = Path.Combine(AppContext.BaseDirectory, "rsa_private_key.txt");
        if (System.IO.File.Exists(keyPath))
        {
            var key = System.IO.File.ReadAllText(keyPath).Trim();
            Log.Information("[GameAPI] RSA private key loaded from {Path}", keyPath);
            return key;
        }
        Log.Warning("[GameAPI] RSA private key not found at {Path}", keyPath);
        return null;
    }

    // ==================================================================
    // /login —— 一个端点，两种角色
    //
    // 客户端的登录分两步，两步都打到这里（域名不同，插件 DNS 劫持后都指向本机）：
    //   1) 账号服务器 official.jinzhangshu.com/login   body 带 account + password(RSA密文)
    //      -> 校验账号密码，下发 token
    //   2) 游戏服务器   <game>/login                   body 带 current_token
    //      -> token 换 uid，并把该 uid 的独立存档装载进内存
    // ==================================================================
    [HttpPost("login")]
    public async Task<IActionResult> Login()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var host = Request.Host.Host;

        string? account = null, passwordCipher = null, currentToken = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var value = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
                    switch (prop.Name)
                    {
                        case "account": account = value; break;
                        case "password": passwordCipher = value; break;
                        case "current_token": currentToken = value; break;
                    }
                }
            }
        }
        catch
        {
            // 非 JSON 体（历史行为是直接回空响应），走下面按 token 的分支即可
        }

        Log.Information("[GameAPI] POST /login host={Host} account={Account} 模式={Mode}",
            host, account ?? "-",
            !string.IsNullOrEmpty(passwordCipher) ? "账号密码"
                : (!string.IsNullOrEmpty(currentToken) ? "token换uid" : "未知"));

        if (host.StartsWith("official.", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(passwordCipher))
            return OfficialLogin(account, passwordCipher);

        return GameServerLogin(currentToken);
    }

    /// <summary>账号服务器角色：校验账号密码，下发 token。</summary>
    private IActionResult OfficialLogin(string? account, string? passwordCipher)
    {
        AccountStore.Reload(); // 登录器可能在服务端运行期间建号/删号

        var rec = AccountStore.Authenticate(account, passwordCipher, out var code, out var reason);
        if (rec == null)
        {
            Log.Warning("[GameAPI] 账号登录被拒: account={Account}, code={Code} ({Name}) 原因={Reason}",
                account ?? "-", code, ErrorCodeName(code), reason ?? "-");

            // 必须返回**非 200**，两个理由：
            //   1) LuaNetManager:SendHttpMessage 只在 !IsSuccess 时读 body 的小写 "code"，
            //      调 TipManager:ShowErrorCodeTips 弹文案；
            //   2) LoginVM:SendOfficialLoginMsg 也只在非 200 分支才 CleanToken()。
            //      若这里返回 200 且不带 token，客户端会带着上一个账号残留的 token
            //      继续走游戏服登录 → 串号进错存档。
            // body 只用小写 code（不放 Code），避免大小写两处各弹一次提示。
            return StatusCode(StatusCodes.Status401Unauthorized,
                new { code, msg = reason ?? ErrorCodeName(code) });
        }

        Log.Information("[GameAPI] 账号登录通过: account={Account} uid={Uid}", rec.Username, rec.Uid);
        return Ok(new
        {
            token = rec.Token,
            need_authorization = false,
        });
    }

    /// <summary>游戏服务器角色：token 换 uid，并装载该账号的独立存档。</summary>
    private IActionResult GameServerLogin(string? currentToken)
    {
        AccountStore.Reload();

        var rec = AccountStore.FindByToken(currentToken);
        if (rec == null)
        {
            // 105003028 = 登录secret失效 —— 客户端会提示并退回登录界面
            Log.Warning("[GameAPI] 游戏服登录失败: token 无效或已过期");
            return Content("errcode=105003028&uid=&token=&secret=&showpolicy=0&showtest=&newaccount=0",
                "text/plain", Encoding.UTF8);
        }

        _session.CurrentPlayerId = rec.Uid;
        _session.SessionTicket = rec.Token;
        _session.IsLoggedIn = true;

        // 多账号存档隔离：装载该 uid 的响应集与货币状态
        var root = _env.ContentRootPath;
        CapturedData.LoadForPlayer(root, rec.Uid);
        GameState.ActivatePlayer(root, rec.Uid);
        // 悖域巡查(突击警报)进度：基线取自该账号的 AlertEventInfoResponse 快照，
        // 再叠加 Data/saves/<uid>/alert_progress.json 里服务端记录的进度。
        // 必须在 CapturedData.LoadForPlayer 之后（需要读快照）。
        AlertEventProgress.ActivatePlayer(root, rec.Uid);
        // 悖域回归(局外周本)进度：基线取自该账号的 WeeklyQuestInfoResponse 快照
        // (其中的 ChoseQuests 长度 = 已解锁任务数)，再叠加
        // Data/saves/<uid>/weekly_progress.json。同样必须在 LoadForPlayer 之后。
        WeeklyProgress.ActivatePlayer(root, rec.Uid);
        // 物品/货币持有量账本：结算奖励下发的 Amount/Count 是"更新后总量"而不是增量
        // （客户端处处做"新值-旧值"），所以服务端必须自己记一份持有量。
        // 基线取自该账号的 BackPackListResponse / PlayerDataResponse 快照，
        // 同样必须在 CapturedData.LoadForPlayer 之后。见 Pomelo/ItemLedger.cs。
        ItemLedger.ActivatePlayer(root, rec.Uid);

        var responseData =
            $"errcode=0&uid={rec.Uid}&token={rec.Token}&secret={rec.Token}&showpolicy=0&showtest=&newaccount=0";
        var sign = SignData(responseData);

        Log.Information("[GameAPI] 游戏服登录成功: account={Account} uid={Uid}",
            rec.Username, rec.Uid);

        return Content($"{responseData}&sign={sign}", "text/plain", Encoding.UTF8);
    }

    private static string ErrorCodeName(int code) => code switch
    {
        105003001 => "账号不合法",
        105003002 => "账号已存在",
        105003003 => "账号不存在",
        105003034 => "账号或密码错误",
        105003028 => "登录secret失效",
        105003021 => "登录失败",
        _ => "登录失败",
    };

    private string SignData(string data)
    {
        if (string.IsNullOrEmpty(RsaPrivateKeyBase64))
        {
            Log.Error("[GameAPI] RSA private key not available, returning empty sign");
            return "";
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(RsaPrivateKeyBase64), out _);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var signatureBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(signatureBytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GameAPI] RSA signing failed");
            return "";
        }
    }

    // ========== /server - 服务器列表 ==========
    [HttpGet("server")]
    public IActionResult GetServer()
    {
        Log.Information("[GameAPI] GET /server from {Host}", Request.Host.Host);
        return Ok(new
        {
            code = 0,
            msg = "ok",
            data = new
            {
                server_list = new[]
                {
                    new
                    {
                        id = 1,
                        name = "离线服务器",
                        ip = ServerIp,
                        port = ServerPort,
                        status = 1,
                        type = 1,
                        load = 0,
                        server_id = 1,
                        server_name = "Offline",
                        host = ServerIp,
                        area_id = 1,
                        area_name = "本地",
                    }
                },
                default_server_id = 1,
            }
        });
    }

    // ========== /announcement - 公告 ==========
    [HttpPost("announcement")]
    [HttpGet("announcement")]
    public IActionResult Announcement()
    {
        Log.Information("[GameAPI] /announcement from {Host}", Request.Host.Host);
        return Ok(new
        {
            code = 0,
            msg = "ok",
            data = new
            {
                notices = Array.Empty<object>(),
                banners = Array.Empty<object>(),
                content = "欢迎来到驱入虚空离线版",
                title = "离线服务器",
            }
        });
    }

    // ========== /ping - 心跳/防沉迷检查 ==========
    [HttpPost("ping")]
    public async Task<IActionResult> Ping()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var host = Request.Host.Host;
        Log.Information("[GameAPI] POST /ping from {Host} body={Body}", host, body);

        return Ok(new
        {
            need_authorization = false,
            age_range = 8, // 8表示成年人，不会触发防沉迷
        });
    }

    // ========== /sms_send - 发送短信验证码 ==========
    [HttpPost("sms_send")]
    public async Task<IActionResult> SmsSend()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("[GameAPI] POST /sms_send from {Host} body={Body}", Request.Host.Host, body);
        return Ok(new { code = 0, msg = "ok" });
    }

    // ========== /verify - 验证码验证 ==========
    [HttpPost("verify")]
    public async Task<IActionResult> Verify()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("[GameAPI] POST /verify from {Host} body={Body}", Request.Host.Host, body);
        return Ok(new
        {
            token = SessionToken,
            need_authorization = false,
        });
    }

    // ========== /checkstatus - 状态检查 ==========
    [HttpGet("checkstatus")]
    [HttpPost("checkstatus")]
    public IActionResult CheckStatus()
    {
        Log.Information("[GameAPI] /checkstatus from {Host}", Request.Host.Host);
        return Ok(new
        {
            code = 0,
            msg = "ok",
            data = new
            {
                status = "normal",
                server_status = 1,
                maintenance = false,
                can_login = true,
                can_play = true,
            }
        });
    }

    // ========== /api/checkstatus - 兼容路径 ==========
    [HttpGet("api/checkstatus")]
    [HttpPost("api/checkstatus")]
    public IActionResult CheckStatusAlt()
    {
        return CheckStatus();
    }

    // ========== /policy_update - 隐私政策检查 ==========
    [HttpPost("policy_update")]
    [HttpGet("policy_update")]
    public IActionResult PolicyUpdate()
    {
        Log.Information("[GameAPI] /policy_update from {Host}", Request.Host.Host);
        return Ok(new
        {
            code = 0,
            data = new
            {
                need_update = false,
                version = "1.0",
            }
        });
    }

    // ========== /wlc_check - 防沉迷检查 ==========
    [HttpPost("wlc_check")]
    public async Task<IActionResult> WlcCheck()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("[GameAPI] POST /wlc_check from {Host} body={Body}", Request.Host.Host, body);
        return Ok(new
        {
            code = 0,
            data = new
            {
                need_authorization = false,
                age_range = 8,
                is_adult = true,
                can_play = true,
                remaining_time = -1,
            }
        });
    }

    // ========== /wegame_bind_official_account - WeGame绑定 ==========
    [HttpPost("wegame_bind_official_account")]
    public async Task<IActionResult> WeGameBind()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("[GameAPI] POST /wegame_bind_official_account from {Host} body={Body}", Request.Host.Host, body);
        return Ok(new { error = 0 });
    }
}
