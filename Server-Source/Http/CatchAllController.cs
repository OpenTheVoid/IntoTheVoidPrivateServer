using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntoTheVoidServer.Http;

[ApiController]
[Route("{*url}")]
public class CatchAllController : ControllerBase
{
    [HttpGet]
    [HttpPost]
    [HttpPut]
    [HttpDelete]
    [HttpPatch]
    [HttpOptions]
    [HttpHead]
    public async Task<IActionResult> HandleUnknown(string url)
    {
        var host = Request.Host.Host;
        var method = Request.Method;

        // Serve the local GM admin panel from wwwroot (the catch-all route would
        // otherwise swallow /admin/* and return JSON instead of the HTML page).
        if ((url ?? "").StartsWith("admin", StringComparison.OrdinalIgnoreCase))
        {
            var wwwRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var rel = url.Trim('/');
            var filePath = string.IsNullOrEmpty(rel) || rel.Equals("admin", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(wwwRoot, "admin", "index.html")
                : Path.Combine(wwwRoot, rel);
            var normalized = Path.GetFullPath(filePath);
            var rootFull = Path.GetFullPath(wwwRoot);
            if (normalized.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) &&
                System.IO.File.Exists(normalized))
            {
                Log.Information("[Admin] Serving static file: {Path}", normalized);
                var contentType = normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    ? "text/html; charset=utf-8"
                    : "application/octet-stream";
                return PhysicalFile(normalized, contentType);
            }
            Log.Warning("[Admin] Admin file not found: {Path}", filePath);
        }

        var query = Request.QueryString.ToString();

        string body = "";
        if (method != "GET" && method != "HEAD" && Request.ContentLength > 0 && Request.ContentLength < 50000)
        {
            using var reader = new StreamReader(Request.Body);
            body = await reader.ReadToEndAsync();
        }

        Log.Warning("[CatchAll] {Method} {Host}/{url}{query} - returning generic success{body}",
            method, host, url, query,
            string.IsNullOrEmpty(body) ? "" : $"\n  Body: {body}");

        // Return a generic success response for ALL requests
        // This ensures the game doesn't get stuck on unknown API endpoints
        return Ok(new { code = 0, msg = "ok", data = new { } });
    }
}
