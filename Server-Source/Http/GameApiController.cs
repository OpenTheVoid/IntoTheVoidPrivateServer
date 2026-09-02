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
    // 官方 uid 为纯数字字符串（客户端 RoomManagerDemo.OnEnter 会 int.Parse(uid)，
    // 若为 GUID 会抛 FormatException 导致卡「载入中」）。此处使用固定纯数字 uid。
    private static readonly string UserUid = "34184063";
    private static readonly string SessionToken = GenerateJwtToken();
    private const string ServerIp = "127.0.0.1";
    private const int ServerPort = 30531;

    private static readonly string? RsaPrivateKeyBase64 = LoadPrivateKey();

    private static string GenerateJwtToken()
    {
        // Build a JWT token matching the game's expected format (HS256)
        var header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
        var payload = JsonSerializer.Serialize(new
        {
            platform = 1,
            scp = 1,
            birth_timestamp = 631152000, // 1990-01-01 - adult
            pwd = "offline",
            exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
            jti = Guid.NewGuid().ToString("D"),
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            nbf = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            sub = UserUid,
        });
        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        // We don't have a real HS256 secret, but the game likely doesn't verify JWT on client side
        // Just add a dummy signature
        var signatureB64 = Base64UrlEncode(Encoding.UTF8.GetBytes("offline_signature"));
        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
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

    // ========== /login - 登录端点 ==========
    [HttpPost("login")]
    public async Task<IActionResult> Login()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var host = Request.Host.Host;
        Log.Information("[GameAPI] POST /login from {Host} body={Body}", host, body);

        // Parse body to determine login type
        var hasPassword = body.Contains("\"password\"");
        var hasCurrentToken = body.Contains("\"current_token\"");

        // official.jinzhangshu.com is the account server - always returns JSON token
        if (host.StartsWith("official.", StringComparison.OrdinalIgnoreCase))
        {
            return OfficialLogin(body);
        }

        if (hasPassword && !hasCurrentToken)
        {
            return OfficialLogin(body);
        }

        return GameServerLogin(body);
    }

    // 官方账号登录 - 返回 JSON
    private IActionResult OfficialLogin(string body)
    {
        Log.Information("[GameAPI] Official login, returning JSON token");

        return Ok(new
        {
            token = SessionToken,
            need_authorization = false,
        });
    }

    // 游戏服务器登录 - 返回 URL编码 + RSA签名
    private IActionResult GameServerLogin(string body)
    {
        Log.Information("[GameAPI] Game server login, returning URL-encoded with RSA signature");

        var uid = UserUid;
        var token = SessionToken;
        var secret = SessionToken;

        // Build response without sign
        var responseData = $"errcode=0&uid={uid}&token={token}&secret={secret}&showpolicy=0&showtest=&newaccount=0";

        // Sign with RSA-SHA1
        var sign = SignData(responseData);

        // Full response: data + &sign=signature
        var fullResponse = $"{responseData}&sign={sign}";

        Log.Information("[GameAPI] Login response: {Response}", fullResponse);

        return Content(fullResponse, "text/plain", Encoding.UTF8);
    }

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
            var signBase64 = Convert.ToBase64String(signatureBytes);
            Log.Information("[GameAPI] RSA signature created, length={Length}", signBase64.Length);
            return signBase64;
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

        // 官方服的ping返回防沉迷信息
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
                age_range = 8, // 8表示成年人
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
