using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Serilog;

namespace IntoTheVoidServer.Pomelo;

/// <summary>
/// 手册 → 事件副本 → 「悖域回归」(局外周本 / ParadoxZone) 的进度跟踪与动态响应。
///
/// 背景 (2026-09-26)
/// ----------------
/// 「悖域回归」面板 = Lua 的 <c>ParadoxZonePanel</c>（EventDungeonPanel 里
/// HandBookEventType.ParadoxZone → Event 60500007），它显示进度时既不读 Event 表、
/// 也不读 Level 表，而是读 <c>MapProgressLogic:GetCurrWeeklyInfo()</c>，
/// 也就是服务端下发的 <c>WeeklyQuestInfoResponse</c>（= <c>self.svrWeeklyInfo</c>）：
///
///   WeeklyQuestInfoResponse {
///     1 = RefreshTimestamp      (long)
///     2 = LastRefreshTimestamp  (long)
///     3 = repeated int32 ChoseQuests   // 已解锁(可挑战)的周本任务，最后一个 = "当前任务"
///     4 = repeated int32 ShowLines     // 本期全部周本任务(按顺序)
///     5 = bool IsFrontOk               // 前置是否满足(未满足则按钮显示提示语且不可点)
///     6 = bool IsNewRefreshChecked
///   }
///
/// 面板 <c>RefreshPrivateDataPanel</c> 的算法：
///   currQuestID = ChoseQuests 里最后一个非零值
///   currPress   = ShowLines 里 currQuestID 的下标
///   bFinished   = currQuestID 无效(ChoseQuests 为空)
///   进度文本    = "currPress / ShowLines.Count"
///   按钮        = IsFrontOk ? (bFinished ? "已完成"(不可点) : "前往") : 提示语(不可点)
///
/// 地图节点则靠 <c>MapProgressLogic:WeeklyQuestInfoResponseEvent</c>：
///   ChoseQuests 里每个 QuestID → GetQuestSht(QuestID).OpenLevel[0]
///   → weeklyLevelDic[levelSht.LinkNode] = { RefreshTimestamp, WeeklyLevelID }
///   ⇒ **只有出现在 ChoseQuests 里的任务，其关卡才会在地图上解锁**。
///
/// 也就是说 **ChoseQuests 的长度 = 已解锁任务数**；通关当前任务后服务端应把它 +1
/// （把下一个任务的 QuestID 也放进 ChoseQuests）。全部通关时 ChoseQuests 变为空
/// （bFinished → 进度显示 Count/Count + 按钮"已完成"）。
///
/// 私服原先对 game.game.WeeklyQuestInfoRequest 直接回放官方抓包快照
/// (Data/saves/&lt;uid&gt;/responses/…bin)，ChoseQuests 是抓包当时的定值
/// （本期抓包 = [34871103]，而 ShowLines = [34871103,34871104,34871105]）。
/// 于是"通关第 1 个周本任务后第 2 个永不解锁"——服务端每次都把旧快照原样发回去。
///
/// 修复
/// ----
/// 1. 在静态捕获响应之前插入"动态响应"层，对 WeeklyQuestInfoRequest 重建
///    ChoseQuests = ShowLines[0 .. 已解锁数-1]（其余字段原样保留）。
/// 2. 玩家通关时客户端会上报 game.game.SettleDataRequest
///    (1=LevelClear bool, 2=CityID, 3=LevelID)，据此把"已解锁数"推进一格并落盘
///    Data/saves/&lt;uid&gt;/weekly_progress.json（跨重启保留，账号隔离）。
/// 3. 推进后主动推送 <c>gate.WeeklyQuestNoticePush</c>（客户端 MessageTypeMap 里的
///    键名就是它），客户端 MapProgressLogic 把它绑到 SendWeeklyListRequest，
///    会立刻重发 WeeklyQuestInfoRequest，于是"通关 → 下个任务解锁"当场生效，不必重登。
///
/// 该步骤全部在服务端完成，客户端配置表 / IL 均无需改动。
/// </summary>
public static class WeeklyProgress
{
    /// <summary>客户端请求周本(悖域回归)信息的路由。</summary>
    public const string InfoRoute = "game.game.WeeklyQuestInfoRequest";

    /// <summary>客户端"本轮红点是否已看过"的上报路由（回空响应即可清红点）。</summary>
    public const string CheckRoute = "game.game.WeeklyQuestRefreshCheckRequest";

    /// <summary>推进后用于催促客户端重新拉取周本信息的推送路由（官方 MessageTypeMap 键名）。</summary>
    public const string PushRoute = "gate.WeeklyQuestNoticePush";

    // ------------------------------------------------------------------
    // 本期(2026-09 抓包)「局外周本_奇点」三个任务与其关卡。
    // 私服的赛季被静态快照冻结（WeeklyQuestInfoResponse 的 RefreshTimestamp 恒为抓包值），
    // 所以这里直接内建；若将来换成别的赛季，把这两行与下面 QuestToLevel 一起改掉即可。
    //   Quest 34871103 "1-2_局外周本_奇点_任务一" -> Level 43430303 (_LinkNode 43600002)
    //   Quest 34871104 "2-3_局外周本_奇点_任务二" -> Level 43430304 (_LinkNode 43600011)
    //   Quest 34871105 "2-7_局外周本_奇点_任务三" -> Level 43430305 (_LinkNode 43600104)
    //   Quest._Goto 链: 34871103 -> 34871104 -> 34871105 -> [0]
    // ------------------------------------------------------------------
    private static readonly int[] FallbackShowLines = { 34871103, 34871104, 34871105 };
    private static readonly int[] FallbackLevels = { 43430303, 43430304, 43430305 };

    private static readonly Dictionary<int, int> QuestToLevel = new Dictionary<int, int>
    {
        { 34871103, 43430303 },
        { 34871104, 43430304 },
        { 34871105, 43430305 },
    };

    private static readonly object Gate = new object();
    private static int[] _showLines = FallbackShowLines;
    private static int[] _levels = FallbackLevels;

    /// <summary>
    /// 已解锁任务数 = 下发的 ChoseQuests 长度。
    /// 取值区间 [1, ShowLines.Length]；等于 ShowLines.Length + 1 表示本期全部通关
    /// （此时下发空 ChoseQuests，面板进入"已完成"）。
    /// </summary>
    private static int _unlocked = 1;

    private static string? _statePath;

    /// <summary>当前已解锁任务数（ChoseQuests 长度）。</summary>
    public static int UnlockedCount { get { lock (Gate) { return _unlocked; } } }

    /// <summary>本期周本任务顺序（与 Levels 同序）。</summary>
    public static int[] ShowLines { get { lock (Gate) { return (int[])_showLines.Clone(); } } }

    /// <summary>本期周本任务对应的关卡顺序。</summary>
    public static int[] Levels { get { lock (Gate) { return (int[])_levels.Clone(); } } }

    // ==================================================================
    // 生命周期
    // ==================================================================

    /// <summary>
    /// 登录时切换到该账号的进度。必须在 CapturedData.LoadForPlayer 之后调用
    /// —— 需要读该账号的 WeeklyQuestInfoResponse 快照作为基线（其中的 ChoseQuests
    /// 长度是官方抓包时的已解锁数，用作下限，保证不会比原档更"倒退"）。
    /// </summary>
    public static void ActivatePlayer(string root, string uid)
    {
        lock (Gate)
        {
            _statePath = Path.Combine(root, "Data", "saves", uid, "weekly_progress.json");
            _showLines = FallbackShowLines;
            _levels = FallbackLevels;
            _template = null;            // 账号快照随 LoadForPlayer 变化，模板必须失效

            var baseline = 0;
            var src = "兜底常量";

            if (CapturedData.Responses.TryGetValue(InfoRoute, out var captured)
                && captured != null && captured.Length > 0)
            {
                var info = Parse(captured);
                if (info != null)
                {
                    if (info.ShowLines.Length > 0) _showLines = info.ShowLines;
                    baseline = info.ChoseQuests.Length;
                    src = "账号快照";
                }
            }

            _levels = ResolveLevels(_showLines);

            if (baseline <= 0) baseline = 1;                 // 至少第 1 个任务是解锁的
            var stored = LoadUnlocked();
            if (stored > baseline) baseline = stored;
            _unlocked = Clamp(baseline, 1, _showLines.Length + 1);

            Log.Information(
                "[Weekly] uid={Uid} 悖域回归: 任务={ShowLines} 关卡={Levels} 已解锁={Unlocked}/{Total} (来源={Src}, 存档={Stored})",
                uid, string.Join(",", _showLines), string.Join(",", _levels),
                _unlocked, _showLines.Length, src, stored);

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
                var info = LoadTemplate();
                response = Serialize(info, ComputeChoseQuests(_unlocked, info.ShowLines));
            }
            return true;
        }

        if (route == CheckRoute)
        {
            // WeeklyQuestRefreshCheckResponse —— 客户端 EventVM:SendWeelklyCheckRequest 只在
            // MapProgressLogic.weeklySeasonCheckd 为 false 时才发；正常流程里它已被
            // WeeklyQuestInfoResponse.IsNewRefreshChecked 置 true，所以基本不会出现。
            // 仍然回一个空响应，避免偶发情况下该 Request 拿不到回执一直挂在等待列表。
            response = Array.Empty<byte>();
            return true;
        }

        return false;
    }

    // ==================================================================
    // 进度推进（SettleDataRequest）
    // ==================================================================

    /// <summary>
    /// 处理 SettleDataRequest（1=LevelClear bool, 2=CityID, 3=LevelID）。
    /// 若通报的是"通关了当前这一个周本任务对应的关卡"，则把已解锁数 +1。
    /// 返回 true 表示进度有变化（调用方应推送 gate.WeeklyQuestNoticePush）。
    ///
    /// 只在 idx + 1 == _unlocked（即通的就是"当前任务"）时推进：
    /// 重打老旧关卡不会刷进度，也不会越级解锁。
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
            if (idx < 0) return false;                  // 不是本期周本关卡
            if (idx + 1 != _unlocked) return false;     // 不是"当前任务"这一关（重打/乱序）

            var next = Math.Min(idx + 2, _showLines.Length + 1);
            if (next <= _unlocked) return false;        // 已全部通关，无变化
            _unlocked = next;
            Save();

            Log.Information(
                "[Weekly] 悖域回归通关 LevelID={LevelId} (任务={QuestId}) -> 已解锁 {Unlocked}/{Total} 任务{Note}",
                levelId, idx >= 0 && idx < _showLines.Length ? _showLines[idx] : 0,
                Math.Min(_unlocked, _showLines.Length), _showLines.Length,
                _unlocked > _showLines.Length ? "（本期已全部完成）" : "");
            return true;
        }
    }

    // ==================================================================
    // 持久化
    // ==================================================================

    private sealed class StateDto
    {
        public int Unlocked { get; set; }
        public int[]? ShowLines { get; set; }
        public int[]? Levels { get; set; }
        public string UpdatedAt { get; set; } = "";
    }

    private static int LoadUnlocked()
    {
        try
        {
            if (string.IsNullOrEmpty(_statePath) || !File.Exists(_statePath)) return 0;
            var dto = JsonSerializer.Deserialize<StateDto>(File.ReadAllText(_statePath));
            return dto?.Unlocked ?? 0;
        }
        catch (Exception ex)
        {
            Log.Warning("[Weekly] 读取进度失败: {Msg}", ex.Message);
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
                Unlocked = _unlocked,
                ShowLines = _showLines,
                Levels = _levels,
                UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };
            File.WriteAllText(_statePath,
                JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Warning("[Weekly] 写入进度失败: {Msg}", ex.Message);
        }
    }

    // ==================================================================
    // 数据模型 / 解析 / 序列化
    // ==================================================================

    /// <summary>WeeklyQuestInfoResponse 的可变镜像（字段与官方一致）。</summary>
    private sealed class Info
    {
        public long RefreshTimestamp;
        public long LastRefreshTimestamp;
        public int[] ChoseQuests = Array.Empty<int>();
        public int[] ShowLines = Array.Empty<int>();
        public bool IsFrontOk;
        public bool IsNewRefreshChecked;
    }

    /// <summary>从捕获快照取模板；缺失时用内建常量兜底。</summary>
    private static Info LoadTemplate()
    {
        if (_template != null) return _template;

        Info? info = null;
        if (CapturedData.Responses.TryGetValue(InfoRoute, out var captured)
            && captured != null && captured.Length > 0)
        {
            info = Parse(captured);
        }

        if (info == null)
        {
            info = new Info
            {
                ShowLines = (int[])_showLines.Clone(),
                ChoseQuests = ComputeChoseQuests(_unlocked, _showLines),
                IsFrontOk = true,
                IsNewRefreshChecked = true,
            };
        }
        else if (info.ShowLines.Length == 0)
        {
            info.ShowLines = (int[])_showLines.Clone();
        }

        _template = info;
        return info;
    }

    private static Info? _template;

    /// <summary>从 WeeklyQuestInfoResponse 字节解析出 Info。</summary>
    private static Info? Parse(byte[] msg)
    {
        if (!Walk(msg, 0, msg.Length, out var fields)) return null;
        var info = new Info();
        var chose = new List<int>();
        var show = new List<int>();

        foreach (var f in fields)
        {
            switch (f.Number)
            {
                case 1 when f.WireType == 0:
                    info.RefreshTimestamp = ReadVarintAt(msg, f);
                    break;
                case 2 when f.WireType == 0:
                    info.LastRefreshTimestamp = ReadVarintAt(msg, f);
                    break;
                case 3:
                    ReadRepeatedInt32(msg, f, chose);
                    break;
                case 4:
                    ReadRepeatedInt32(msg, f, show);
                    break;
                case 5 when f.WireType == 0:
                    info.IsFrontOk = ReadVarintAt(msg, f) != 0;
                    break;
                case 6 when f.WireType == 0:
                    info.IsNewRefreshChecked = ReadVarintAt(msg, f) != 0;
                    break;
            }
        }

        info.ChoseQuests = chose.ToArray();
        info.ShowLines = show.ToArray();
        return info;
    }

    /// <summary>按官方字段顺序重建 WeeklyQuestInfoResponse 字节。</summary>
    private static byte[] Serialize(Info info, int[] choseQuests)
    {
        using var ms = new MemoryStream();

        if (info.RefreshTimestamp != 0)
        {
            WriteTag(ms, 1, 0);
            WriteVarint(ms, (ulong)info.RefreshTimestamp);
        }
        if (info.LastRefreshTimestamp != 0)
        {
            WriteTag(ms, 2, 0);
            WriteVarint(ms, (ulong)info.LastRefreshTimestamp);
        }

        WritePackedInt32(ms, 3, choseQuests);           // ChoseQuests（空则整字段省略）
        WritePackedInt32(ms, 4, info.ShowLines);        // ShowLines

        if (info.IsFrontOk)
        {
            WriteTag(ms, 5, 0);
            WriteVarint(ms, 1);
        }
        if (info.IsNewRefreshChecked)
        {
            WriteTag(ms, 6, 0);
            WriteVarint(ms, 1);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 已解锁数 → 下发的 ChoseQuests。
    /// unlocked &gt; ShowLines.Length 表示本期全部完成 ⇒ 下发空列表（面板判定为"已完成"）。
    /// </summary>
    private static int[] ComputeChoseQuests(int unlocked, int[] showLines)
    {
        if (showLines.Length == 0) return Array.Empty<int>();
        if (unlocked > showLines.Length) return Array.Empty<int>();
        var n = Clamp(unlocked, 0, showLines.Length);
        if (n <= 0) return Array.Empty<int>();
        var arr = new int[n];
        Array.Copy(showLines, 0, arr, 0, n);
        return arr;
    }

    /// <summary>把 ShowLines(任务) 映射成同序的关卡列表。</summary>
    private static int[] ResolveLevels(int[] showLines)
    {
        var outp = new int[showLines.Length];
        for (var i = 0; i < showLines.Length; i++)
        {
            if (QuestToLevel.TryGetValue(showLines[i], out var lv)) outp[i] = lv;
            else if (i < FallbackLevels.Length) outp[i] = FallbackLevels[i];
            else outp[i] = 0;
        }
        return outp;
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
                    valueStart = i;                     // 值起点 = 长度前缀之后
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

    private static long ReadVarintAt(byte[] b, Field f)
    {
        var i = f.ValueStart;
        return TryReadVarint(b, ref i, f.ValueEnd, out var v) ? v : 0L;
    }

    /// <summary>读取 repeated int32 —— 同时兼容 packed(wire=2) 与逐个出现(wire=0)。</summary>
    private static void ReadRepeatedInt32(byte[] b, Field f, List<int> sink)
    {
        if (f.WireType == 0)
        {
            sink.Add((int)ReadVarintAt(b, f));
            return;
        }
        if (f.WireType != 2) return;
        var i = f.ValueStart;
        while (i < f.ValueEnd)
        {
            if (!TryReadVarint(b, ref i, f.ValueEnd, out var v)) return;
            sink.Add((int)v);
        }
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

    /// <summary>写 packed repeated int32；空列表时整个字段省略（protobuf 默认值语义）。</summary>
    private static void WritePackedInt32(Stream s, int number, int[] values)
    {
        if (values == null || values.Length == 0) return;
        using var tmp = new MemoryStream();
        foreach (var v in values) WriteVarint(tmp, (ulong)v);
        var body = tmp.ToArray();
        WriteTag(s, number, 2);
        WriteVarint(s, (ulong)body.Length);
        s.Write(body, 0, body.Length);
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}
