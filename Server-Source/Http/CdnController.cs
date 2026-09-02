using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntoTheVoidServer.Http;

/// <summary>
/// CDN file server - serves game asset bundles locally to bypass launcher downloads
/// Handles package.jinzhangshu.com requests for YooAsset bundles
/// </summary>
[ApiController]
[Route("cdn")]
public class CdnController : ControllerBase
{
    private static string StreamingAssetsRoot => GamePaths.StreamingAssetsRoot;

    [HttpGet("ABRes/{**path}")]
    [HttpGet("MidABRes/{**path}")]
    [HttpGet("MidOfficialABRes/{**path}")]
    [HttpGet("PResABRes/{**path}")]
    [HttpGet("PResOfficialABRes/{**path}")]
    [HttpGet("ResABRes/{**path}")]
    [HttpGet("ResOfficialABRes/{**path}")]
    [HttpGet("ABReshttps/{**path}")]
    [HttpGet("ResABReshttps/{**path}")]
    public IActionResult ServeBundle(string path)
    {
        Log.Information("[CDN] Bundle request: {Path}", path);

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
        {
            return NotFound();
        }

        // Search in StreamingAssets recursively
        var foundFiles = Directory.GetFiles(StreamingAssetsRoot, fileName, SearchOption.AllDirectories);
        if (foundFiles.Length > 0)
        {
            Log.Information("[CDN] Found bundle at: {FoundPath}", foundFiles[0]);
            var fileBytes = System.IO.File.ReadAllBytes(foundFiles[0]);
            return File(fileBytes, "application/octet-stream");
        }

        Log.Warning("[CDN] Bundle not found: {Path}", path);
        return NotFound();
    }
}
