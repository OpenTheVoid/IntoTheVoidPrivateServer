using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntoTheVoidServer.Http;

/// <summary>
/// TapSDK 模拟控制器 - 处理 TapTap SDK 的各种请求
/// </summary>
[ApiController]
[Route("")]
public class TapSdkController : ControllerBase
{
    // ========== TapDB 数据统计 ==========
    
    [HttpPost("report")]
    [HttpPost("v1/report")]
    [HttpPost("tapdb/report")]
    public async Task<IActionResult> TapDbReport()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("[TapDB] Report from {Host}", Request.Host.Host);
        return Ok(new { code = 0, msg = "ok" });
    }

    [HttpPost("events")]
    [HttpPost("v1/events")]
    public async Task<IActionResult> TapDbEvents()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("[TapDB] Events from {Host}", Request.Host.Host);
        return Ok(new { code = 0, msg = "ok" });
    }

    // ========== TapSDK 配置 ==========
    
    [HttpGet("config")]
    [HttpGet("sdk/config")]
    [HttpGet("v1/config")]
    public IActionResult SdkConfig()
    {
        Log.Information("[TapSDK] Config request from {Host}", Request.Host.Host);
        return Ok(new
        {
            code = 0,
            data = new
            {
                enabled = true,
                region = "CN",
                server_url = "https://tapsdk.tapapis.cn",
            }
        });
    }

    // ========== 账号/登录相关 ==========
    
    [HttpPost("token")]
    [HttpPost("oauth/token")]
    [HttpPost("v1/oauth/token")]
    public async Task<IActionResult> OAuthToken()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("[TapSDK] OAuth token request from {Host}: {Body}", Request.Host.Host, body);
        
        // 返回一个模拟的 token
        return Ok(new
        {
            access_token = "demo_access_token_offline",
            token_type = "Bearer",
            expires_in = 8640000,
            refresh_token = "demo_refresh_token_offline",
            scope = "public_profile"
        });
    }

    [HttpGet("userinfo")]
    [HttpGet("v1/userinfo")]
    [HttpGet("profile")]
    public IActionResult UserInfo()
    {
        Log.Information("[TapSDK] User info request from {Host}", Request.Host.Host);
        return Ok(new
        {
            code = 0,
            data = new
            {
                openid = "offline_player_001",
                unionid = "offline_player_001",
                name = "离线玩家",
                avatar = "",
                gender = 0,
                isGuest = false,
            }
        });
    }

    // ========== 防沉迷相关 ==========
    
    [HttpPost("anti_addiction/config")]
    [HttpPost("v1/anti_addiction/config")]
    public async Task<IActionResult> AntiAddictionConfig()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("[TapSDK] Anti-addiction config from {Host}", Request.Host.Host);
        return Ok(new
        {
            code = 0,
            data = new
            {
                is_enabled = false,
                is_adult = true,
                age = 25,
            }
        });
    }

    [HttpPost("anti_addiction/login")]
    [HttpPost("v1/anti_addiction/login")]
    public async Task<IActionResult> AntiAddictionLogin()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("[TapSDK] Anti-addiction login from {Host}", Request.Host.Host);
        return Ok(new
        {
            code = 0,
            data = new
            {
                is_adult = true,
                can_play = true,
                remaining_time = -1, // -1 表示无限制
            }
        });
    }

    [HttpPost("anti_addiction/logout")]
    public IActionResult AntiAddictionLogout()
    {
        return Ok(new { code = 0 });
    }

    // ========== 通用接口 ==========
    
    [HttpGet("health_check")]
    public IActionResult HealthCheck()
    {
        return Ok(new { status = "ok" });
    }
}
