using System;
using System.Collections.Generic;
using IntoTheVoidServer.Router;
using Serilog;

namespace IntoTheVoidServer.Pomelo;

/// <summary>
/// 补齐"客户端实际调用但私服原先没有响应"的接口。
///
/// 背景
/// ----
/// 服务端日志里出现过 `Unhandled route` 的接口, 说明客户端确实会调用,
/// 而私服原先一律返回空字节。空字节对 Google.Protobuf 而言是合法消息
/// (所有字段取默认值), 但像"个人档案"这类界面会因此一片空白。
///
/// 本文件中的字段编号与类型均来自 HotAssembly/Assembly-CSharp.dll 提取的
/// Data/proto_schema.json, 与客户端协议严格对齐。
///
/// 由 tools/extract_proto_schema.py + tools/interface_gap.py 维护。
/// </summary>
public static class MissingInterfaceResponses
{
    /// <summary>离线模式下的默认玩家 UID(与登录流程一致)。</summary>
    private const string DefaultUid = "34184063";

    /// <summary>离线模式下的默认昵称(与 BuildPlayerDataResponse 保持一致)。</summary>
    private const string DefaultNickname = "OfflinePlayer";

    /// <summary>
    /// 需要在 ClientRoutes 补齐之后再注册的真实响应。
    /// 键为路由, 值为构造函数。
    /// </summary>
    private static readonly Dictionary<string, Func<byte[]>> Builders = new()
    {
        // --- 个人档案: 客户端档案界面的主要数据源 ---
        ["game.game.GetProfileRequest"] = BuildGetProfileResponse,
        ["gate.ProfileRequest"] = BuildProfileResponse,

        // --- 公会 ---
        ["game.game.GetGuildMembersRequest"] = BuildGetGuildMembersResponse,
        ["game.game.GetGuildRepairListRequest"] = BuildGetGuildRepairListResponse,

        // --- 商业化 / 图鉴 / 其它信息类 ---
        ["game.game.RechargeInfoRequest"] = BuildRechargeInfoResponse,
        ["game.game.GetAtlasInfoRequest"] = BuildGetAtlasInfoResponse,
        ["game.game.MRItemCompletedRankRequest"] = BuildMRItemCompletedRankResponse,
        ["game.game.ServerTagListRequest"] = BuildServerTagListResponse,
        ["game.game.LoginDeviceInfoRequest"] = BuildLoginDeviceInfoResponse,
        ["game.game.ClearRedPointRequest"] = BuildClearRedPointResponse,
    };

    /// <summary>
    /// 注册真实响应构造器。必须在 ClientRoutes.Register 之后调用,
    /// 这样本文件的实现会覆盖自动补齐的空响应。
    /// </summary>
    public static int Register(MessageRouter router)
    {
        foreach (var (route, builder) in Builders)
        {
            router.RegisterHandler(route, (_, _) =>
            {
                var payload = builder();
                Log.Debug("[MissingInterface] {Route} -> {Size} bytes", route, payload.Length);
                return System.Threading.Tasks.Task.FromResult<byte[]?>(payload);
            });
        }

        // --- 关卡刷怪: LevelEnemyDropRequest 必须按请求回显 SeqID ---
        // 客户端 RORSpawnManager.EntitySpawner() 在创建敌人 spawner 前会先把它放入
        // m_emySpawnerWaitList 等待列表并发送 LevelEnemyDropRequest 向服务端"购买"
        // 该 spawner 的掉落表; ReceiveSpawnerDropNetMsg 用响应里的 SeqID 反查等待项,
        // 匹配成功才会 Remove + InvokeFun -> 真正生成敌人。
        // 若服务端返回空响应(SeqID=0), FindIndex 永远失败 -> 敌人永不生成, 且客户端
        // 每 1.5s 重发请求(日志中 LevelEnemyDropRequest 连发的现象)。
        // 因此这里必须回显 SeqID(字段1) 与 EnemyLootID(字段3); TypeDropList(字段4,
        // map) 留空即"不掉落物品", 但不影响刷怪本身。
        router.RegisterHandler("game.game.LevelEnemyDropRequest", (payload, _) =>
        {
            var response = BuildLevelEnemyDropResponse(payload);
            Log.Information("[LevelEnemyDrop] request={Size} bytes -> response {RespSize} bytes",
                payload?.Length ?? 0, response?.Length ?? 0);
            return System.Threading.Tasks.Task.FromResult(response);
        });

        return Builders.Count + 1;
    }

    // ==================================================================
    // GetProfileResponse (Protos.GetProfileResponse, 19 字段)
    // ==================================================================
    /// <summary>
    /// 个人档案。字段编号严格取自 proto_schema.json。
    /// MR 取 GameState 中 2 号货币(垄金)的值, 保证与 GM 面板改动联动。
    /// </summary>
    public static byte[] BuildGetProfileResponse()
    {
        return ProtoBuilder.Write()
            .WriteString(1, DefaultUid)              // UID
            .WriteInt32(2, GameState.GetCurrency(2))  // MR (垄金)
            .WriteInt32(3, 0)                         // Exp
            .WriteString(4, DefaultNickname)          // Nickname
            .WriteInt32(5, 10)                        // EquipmentCount
            .WriteInt32(6, 30)                        // ModCount
            .WriteInt32(7, 10001)                     // AvatarID
            .WriteInt32(8, 10001)                     // AvatarFrameID
            // 字段 9 ProfileCharacter 为嵌套消息, 留空即"未设置展示角色", 客户端会容错
            .WriteString(10, "")                      // Introduce
            .WriteString(11, "")                      // Icon
            .WriteString(12, "")                      // IconFrame
            .WriteString(13, "")                      // Theme
            .WriteInt32(14, 0)                        // RR
            .WriteInt32(15, 0)                        // GuildID (0 = 无公会)
            .WriteString(16, "")                      // GuildName
            .WriteInt32(17, 1)                        // TeamMemberCount
            .WriteInt32(18, 0)                        // Position
            .WriteString(19, "")                      // Title
            .ToBytes();
    }

    // ==================================================================
    // ProfileResponse (Protos.ProfileResponse, 2 字段)
    // 字段 2 是 map<string, string>; 离线模式无扩展资料, 留空 map 即合法
    // ==================================================================
    public static byte[] BuildProfileResponse()
    {
        return ProtoBuilder.Write()
            .WriteString(1, DefaultUid)   // UID
            .ToBytes();
    }

    // ==================================================================
    // GetGuildMembersResponse (2 字段)
    // GuildID = 0 表示"未加入公会", 成员列表为空
    // ==================================================================
    public static byte[] BuildGetGuildMembersResponse()
    {
        return ProtoBuilder.Write()
            .WriteInt32(1, 0)             // GuildID (0 = 无公会)
            .ToBytes();
    }

    // ==================================================================
    // GetGuildRepairListResponse (2 字段: Power + repeated GuildRepairInfo)
    // ==================================================================
    public static byte[] BuildGetGuildRepairListResponse()
    {
        return ProtoBuilder.Write()
            .WriteInt32(1, 0)             // Power
            .ToBytes();
    }

    // ==================================================================
    // RechargeInfoResponse (1 字段: 嵌套 RechargeInfo)
    // 离线模式无充值记录 -> 留空嵌套消息
    // ==================================================================
    public static byte[] BuildRechargeInfoResponse()
    {
        return ProtoBuilder.Write()
            .WriteMessage(1, Array.Empty<byte>())   // RechargeInfo (空)
            .ToBytes();
    }

    // ==================================================================
    // GetAtlasInfoResponse (2 字段: repeated int ItemIDs, repeated QuestPack)
    // 离线模式无图鉴解锁 -> 两个列表均为空, 空 repeated 字段合法
    // ==================================================================
    public static byte[] BuildGetAtlasInfoResponse()
    {
        return Array.Empty<byte>();
    }

    // ==================================================================
    // MRItemCompletedRankResponse (1 字段: repeated MRPackItem)
    // ==================================================================
    public static byte[] BuildMRItemCompletedRankResponse()
    {
        return Array.Empty<byte>();
    }

    // ==================================================================
    // ServerTagListResponse (1 字段: map<string,int> ServerTags)
    // 离线模式不下发任何服务器标签 -> 空 map
    // ==================================================================
    public static byte[] BuildServerTagListResponse()
    {
        return Array.Empty<byte>();
    }

    // ==================================================================
    // LoginDeviceInfoResponse (1 字段: map<int,int> DeviceInfo)
    // ==================================================================
    public static byte[] BuildLoginDeviceInfoResponse()
    {
        return Array.Empty<byte>();
    }

    // ==================================================================
    // ClearRedPointResponse (1 字段: bytes Category)
    // 清除红点无返回数据 -> 空 bytes 合法
    // ==================================================================
    public static byte[] BuildClearRedPointResponse()
    {
        return Array.Empty<byte>();
    }

    // ==================================================================
    // LevelEnemyDropResponse (Protos.LevelEnemyDropResponse, 5 字段)
    // 字段: 1=SeqID(int) 2=TurnNum(int) 3=EnemyLootID(int)
    //       4=TypeDropList(map<int,EnemyDropInfo>) 5=BossDrop(EnemyDropInfo)
    // 请求 LevelEnemyDropRequest: 1=SeqID(int) 2=EnemyLootID(int) 3=LevelID(int)
    //
    // 关键 1: 客户端 RORSpawnManager 用响应的 SeqID 解锁等待列表中的刷怪项,
    //         所以 SeqID / EnemyLootID 必须回显请求值, 否则敌人永不生成(关卡无怪)。
    //
    // 关键 2: 敌人实际掉什么由字段 4 TypeDropList 决定 ——
    //         每只怪死亡时 GameEventEnemyDropAward.DropSpecialItem() 会调 DropItemsByNet(),
    //         它按 LevelNetDropData.GetEmyDropItem(spawner, EnmType) 取
    //         TypeDropList[EnmType].DropByPosition[该类型已击杀数], 再照 SingleEnemyDrop.Drops
    //         生成掉落实体。TypeDropList 为空 -> 直接 return -> 怪物少了整整一份掉落。
    //         这里按"该关卡自己的 Level._EnemyDrop 配置"逐关生成, 见 Pomelo/DropGenerator.cs。
    // ==================================================================
    public static byte[]? BuildLevelEnemyDropResponse(byte[]? requestPayload)
    {
        if (requestPayload == null || requestPayload.Length == 0)
        {
            Log.Warning("[LevelEnemyDrop] 请求体为空, 返回 null(走兜底)");
            return null;
        }

        long seqId = 0, enemyLootId = 0, levelId = 0;
        int i = 0;
        // 解析 LevelEnemyDropRequest: 三个 int 均为 varint。
        while (i < requestPayload.Length)
        {
            var tag = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
            if (tag == null)
                break;
            i += tag.Value.consumed;
            int fieldNumber = (int)(tag.Value.value >> 3);
            int wireType = (int)(tag.Value.value & 7);
            if (wireType != 0)
                break; // 期望全是 varint
            var fieldValue = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
            if (fieldValue == null)
                break;
            i += fieldValue.Value.consumed;
            switch (fieldNumber)
            {
                case 1: seqId = fieldValue.Value.value; break;
                case 2: enemyLootId = fieldValue.Value.value; break;
                case 3: levelId = fieldValue.Value.value; break;
            }
        }

        if (seqId <= 0)
        {
            Log.Warning("[LevelEnemyDrop] 未能从请求解析出有效 SeqID, payload={PayloadHex}",
                Convert.ToHexString(requestPayload));
            return null;
        }

        var head = ProtoBuilder.Write()
            .WriteInt32(1, (int)seqId)          // SeqID 回显
            .WriteInt32(2, 0)                   // TurnNum
            .WriteInt32(3, (int)enemyLootId)    // EnemyLootID 回显
            .ToBytes();

        // 字段 4/5：按该关卡自己的掉落配置，为这个刷怪组的每只怪算出掉落。
        // 注意 BuildDropFields 返回的已经是"带 field tag 的完整序列"，
        // 这里只能拼接，不能再套一层 WriteMessage(4, …)，否则会变成 field4{field4{…}}。
        var bytes = head;
        string dropSummary = "无掉落配置";
        if (enemyLootId > 0 && levelId > 0 && DropTables.IsLoaded)
        {
            var plan = DropGenerator.Build((int)levelId, (int)enemyLootId,
                ResolveWorldPoolTableId(), Random.Shared);
            if (plan.TypeDropList.Count > 0 || plan.BossDrop.Count > 0)
            {
                bytes = head.Concat(BuildDropFields(plan)).ToArray();
                dropSummary = plan.Summary();
            }
        }

        Log.Information("[LevelEnemyDrop] SeqID={SeqId} Loot={LootId} Level={LevelId} -> {Size}B 掉落[{Drop}]",
            seqId, enemyLootId, levelId, bytes.Length, dropSummary);
        return bytes;
    }

    // ==================================================================
    // 掉落序列化：产出 LevelEnemyDropResponse 字段 4/5 的完整线格式序列
    // （每个 map entry 自带 tag，调用方直接拼到响应尾部即可）
    //   字段 4 TypeDropList  map<int(EnmType), EnemyDropInfo>
    //   字段 5 BossDrop       EnemyDropInfo（BOSS 宝箱，客户端平铺成列表按序取用）
    //   EnemyDropInfo  { 1 = DropByPosition: map<int(位置序号), SingleEnemyDrop> }
    //   SingleEnemyDrop{ 1 = Drops: repeated ItemInfo }
    //   ItemInfo       { 1 = ItemID(int), 2 = ItemCount(int) }
    //
    // 位置序号必须从 0 连续铺满：客户端用"该类型已击杀数"递增索引，
    // 缺项会让 GetEmyDropItem 返回 null（这一只直接不掉）。
    // "这一只不掉"要写成 value 为空消息的空 SingleEnemyDrop 占位。
    // ==================================================================
    private static byte[] BuildDropFields(SpawnerDropPlan plan)
    {
        var root = ProtoBuilder.Write();

        foreach (var (enmType, byPos) in plan.TypeDropList.OrderBy(kv => kv.Key))
        {
            // TypeDropList 的 map entry { 1 = key(EnmType), 2 = value(EnemyDropInfo) }
            root.WriteMessage(4, ProtoBuilder.Write()
                .WriteInt32(1, enmType)
                .WriteMessage(2, BuildEnemyDropInfo(byPos))
                .ToBytes());
        }

        if (plan.BossDrop.Count > 0)
            root.WriteMessage(5, BuildEnemyDropInfo(plan.BossDrop));

        return root.ToBytes();
    }

    /// <summary>EnemyDropInfo { 1 = DropByPosition: map&lt;int(位置), SingleEnemyDrop&gt; }</summary>
    private static byte[] BuildEnemyDropInfo(Dictionary<int, List<DropItem>> byPos)
    {
        var info = ProtoBuilder.Write();

        foreach (var (pos, items) in byPos.OrderBy(kv => kv.Key))
        {
            byte[] valueMsg;
            if (items.Count == 0)
            {
                valueMsg = Array.Empty<byte>();
            }
            else
            {
                var drops = ProtoBuilder.Write();
                foreach (var it in items)
                {
                    drops.WriteMessage(1, ProtoBuilder.Write()
                        .WriteInt32(1, it.ItemId)
                        .WriteInt32(2, it.Count)
                        .ToBytes());
                }
                valueMsg = drops.ToBytes();
            }

            // map entry { 1 = key(位置), 2 = value(SingleEnemyDrop) }
            info.WriteMessage(1, ProtoBuilder.Write()
                .WriteInt32(1, pos)
                .WriteMessage(2, valueMsg)
                .ToBytes());
        }

        return info.ToBytes();
    }

    /// <summary>
    /// 本期世界掉落池条目 ID —— 来自 ItemDropWorldPoolResponse { 1 = ItemDropWorldPoolID }。
    /// 服务端捕获快照里存着官方值（通常 61000002），解析失败时回落到该常量。
    /// 世界池只在 EnemyDrop._WorldPoolRate 命中时替代关卡池。
    /// </summary>
    private static int ResolveWorldPoolTableId()
    {
        const string route = "game.game.ItemDropWorldPoolRequest";
        if (CapturedData.Responses.TryGetValue(route, out var raw) && raw is { Length: > 0 })
        {
            int i = 0;
            while (i < raw.Length)
            {
                var tag = ProtoLevelIdRewriter.ReadVarint(raw, i);
                if (tag == null) break;
                i += tag.Value.consumed;
                int fieldNumber = (int)(tag.Value.value >> 3);
                int wireType = (int)(tag.Value.value & 7);
                if (wireType != 0) break;
                var val = ProtoLevelIdRewriter.ReadVarint(raw, i);
                if (val == null) break;
                i += val.Value.consumed;
                if (fieldNumber == 1) return (int)val.Value.value;
            }
        }
        return 61000002;
    }
}
