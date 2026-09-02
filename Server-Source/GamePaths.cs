using Serilog;

namespace IntoTheVoidServer;

/// <summary>
/// 游戏安装目录的统一路径解析。
/// 服务端部署在 <游戏目录>\IntoTheVoidServer，因此游戏目录就是内容根目录的上一级。
/// 推导优先级：
///   1. 环境变量 UCS_GAME_ROOT（显式指定，最优先）
///   2. 从 contentRoot 向上逐级探测包含 IntoTheVoid.exe 的目录（支撑任意搬迁/换盘）
///   3. 从当前进程目录向上探测（兜底）
/// 不再使用硬编码绝对路径，保证交付给任何人换盘后仍可用。
/// </summary>
internal static class GamePaths
{
    private const string GameExe = "IntoTheVoid.exe";

    public static string GameRoot { get; private set; } = "";
    public static string LauncherRoot { get; private set; } = "";
    public static string StreamingAssetsRoot { get; private set; } = "";

    public static void Initialize(string contentRoot)
    {
        var root = ResolveGameRoot(contentRoot);
        GameRoot = root;
        LauncherRoot = Directory.GetParent(
            GameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName ?? "";
        StreamingAssetsRoot = Path.Combine(GameRoot, "IntoTheVoid_Data", "StreamingAssets");

        Log.Information("[Paths] GameRoot={GameRoot}", GameRoot);
        Log.Information("[Paths] StreamingAssets={StreamingAssets} exists={Exists}",
            StreamingAssetsRoot, Directory.Exists(StreamingAssetsRoot));
    }

    private static string ResolveGameRoot(string contentRoot)
    {
        // 1. 环境变量显式指定（例如交付时对方换盘后可设置）
        var env = Environment.GetEnvironmentVariable("UCS_GAME_ROOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, GameExe)))
        {
            Log.Information("[Paths] Using UCS_GAME_ROOT={Env}", env);
            return env;
        }

        // 2. 从 contentRoot 向上逐级探测存在 IntoTheVoid.exe 的目录
        var dir = (contentRoot ?? "").TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var found = ProbeUpward(dir);
        if (found != null)
        {
            Log.Information("[Paths] Detected game root by probing upward: {Found}", found);
            return found;
        }

        // 3. 从当前进程所在目录向上探测（+处理直接放在游戏根目录上方的场景）
        var processDir = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        found = ProbeUpward(processDir);
        if (found != null)
        {
            Log.Information("[Paths] Detected game root by probing process dir: {Found}", found);
            return found;
        }

        Log.Warning("[Paths] Could not detect game root; StreamingAssets CDN will be unavailable.");
        return dir;
    }

    private static string? ProbeUpward(string startDir)
    {
        var d = startDir;
        while (!string.IsNullOrEmpty(d) && Directory.Exists(d))
        {
            if (File.Exists(Path.Combine(d, GameExe)))
                return d;
            var parent = Directory.GetParent(d)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent == d)
                break;
            d = parent;
        }
        return null;
    }
}
