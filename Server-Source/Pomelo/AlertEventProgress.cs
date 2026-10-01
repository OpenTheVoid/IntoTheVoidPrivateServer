using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Serilog;

namespace IntoTheVoidServer.Pomelo;

/// <summary>
/// 警报关卡「悖域巡查」(突击警报 / AlertType="sortie") 的进度跟踪与动态响应。
///
/// 背景 (2026-09-26)
/// ----------------
/// 客户端 MapProgressLogic:OnAlertEventInfoResponse 把服务端下发的
/// AlertEventInfoResponse 存成 SortieLevelDic：
///   AlertEventInfo { 1=AlertType "sortie", 2=Levels[]{1=LevelID,2=BuffIDs,3=ModIDs},
///                    3=NextRefreshTime, 4=OpenFlag, 5=CurrentIndex }
/// 手册 -> 事件副本 -> 悖域巡查 面板 EventDungeonPanel:GetDailyParadoxData 用它算每个关卡状态：
///   state = CurrentIndex - 1basedIndex      (>0 已完成 / ==0 进行中[可挑战] / <0 未完成[锁定])
/// 地图节点 MapLevelItem_Base 则用 MapProgressLogic:GetSortieLevelCurrentStep：
///   nodeIndex == CurrentIndex 才把该节点画成"当前可打"的悖域巡查关卡。
///
/// 也就是说 **CurrentIndex = 当前应该打的那一关的 1-based 序号**（= 已通关数 + 1）。
///
/// 私服原先对 game.game.AlertEventInfoRequest 直接回放官方抓包快照
/// (Data/saves/&lt;uid&gt;/responses/…bin)，里面的 CurrentIndex 是抓包当时的定值（=1）。
/// 于是"通关第 1 关后第 2 关永不解锁"——服务端每次都把旧快照原样发回去。
///
/// 修复
/// ----
/// 1. 在静态捕获响应之前插入"动态响应"层，对 AlertEventInfoRequest 把 sortie 段的
///    <c>CurrentIndex</c> 改写成服务端维护的真实进度（其余字段与其它 AlertType 原样保留）。
/// 2. 玩家通关时客户端会上报 game.game.SettleDataRequest
///    (1=LevelClear bool, 2=CityID, 3=LevelID)，据此推进进度并落盘
///    Data/saves/&lt;uid&gt;/alert_progress.json（跨重启保留，账号隔离）。
/// 3. 推进后主动推送 <c>gate.AlertEventPush</c>（客户端 MessageTypeMap 里的键名就是它），
///    客户端 MapProgressLogic:OnAlertEventPush 会立刻重发 AlertEventInfoRequest，
///    于是"通关 -> 第二关解锁"当场生效，不必重登。
///
/// 该步骤全部在服务端完成，客户端配置表 / IL 均无需改动。
/// </summary>
public static class AlertEventProgress
{
    /// <summary>客户端请求告警信息的路由。</summary>
    public const string InfoRoute = "game.game.AlertEventInfoRequest";

    /// <summary>客户端"本轮红点是否已看过"的上报路由（回显 AlertType 即可清红点）。</summary>
    public const string RefreshCheckRoute = "game.game.AlertEventRefreshCheckRequest";

    /// <summary>推进后用于催促客户端重新拉取告警信息的推送路由（官方抓包键名）。</summary>
    public const string PushRoute = "gate.AlertEventPush";

    /// <summary>捕获响应缺失时的兜底 sortie 关卡列表（官方包 sortie 段实测顺序）。</summary>
    private static readonly int[] FallbackLevels = { 43431000, 43431026, 43431042 };

    private static readonly object Gate = new();
    private static int[] _levels = FallbackLevels;
    private static int _currentIndex = 1;
    private static string? _statePath;

    /// <summary>当前"应该打的那一关"的 1-based 序号。</summary>
    public static int CurrentIndex { get { lock (Gate) { return _currentIndex; } } }

    /// <summary>当前赛季的 sortie 关卡顺序（与 CurrentIndex 同序）。</summary>
    public static int[] Levels { get { lock (Gate) { return (int[])_levels.Clone(); } } }

    // ==================================================================
    // 生命周期
    // ==================================================================

    /// <summary>
    /// 登录时切换到该账号的进度。必须在 CapturedData.LoadForPlayer 之后调用
    /// —— 需要读该账号的 AlertEventInfoResponse 快照作为基线（其中的 CurrentIndex
    /// 是官方抓包时的进度，用作下限，保证不会比原档更"倒退"）。
    /// </summary>
    public static void ActivatePlayer(string root, string uid)
    {
        lock (Gate)
        {
            _statePath = Path.Combine(root, "Data", "saves", uid, "alert_progress.json");
            _levels = FallbackLevels;
            int baseline = 0;
            var src = "兜底常量";

            if (CapturedData.Responses.TryGetValue(InfoRoute, out var captured)
                && captured != null && captured.Length > 0)
            {
                var parsed = ParseSortie(captured);
                if (parsed != null)
                {
                    if (parsed.Value.Levels.Length > 0) _levels = parsed.Value.Levels;
                    baseline = parsed.Value.CurrentIndex;
                    src = "账号快照";
                }
            }

            if (baseline <= 0) baseline = 1;          // CurrentIndex 从 1 开始（第 1 关为当前关）
            var stored = LoadIndex();
            if (stored > baseline) baseline = stored;
            _currentIndex = Math.Max(1, baseline);

            Log.Information(
                "[AlertEvent] uid={Uid} 悖域巡查: 关卡={Levels} CurrentIndex={Idx} (来源={Src}, 存档={Stored})",
                uid, string.Join(",", _levels), _currentIndex, src, stored);

            if (!string.IsNullOrEmpty(_statePath) && !File.Exists(_statePath)) Save();
        }
    }

    // ==================================================================
    // 动态响应（在静态捕获之前被调用）
    // ==================================================================

    /// <summary>
    /// 需要服务端动态生成的响应。返回 true 表示已接管该路由（response 即响应体，
    /// 允许为 null 表示"已知但不回包"）。
    /// </summary>
    public static bool TryBuildDynamicResponse(string route, byte[]? requestPayload, out byte[]? response)
    {
        response = null;

        if (route == InfoRoute)
        {
            lock (Gate)
            {
                if (CapturedData.Responses.TryGetValue(route, out var captured)
                    && captured != null && captured.Length > 0)
                {
                    response = RewriteSortieIndex(captured, _currentIndex) ?? captured;
                }
                else
                {
                    response = BuildFromScratch(_levels, _currentIndex);
                }
            }
            return true;
        }

        if (route == RefreshCheckRoute)
        {
            // AlertEventRefreshCheckResponse { 1=AlertType string } —— 回显即可清红点。
            // 注意：即使没有 AlertType 也必须回一个（空）响应，否则该 Request 得不到
            // 回执，客户端会一直挂在等待列表里。
            var alertType = ReadStringField(requestPayload, 1);
            if (string.IsNullOrEmpty(alertType))
            {
                response = Array.Empty<byte>();
                return true;
            }
            using var ms = new MemoryStream();
            WriteTag(ms, 1, 2);
            var raw = Encoding.UTF8.GetBytes(alertType!);
            WriteVarint(ms, (ulong)raw.Length);
            ms.Write(raw, 0, raw.Length);
            response = ms.ToArray();
            return true;
        }

        return false;
    }

    // ==================================================================
    // 进度推进（SettleDataRequest）
    // ==================================================================

    /// <summary>
    /// 处理 SettleDataRequest（1=LevelClear bool, 2=CityID, 3=LevelID）。
    /// 若通报的是"通关了某个 sortie 关卡"，则把 CurrentIndex 推进到该关的下一关。
    /// 返回 true 表示进度有变化（调用方应推送 gate.AlertEventPush）。
    /// </summary>
    public static bool OnLevelSettled(byte[]? payload)
    {
        if (payload == null || payload.Length == 0) return false;
        if (!Walk(payload, 0, payload.Length, out var fields)) return false;

        var clear = false;
        var levelId = 0;
        foreach (var f in fields)
        {
            if (f.WireType != 0) continue;
            var i = f.ValueStart;
            if (!TryReadVarint(payload, ref i, f.ValueEnd, out var v)) continue;
            if (f.Number == 1) clear = v != 0;
            else if (f.Number == 3) levelId = (int)v;
        }

        if (!clear || levelId == 0) return false;

        lock (Gate)
        {
            var idx = Array.IndexOf(_levels, levelId);
            if (idx < 0) return false;                 // 不是本期 sortie 关卡
            var next = idx + 2;                        // 1-based 序号 + 1 = 下一关
            if (next <= _currentIndex) return false;   // 重复通关/乱序，不回退
            _currentIndex = next;
            Save();
            Log.Information("[AlertEvent] 悖域巡查通关 LevelID={LevelId} -> 下一关 CurrentIndex={Idx} (关卡表={Levels})",
                levelId, _currentIndex, string.Join(",", _levels));
            return true;
        }
    }

    // ==================================================================
    // 持久化
    // ==================================================================

    private sealed class StateDto
    {
        public int SortieIndex { get; set; }
        public int[]? SortieLevels { get; set; }
        public string UpdatedAt { get; set; } = "";
    }

    private static int LoadIndex()
    {
        try
        {
            if (string.IsNullOrEmpty(_statePath) || !File.Exists(_statePath)) return 0;
            var dto = JsonSerializer.Deserialize<StateDto>(File.ReadAllText(_statePath));
            return dto?.SortieIndex ?? 0;
        }
        catch (Exception ex)
        {
            Log.Warning("[AlertEvent] 读取进度失败: {Msg}", ex.Message);
            return 0;
        }
    }

    private static void Save()
    {
        if (string.IsNullOrEmpty(_statePath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var dto = new StateDto
            {
                SortieIndex = _currentIndex,
                SortieLevels = _levels,
                UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };
            File.WriteAllText(_statePath,
                JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Warning("[AlertEvent] 写入进度失败: {Msg}", ex.Message);
        }
    }

    // ==================================================================
    // 构造 / 改写 AlertEventInfoResponse
    // ==================================================================

    /// <summary>把 sortie 段的 field5 (CurrentIndex) 改写成 index，其余字节原样保留。</summary>
    private static byte[]? RewriteSortieIndex(byte[] msg, int index)
    {
        if (!Walk(msg, 0, msg.Length, out var top)) return null;
        using var outp = new MemoryStream();
        var touched = false;

        foreach (var f in top)
        {
            if (f.Number == 1 && f.WireType == 2)
            {
                var sub = new byte[f.ValueEnd - f.ValueStart];
                Array.Copy(msg, f.ValueStart, sub, 0, sub.Length);
                var rebuilt = RewriteInfo(sub, index);
                if (rebuilt != null)
                {
                    WriteTag(outp, 1, 2);
                    WriteVarint(outp, (ulong)rebuilt.Length);
                    outp.Write(rebuilt, 0, rebuilt.Length);
                    touched = true;
                    continue;
                }
            }
            outp.Write(msg, f.TagStart, f.ValueEnd - f.TagStart);
        }

        return touched ? outp.ToArray() : null;
    }

    /// <summary>若该 AlertEventInfo 是 sortie，则重建它（丢弃旧 field5，追加新 field5）。</summary>
    private static byte[]? RewriteInfo(byte[] info, int index)
    {
        if (!Walk(info, 0, info.Length, out var fields)) return null;
        var isSortie = false;
        foreach (var f in fields)
            if (f.Number == 1 && f.WireType == 2
                && Encoding.UTF8.GetString(info, f.ValueStart, f.ValueEnd - f.ValueStart) == "sortie")
                isSortie = true;
        if (!isSortie) return null;

        using var outp = new MemoryStream();
        foreach (var f in fields)
        {
            if (f.Number == 5 && f.WireType == 0) continue;       // 旧 CurrentIndex
            outp.Write(info, f.TagStart, f.ValueEnd - f.TagStart);
        }
        WriteTag(outp, 5, 0);
        WriteVarint(outp, (ulong)Math.Max(0, index));
        return outp.ToArray();
    }

    /// <summary>无快照时从零构造：只下发 sortie 段。</summary>
    private static byte[] BuildFromScratch(int[] levels, int index)
    {
        using var info = new MemoryStream();

        // 1 = AlertType
        var t = Encoding.UTF8.GetBytes("sortie");
        WriteTag(info, 1, 2);
        WriteVarint(info, (ulong)t.Length);
        info.Write(t, 0, t.Length);

        // 2 = repeated AlertLevel { 1=LevelID }
        foreach (var lv in levels)
        {
            using var l = new MemoryStream();
            WriteTag(l, 1, 0);
            WriteVarint(l, (ulong)lv);
            var bytes = l.ToArray();
            WriteTag(info, 2, 2);
            WriteVarint(info, (ulong)bytes.Length);
            info.Write(bytes, 0, bytes.Length);
        }

        // 4 = OpenFlag = true
        WriteTag(info, 4, 0);
        WriteVarint(info, 1);

        // 5 = CurrentIndex
        WriteTag(info, 5, 0);
        WriteVarint(info, (ulong)Math.Max(0, index));

        var infoBytes = info.ToArray();
        using var top = new MemoryStream();
        WriteTag(top, 1, 2);
        WriteVarint(top, (ulong)infoBytes.Length);
        top.Write(infoBytes, 0, infoBytes.Length);
        return top.ToArray();
    }

    /// <summary>从 AlertEventInfoResponse 中取 sortie 段的关卡列表与 CurrentIndex。</summary>
    private static (int[] Levels, int CurrentIndex)? ParseSortie(byte[] msg)
    {
        if (!Walk(msg, 0, msg.Length, out var top)) return null;
        foreach (var f in top)
        {
            if (f.Number != 1 || f.WireType != 2) continue;
            if (!Walk(msg, f.ValueStart, f.ValueEnd, out var info)) continue;

            string? type = null;
            var levels = new List<int>();
            var idx = 0;
            foreach (var g in info)
            {
                if (g.Number == 1 && g.WireType == 2)
                {
                    type = Encoding.UTF8.GetString(msg, g.ValueStart, g.ValueEnd - g.ValueStart);
                }
                else if (g.Number == 2 && g.WireType == 2)
                {
                    if (!Walk(msg, g.ValueStart, g.ValueEnd, out var lv)) continue;
                    foreach (var h in lv)
                    {
                        if (h.Number != 1 || h.WireType != 0) continue;
                        var i = h.ValueStart;
                        if (TryReadVarint(msg, ref i, h.ValueEnd, out var v)) levels.Add((int)v);
                    }
                }
                else if (g.Number == 5 && g.WireType == 0)
                {
                    var i = g.ValueStart;
                    if (TryReadVarint(msg, ref i, g.ValueEnd, out var v)) idx = (int)v;
                }
            }

            if (type == "sortie") return (levels.ToArray(), idx);
        }
        return null;
    }

    // ==================================================================
    // 极简 protobuf 读写工具
    // ==================================================================

    private readonly struct Field
    {
        public readonly int Number;
        public readonly int WireType;
        public readonly int TagStart;
        public readonly int ValueStart;
        public readonly int ValueEnd;

        public Field(int number, int wireType, int tagStart, int valueStart, int valueEnd)
        {
            Number = number; WireType = wireType;
            TagStart = tagStart; ValueStart = valueStart; ValueEnd = valueEnd;
        }
    }

    private static bool Walk(byte[] b, int start, int end, out List<Field> fields)
    {
        fields = new List<Field>();
        var i = start;
        while (i < end)
        {
            var tagStart = i;
            if (!TryReadVarint(b, ref i, end, out var tag)) return false;
            var number = (int)(tag >> 3);
            var wire = (int)(tag & 7);
            int valueStart;
            int valueEnd;
            switch (wire)
            {
                case 0:
                    valueStart = i;
                    if (!TryReadVarint(b, ref i, end, out _)) return false;
                    valueEnd = i;
                    break;
                case 2:
                    if (!TryReadVarint(b, ref i, end, out var len)) return false;
                    valueStart = i;
                    if (len < 0 || len > int.MaxValue) return false;
                    valueEnd = i + (int)len;
                    if (valueEnd > end || valueEnd < valueStart) return false;
                    i = valueEnd;
                    break;
                case 5:
                    valueStart = i;
                    valueEnd = i + 4;
                    if (valueEnd > end) return false;
                    i = valueEnd;
                    break;
                case 1:
                    valueStart = i;
                    valueEnd = i + 8;
                    if (valueEnd > end) return false;
                    i = valueEnd;
                    break;
                default:
                    return false;
            }
            fields.Add(new Field(number, wire, tagStart, valueStart, valueEnd));
        }
        return true;
    }

    private static bool TryReadVarint(byte[] b, ref int i, int end, out long value)
    {
        value = 0;
        var shift = 0;
        while (i < end)
        {
            var c = b[i++];
            value |= (long)(c & 0x7F) << shift;
            if ((c & 0x80) == 0) return true;
            shift += 7;
            if (shift > 63) return false;
        }
        return false;
    }

    private static void WriteVarint(Stream s, ulong value)
    {
        while (value >= 0x80)
        {
            s.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        s.WriteByte((byte)value);
    }

    private static void WriteTag(Stream s, int number, int wireType)
        => WriteVarint(s, ((ulong)number << 3) | (ulong)wireType);

    private static string? ReadStringField(byte[]? msg, int number)
    {
        if (msg == null || msg.Length == 0) return null;
        if (!Walk(msg, 0, msg.Length, out var fields)) return null;
        foreach (var f in fields)
            if (f.Number == number && f.WireType == 2)
                return Encoding.UTF8.GetString(msg, f.ValueStart, f.ValueEnd - f.ValueStart);
        return null;
    }
}
