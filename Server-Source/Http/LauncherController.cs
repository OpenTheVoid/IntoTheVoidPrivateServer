using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntoTheVoidServer.Http;

[ApiController]
[Route("")]
public class LauncherController : ControllerBase
{
    [HttpGet("download")]
    [HttpPost("download")]
    public async Task<IActionResult> Download()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("Launcher request: /download ({Method}) body={Body}", Request.Method, body);
        return Ok(new
        {
            code = 0,
            msg = "ok",
            data = new
            {
                version = "2.1.0.1",
                btnStage = 4,
                fileInfoJson = "https://package.jinzhangshu.com/newLauncher/2.1.0.1_info.json",
                diffInfoJson = "https://package.jinzhangshu.com/newLauncher/diff_info.json",
                updateInfo = new
                {
                    version = "2.1.0.1",
                    updateDesc = "Offline server active",
                    isNeedUpdate = false,
                    isNeedReDownload = false,
                },
                DownLoadInfo01 = "https://package.jinzhangshu.com/ABRes",
                DownLoadInfo02 = "https://package.jinzhangshu.com/ResABRes",
                DownLoadInfo03 = "https://package.jinzhangshu.com/MidABRes",
                DownLoadInfo04 = "https://package.jinzhangshu.com/PResABRes",
                canOpenGame = true,
                checkIsOpened = true,
                serverIp = "127.0.0.1",
                serverPort = 30531,
                httpIp = "http://127.0.0.1:8183",
                extraWebAddress = "http://127.0.0.1:8184",
                noticeUrl = "",
                clusterIpList = new[] { "127.0.0.1" },
                autoEnableStartUp = 1,
                autoOpenGameLauncher = 1,
            }
        });
    }

    [HttpPost("launcher_tab")]
    public async Task<IActionResult> LauncherTab()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("Launcher request: /launcher_tab body={Body}", body);
        return Ok(new
        {
            code = 0,
            msg = "ok",
            data = new
            {
                tabs = new[]
                {
                    new { id = 1, name = "首页", type = "web", url = "", sort = 1 },
                    new { id = 2, name = "公告", type = "notice", url = "", sort = 2 },
                },
                noticeList = Array.Empty<object>(),
                bannerList = Array.Empty<object>(),
            }
        });
    }

    [HttpGet("version")]
    public IActionResult Version()
    {
        return Ok(new { version = "2.1.0.1", canOpenGame = true });
    }

    [HttpGet("notice")]
    public IActionResult Notice()
    {
        return Ok(new { code = 0, data = new { notices = Array.Empty<object>() } });
    }

    [HttpPost("feedback")]
    public IActionResult Feedback()
    {
        return Ok(new { code = 0, msg = "ok" });
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "online",
            server = "IntoTheVoid Offline Server",
            time = DateTime.UtcNow.ToString("o"),
            players = 1,
            version = "2.1.0.1"
        });
    }

    [HttpPost("ReportClientDownload")]
    public async Task<IActionResult> ReportClientDownload()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Log.Information("ReportClientDownload: {Body}", body);
        return Ok(new { code = 0, msg = "ok" });
    }
}

[ApiController]
[Route("query")]
public class QueryController : ControllerBase
{
    [HttpGet("serverList")]
    [HttpPost("serverList")]
    public IActionResult ServerList()
    {
        Log.Information("Query: serverList");
        return Ok(new
        {
            code = 0,
            data = new[]
            {
                new
                {
                    id = 1,
                    name = "Offline Server",
                    ip = "127.0.0.1",
                    port = 30531,
                    status = "normal",
                    load = 0,
                }
            }
        });
    }

    [HttpGet("announcement")]
    public IActionResult Announcement()
    {
        return Ok(new { code = 0, data = new { content = "Welcome to IntoTheVoid Offline Server" } });
    }
}
