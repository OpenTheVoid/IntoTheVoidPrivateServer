// Auto-generated from official server pcap capture
// DO NOT EDIT MANUALLY

using System.Collections.Generic;
using System.IO;
using Serilog;

namespace IntoTheVoidServer.Pomelo;

public static class CapturedData
{
    public static readonly Dictionary<string, byte[]> Responses = new();
    public static readonly List<(string route, byte[] data)> Pushes = new();

    public static void Load(string dataDir)
    {
        var respDir = Path.Combine(dataDir, "responses");
        if (Directory.Exists(respDir))
        {
            foreach (var file in Directory.GetFiles(respDir, "*.bin"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var route = name.Replace("_", ".");
                Responses[route] = File.ReadAllBytes(file);
            }
        }

        var pushDir = Path.Combine(respDir, "pushes");
        if (Directory.Exists(pushDir))
        {
            foreach (var file in Directory.GetFiles(pushDir, "*.bin"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var lastUnderscore = name.LastIndexOf('_');
                var route = lastUnderscore > 0
                    ? name.Substring(0, lastUnderscore).Replace("_", ".")
                    : name.Replace("_", ".");
                var data = File.ReadAllBytes(file);
                Pushes.Add((route, data));
            }
        }

        Log.Information("Loaded {RespCount} captured responses and {PushCount} pushes",
            Responses.Count, Pushes.Count);
    }

    /// <summary>
    /// 按登录账号切换响应集（多账号存档隔离）。
    ///
    /// 玩家的背包/任务/等级等状态全部由这批"捕获响应"承载，因此登录哪个账号就装载哪份
    /// Data/saves/&lt;uid&gt;/responses。空档账号目录存在但为空 → Responses 清空，客户端请求
    /// 全部落到 MessageRouter 的默认响应，即"从零开始"。
    /// 推送集（pushes）若该账号没有自带，则沿用启动时载入的公共推送。
    /// </summary>
    public static void LoadForPlayer(string root, string uid)
    {
        var respDir = Path.Combine(root, "Data", "saves", uid, "responses");
        Responses.Clear();

        if (Directory.Exists(respDir))
        {
            foreach (var file in Directory.GetFiles(respDir, "*.bin"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                Responses[name.Replace("_", ".")] = File.ReadAllBytes(file);
            }
        }

        var pushDir = Path.Combine(respDir, "pushes");
        if (Directory.Exists(pushDir) && Directory.GetFiles(pushDir, "*.bin").Length > 0)
        {
            Pushes.Clear();
            foreach (var file in Directory.GetFiles(pushDir, "*.bin"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var lastUnderscore = name.LastIndexOf('_');
                var route = lastUnderscore > 0
                    ? name.Substring(0, lastUnderscore).Replace("_", ".")
                    : name.Replace("_", ".");
                Pushes.Add((route, File.ReadAllBytes(file)));
            }
        }

        Log.Information("[CapturedData] 账号 uid={Uid} 载入 {RespCount} 个响应 (存档目录存在={Exists})",
            uid, Responses.Count, Directory.Exists(respDir));
    }
}
