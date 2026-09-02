using IntoTheVoidServer.Http;
using IntoTheVoidServer.Net;
using IntoTheVoidServer.Pomelo;
using IntoTheVoidServer.Router;
using Serilog;
using System.Security.Cryptography.X509Certificates;
using IntoTheVoidServer;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/server_.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("=== IntoTheVoid Offline Server ===");
    Log.Information("Starting server...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSingleton<PlayerSessionManager>();
    builder.Services.AddSingleton<MessageRouter>();
    builder.Services.AddSingleton<PomeloTcpServer>(sp =>
    {
        var router = sp.GetRequiredService<MessageRouter>();
        return new PomeloTcpServer(30531, router);
    });

    // Configure HTTPS if cert exists
    var certPath = Path.Combine(builder.Environment.ContentRootPath, "cert.pfx");
    var certPassword = "intothevoid123!";
    if (File.Exists(certPath))
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Any, 80);
            options.Listen(System.Net.IPAddress.Any, 8183);
            options.Listen(System.Net.IPAddress.Any, 443, listenOptions =>
            {
                listenOptions.UseHttps(certPath, certPassword);
            });
            options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB max
        });
        Log.Information("HTTPS configured with cert: {CertPath}", certPath);
    }
    else
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Any, 80);
            options.Listen(System.Net.IPAddress.Any, 8183);
        });
        Log.Information("No cert.pfx found - HTTP only mode (ports 80, 8183)");
    }

    var app = builder.Build();

    GamePaths.Initialize(builder.Environment.ContentRootPath);
    GameState.SetSavePath(builder.Environment.ContentRootPath);
    GameState.InitializeDefaults();

    // Global error handler + request logging
    app.Use(async (context, next) =>
    {
        try
        {
            var host = context.Request.Host.Host;
            var path = context.Request.Path.Value ?? "";
            var method = context.Request.Method;

            // Read request body for logging
            string body = "";
            if (method != "GET" && context.Request.ContentLength > 0 && context.Request.ContentLength < 10000)
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
            }

            Log.Information("[HTTP] {Method} {Host}{Path}{Query} from {RemoteIp}{Body}",
                method, host, path, context.Request.QueryString,
                context.Connection.RemoteIpAddress,
                string.IsNullOrEmpty(body) ? "" : $"\n  Body: {body}");

            // Route ALL CDN-like requests on any domain to local files
            var pathTrimmed = path.TrimStart('/');
            if (!string.IsNullOrEmpty(pathTrimmed) &&
                (path.Contains("Version.txt", StringComparison.OrdinalIgnoreCase) ||
                 path.Contains("PackageManifest_", StringComparison.OrdinalIgnoreCase) ||
                 path.Contains("PackageHash_", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)))
            {
                var served = await TryServeCdnFile(context, pathTrimmed);
                if (served) return;
            }

            // Handle package. domains fully
            if (host.StartsWith("package.", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(pathTrimmed))
                {
                    var served = await TryServeCdnFile(context, pathTrimmed);
                    if (served) return;
                }
                // Return 200 for unknown package paths to avoid blocking.
                // The client probes version directories with a trailing slash (e.g.
                // ".../1.0.0.501.1.0.0.501/") before downloading bundles; a 404 there
                // makes it abort the CDN flow. A 200 with an empty body unblocks it.
                Log.Warning("[CDN] Unhandled package request (serving empty 200): {Path}", path);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{}");
                return;
            }

            await next();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled error in request pipeline");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
    });

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapControllers();

    // Load captured official server data
    var dataDir = Path.Combine(builder.Environment.ContentRootPath, "Data");
    CapturedData.Load(dataDir);

    var tcpServer = app.Services.GetRequiredService<PomeloTcpServer>();
    var tcpTask = tcpServer.StartAsync();

    Log.Information("HTTP API on http://0.0.0.0:8183");
    if (File.Exists(certPath))
        Log.Information("HTTPS API on https://0.0.0.0:443");
    Log.Information("TCP Game Server on port 30531");
    Log.Information("Server started. Press Ctrl+C to stop.");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static async Task<bool> TryServeCdnFile(HttpContext context, string path)
{
    var streamingAssetsRoot = GamePaths.StreamingAssetsRoot;
    var gameRoot = GamePaths.GameRoot;
    var launcherRoot = GamePaths.LauncherRoot;

    var fileName = Path.GetFileName(path);
    if (string.IsNullOrEmpty(fileName)) return false;

    Log.Information("[CDN] TryServeCdnFile: fileName={FileName}", fileName);

    // Handle Version.txt - must be JSON object with version fields
    if (fileName.Equals("Version.txt", StringComparison.OrdinalIgnoreCase))
    {
        var version = "1.0.0.501";
        context.Response.ContentType = "application/json";
        var jsonVersion = "{\"LatestGameVersion\":\"" + version + "\",\"InternalGameVersion\":\"" + version + "\"}";
        await context.Response.WriteAsync(jsonVersion);
        Log.Information("[CDN] Served Version.txt: {Version} path={Path}", version, path);
        return true;
    }

    // Handle PackageManifest files (both .json and .bytes formats)
    if (fileName.StartsWith("PackageManifest_", StringComparison.OrdinalIgnoreCase) &&
        (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || 
         fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase)))
    {
        var isBytes = fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase);
        var extLength = isBytes ? 6 : 5; // .bytes or .json

        // First, try to find exact match
        var foundFiles = Directory.GetFiles(streamingAssetsRoot, fileName, SearchOption.AllDirectories);
        if (foundFiles.Length > 0)
        {
            if (isBytes)
            {
                var fileBytes = await File.ReadAllBytesAsync(foundFiles[0]);
                context.Response.ContentType = "application/octet-stream";
                await context.Response.Body.WriteAsync(fileBytes);
            }
            else
            {
                var content = await File.ReadAllTextAsync(foundFiles[0]);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(content);
            }
            Log.Information("[CDN] Served manifest: {Path} -> {FoundPath}", path, foundFiles[0]);
            return true;
        }

        // Extract requested package name and version (handle duplicated versions like 1.0.0.501.1.0.0.501)
        var nameWithoutExt = fileName.Substring(0, fileName.Length - extLength);
        var parts = nameWithoutExt.Split('_');
        string requestedPkgName = "unknown";
        string requestedPkgVersion = "1.0.0.501";
        if (parts.Length >= 3)
        {
            var rawVersion = parts[parts.Length - 1];
            // Handle duplicated version: 1.0.0.501.1.0.0.501 -> 1.0.0.501
            if (rawVersion.Split('.').Length > 4)
            {
                var verParts = rawVersion.Split('.');
                requestedPkgVersion = string.Join(".", verParts.Take(4));
            }
            else
            {
                requestedPkgVersion = rawVersion;
            }
            requestedPkgName = string.Join("_", parts.Skip(1).Take(parts.Length - 2));
        }

        // Check if this is a Raw package (ends with "Raw")
        if (requestedPkgName.EndsWith("Raw", StringComparison.OrdinalIgnoreCase))
        {
            // Find the base package name and Raw directory
            var basePkgName = requestedPkgName.Substring(0, requestedPkgName.Length - 3); // Remove "Raw"
            var rawDirName = requestedPkgName; // e.g., intothevoidRaw, ResOfficialABResRaw
            
            // Search for the raw files directory
            var rawDirs = Directory.GetDirectories(streamingAssetsRoot, rawDirName, SearchOption.AllDirectories);
            string? rawFilesDir = null;
            foreach (var dir in rawDirs)
            {
                if (Directory.GetFiles(dir, "*.rawfile").Length > 0)
                {
                    rawFilesDir = dir;
                    break;
                }
            }
            
            if (rawFilesDir != null)
            {
                var binaryBytes = YooAssetManifestConverter.GenerateRawPackageManifest(rawFilesDir, requestedPkgName, requestedPkgVersion);
                if (isBytes)
                {
                    context.Response.ContentType = "application/octet-stream";
                    await context.Response.Body.WriteAsync(binaryBytes);
                }
                else
                {
                    // For JSON format, we'd need to convert back - just return binary for now
                    context.Response.ContentType = "application/octet-stream";
                    await context.Response.Body.WriteAsync(binaryBytes);
                }
                Log.Information("[CDN] Generated raw package manifest: {Pkg} v{Ver} ({Bytes} bytes, {Files} files)", 
                    requestedPkgName, requestedPkgVersion, binaryBytes.Length, Directory.GetFiles(rawFilesDir, "*.rawfile").Length);
                return true;
            }
        }

        // Try to find matching local manifest by package name
        var localManifestPath = "";
        var localPkgName = "";
        var localPkgVersion = "";
        
        // Search for all PackageManifest json files
        var allManifests = Directory.GetFiles(streamingAssetsRoot, "PackageManifest_*.json", SearchOption.AllDirectories);
        foreach (var mf in allManifests)
        {
            var mfName = Path.GetFileNameWithoutExtension(mf);
            var mfParts = mfName.Split('_');
            if (mfParts.Length >= 3)
            {
                var mfVersion = mfParts[mfParts.Length - 1];
                var mfPkgName = string.Join("_", mfParts.Skip(1).Take(mfParts.Length - 2));
                // Prefer exact package name match
                if (mfPkgName.Equals(requestedPkgName, StringComparison.OrdinalIgnoreCase))
                {
                    localManifestPath = mf;
                    localPkgName = mfPkgName;
                    localPkgVersion = mfVersion;
                    break;
                }
            }
        }

        // If no exact match, use the first intothevoid manifest as fallback
        if (string.IsNullOrEmpty(localManifestPath))
        {
            var defaultPath = Path.Combine(streamingAssetsRoot, "intothevoid", "intothevoid", "PackageManifest_intothevoid_1.0.0.501.json");
            if (File.Exists(defaultPath))
            {
                localManifestPath = defaultPath;
                localPkgName = "intothevoid";
                localPkgVersion = "1.0.0.501";
            }
        }

        if (!string.IsNullOrEmpty(localManifestPath) && File.Exists(localManifestPath))
        {
            var needsMapping = !localPkgName.Equals(requestedPkgName, StringComparison.OrdinalIgnoreCase) ||
                               !localPkgVersion.Equals(requestedPkgVersion, StringComparison.OrdinalIgnoreCase);

            if (isBytes)
            {
                // Binary format requested - convert JSON to binary
                var jsonContent = await File.ReadAllTextAsync(localManifestPath);
                if (needsMapping)
                {
                    // Modify package name and version in JSON
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonContent);
                    var root = doc.RootElement;
                    using var ms = new System.IO.MemoryStream();
                    using var writer = new System.Text.Json.Utf8JsonWriter(ms, new System.Text.Json.JsonWriterOptions { Indented = true });
                    writer.WriteStartObject();
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.NameEquals("PackageName"))
                        {
                            writer.WriteString("PackageName", requestedPkgName);
                        }
                        else if (prop.NameEquals("PackageVersion"))
                        {
                            writer.WriteString("PackageVersion", requestedPkgVersion);
                        }
                        else
                        {
                            writer.WritePropertyName(prop.Name);
                            prop.Value.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                    writer.Flush();
                    ms.Position = 0;
                    using var reader = new StreamReader(ms);
                    jsonContent = await reader.ReadToEndAsync();
                }
                
                var binaryBytes = YooAssetManifestConverter.ConvertJsonToBinary(jsonContent);
                context.Response.ContentType = "application/octet-stream";
                await context.Response.Body.WriteAsync(binaryBytes);
                Log.Information("[CDN] Converted manifest to binary: {Pkg} v{Ver} ({Bytes} bytes)", requestedPkgName, requestedPkgVersion, binaryBytes.Length);
                return true;
            }
            else
            {
                // JSON format requested
                byte[] resultBytes;
                if (!needsMapping)
                {
                    resultBytes = await File.ReadAllBytesAsync(localManifestPath);
                }
                else
                {
                    var content = await File.ReadAllTextAsync(localManifestPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    using var ms = new System.IO.MemoryStream();
                    using var writer = new System.Text.Json.Utf8JsonWriter(ms, new System.Text.Json.JsonWriterOptions { Indented = true });
                    writer.WriteStartObject();
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.NameEquals("PackageName"))
                        {
                            writer.WriteString("PackageName", requestedPkgName);
                        }
                        else if (prop.NameEquals("PackageVersion"))
                        {
                            writer.WriteString("PackageVersion", requestedPkgVersion);
                        }
                        else
                        {
                            writer.WritePropertyName(prop.Name);
                            prop.Value.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                    writer.Flush();
                    resultBytes = ms.ToArray();
                }
                context.Response.ContentType = "application/json";
                await context.Response.Body.WriteAsync(resultBytes);
                Log.Information("[CDN] Served JSON manifest: {Pkg} v{Ver}", requestedPkgName, requestedPkgVersion);
                return true;
            }
        }

        // Fallback: generate an empty manifest
        var emptyManifest = new
        {
            FileVersion = "2.0.0",
            EnableAddressable = false,
            LocationToLower = true,
            IncludeAssetGUID = false,
            OutputNameStyle = 0,
            BuildPipeline = "ScriptableBuildPipeline",
            PackageName = requestedPkgName,
            PackageVersion = requestedPkgVersion,
            AssetList = Array.Empty<object>(),
            BundleList = Array.Empty<object>()
        };
        var json = System.Text.Json.JsonSerializer.Serialize(emptyManifest);
        if (isBytes)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/octet-stream";
            await context.Response.Body.WriteAsync(bytes);
        }
        else
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(json);
        }
        Log.Information("[CDN] Generated empty manifest: {PackageName} v{Version} ({Format})", requestedPkgName, requestedPkgVersion, isBytes ? "bytes" : "json");
        return true;
    }

    // Handle all .hash files
    if (fileName.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
    {
        // Parse package name and version from hash file name
        var manifestFileName = Path.GetFileNameWithoutExtension(fileName); // Remove .hash
        var nameParts = manifestFileName.Split('_');
        string hashPkgName = "unknown";
        string hashPkgVersion = "1.0.0.501";
        if (nameParts.Length >= 3 && nameParts[0] == "PackageManifest")
        {
            var rawVersion = nameParts[nameParts.Length - 1];
            if (rawVersion.Split('.').Length > 4)
            {
                var verParts = rawVersion.Split('.');
                hashPkgVersion = string.Join(".", verParts.Take(4));
            }
            else
            {
                hashPkgVersion = rawVersion;
            }
            hashPkgName = string.Join("_", nameParts.Skip(1).Take(nameParts.Length - 2));
        }
        
        // Check if this is a Raw package
        if (hashPkgName.EndsWith("Raw", StringComparison.OrdinalIgnoreCase))
        {
            var rawDirName = hashPkgName;
            var rawDirs = Directory.GetDirectories(streamingAssetsRoot, rawDirName, SearchOption.AllDirectories);
            string? rawFilesDir = null;
            foreach (var dir in rawDirs)
            {
                if (Directory.GetFiles(dir, "*.rawfile").Length > 0)
                {
                    rawFilesDir = dir;
                    break;
                }
            }
            
            if (rawFilesDir != null)
            {
                var hash = YooAssetManifestConverter.ComputeRawPackageManifestHash(rawFilesDir, hashPkgName, hashPkgVersion);
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(hash);
                Log.Information("[CDN] Served raw package hash: {FileName} = {Hash}", fileName, hash);
                return true;
            }
        }
        
        // Check if streaming assets directory exists
        if (!Directory.Exists(streamingAssetsRoot))
        {
            Log.Warning("[CDN] StreamingAssets directory not found: {Path}", streamingAssetsRoot);
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("0245bb3f31786fa3ef10191bc389e093");
            return true;
        }
        
        // Try to find the corresponding manifest file and compute its MD5 (binary format)
        var manifestFiles = Directory.GetFiles(streamingAssetsRoot, manifestFileName, SearchOption.AllDirectories);
        if (manifestFiles.Length == 0)
        {
            // Try with .json extension
            var jsonManifest = manifestFileName + ".json";
            manifestFiles = Directory.GetFiles(streamingAssetsRoot, jsonManifest, SearchOption.AllDirectories);
        }
        if (manifestFiles.Length == 0)
        {
            // Try to find any manifest for this package (handle version mismatch) - use already parsed pkgName
            if (!string.IsNullOrEmpty(hashPkgName) && hashPkgName != "unknown")
            {
                var searchPattern = $"PackageManifest_{hashPkgName}_*.json";
                manifestFiles = Directory.GetFiles(streamingAssetsRoot, searchPattern, SearchOption.AllDirectories);
            }
        }
        
        if (manifestFiles.Length > 0)
        {
            // Read JSON and convert to binary, then compute MD5
            var jsonContent = await File.ReadAllTextAsync(manifestFiles[0]);
            
            // Check if we need to map version (handle duplicated versions)
            var hashNameParts = manifestFileName.Split('_');
            if (hashNameParts.Length >= 3)
            {
                var rawVersion = hashNameParts[hashNameParts.Length - 1];
                if (rawVersion.Split('.').Length > 4)
                {
                    // Duplicated version - need to update version in JSON before converting
                    var verParts = rawVersion.Split('.');
                    var targetVersion = string.Join(".", verParts.Take(4));
                    
                    // Check if local manifest version is different
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonContent);
                    var pkgVer = doc.RootElement.GetProperty("PackageVersion").GetString();
                    if (pkgVer != targetVersion)
                    {
                        // Modify version in JSON
                        var root = doc.RootElement;
                        using var ms = new System.IO.MemoryStream();
                        using var writer = new System.Text.Json.Utf8JsonWriter(ms, new System.Text.Json.JsonWriterOptions { Indented = true });
                        writer.WriteStartObject();
                        foreach (var prop in root.EnumerateObject())
                        {
                            if (prop.NameEquals("PackageVersion"))
                            {
                                writer.WriteString("PackageVersion", targetVersion);
                            }
                            else
                            {
                                writer.WritePropertyName(prop.Name);
                                prop.Value.WriteTo(writer);
                            }
                        }
                        writer.WriteEndObject();
                        writer.Flush();
                        ms.Position = 0;
                        using var reader = new StreamReader(ms);
                        jsonContent = await reader.ReadToEndAsync();
                    }
                }
            }
            
            var binaryBytes = YooAssetManifestConverter.ConvertJsonToBinary(jsonContent);
            var hash = YooAssetManifestConverter.ComputeMD5Hash(binaryBytes);
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(hash);
            Log.Information("[CDN] Served binary hash: {FileName} = {Hash} (from {Source})", fileName, hash, Path.GetFileName(manifestFiles[0]));
            return true;
        }

        // Fallback hash
        var fallbackHash = "0245bb3f31786fa3ef10191bc389e093";
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(fallbackHash);
        Log.Information("[CDN] Served fallback hash file: {FileName}", fileName);
        return true;
    }

    // Handle newLauncher config files
    if (path.StartsWith("newLauncher/", StringComparison.OrdinalIgnoreCase))
    {
        var configFile = path.Substring("newLauncher/".Length);
        Log.Information("[CDN] NewLauncher config request: {Config}", configFile);

        // Try to find in game root or launcher root
        var gamePath = Path.Combine(gameRoot, configFile);
        if (System.IO.File.Exists(gamePath))
        {
            var content = await System.IO.File.ReadAllTextAsync(gamePath);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(content);
            Log.Information("[CDN] Served newLauncher config from: {Path}", gamePath);
            return true;
        }

        var launcherPath = Path.Combine(launcherRoot, configFile);
        if (System.IO.File.Exists(launcherPath))
        {
            var content = await System.IO.File.ReadAllTextAsync(launcherPath);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(content);
            Log.Information("[CDN] Served newLauncher config from: {Path}", launcherPath);
            return true;
        }

        // login_channel.json - generate dynamically
        if (configFile.Equals("login_channel.json", StringComparison.OrdinalIgnoreCase))
        {
            var json = @"{""channel"":""official"",""launcher_channel"":2}";
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(json);
            Log.Information("[CDN] Served generated login_channel.json");
            return true;
        }

        Log.Warning("[CDN] NewLauncher config not found: {Config}", configFile);
        return false;
    }

    // For manifest/info files, try to serve from game root
    if (fileName.EndsWith("_info.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".info", StringComparison.OrdinalIgnoreCase))
    {
        var infoPath = Path.Combine(gameRoot, fileName);
        if (System.IO.File.Exists(infoPath))
        {
            var content = await System.IO.File.ReadAllTextAsync(infoPath);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(content);
            Log.Information("[CDN] Served info file: {Path}", infoPath);
            return true;
        }
    }

    try
    {
        var foundFiles = Directory.GetFiles(streamingAssetsRoot, fileName, SearchOption.AllDirectories);
        if (foundFiles.Length > 0)
        {
            Log.Information("[CDN] Serving: {Path} -> {FoundPath}", path, foundFiles[0]);
            var fileBytes = await File.ReadAllBytesAsync(foundFiles[0]);
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength = fileBytes.Length;
            await context.Response.Body.WriteAsync(fileBytes);
            return true;
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[CDN] Error serving file: {Path}", path);
    }

    Log.Warning("[CDN] File not found: {Path}", path);
    return false;
}
