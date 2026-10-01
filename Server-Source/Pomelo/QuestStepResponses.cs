using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IntoTheVoidServer.Router;
using Serilog;

namespace IntoTheVoidServer.Pomelo;

/// <summary>
/// 局内任务/互动类请求的响应补齐 (2026-09-03)。
///
/// 服务端日志暴露的三个缺口 (server_20260903.log):
///   1) game.game.BattleQuestRewardRequest  —— 全日志 16 次 "Unhandled route"。
///      局内任务 (QuestType.InGame) 完成时 QuestData.GotoNextQuest() 会发此请求
///      回执奖励; 私服无 handler -> 空响应 -> 客户端等待列表按 SeqID 找不到回执,
///      疑似引发多波次防守/生存关卡 "第二波不刷" 及结算后 19:04:01 异常爆发。
///   2) game.game.SetQuestStepsRequest     —— 被 Data/responses 0 字节捕获响应抢占,
///      请求体从未进 router (日志只有 Using captured response 0 bytes)。
///      局外任务步上报 (QuestReport.SendQuestStepsRequest) 依赖该回执推进。
///   3) game.game.IngameInteractionChangeRequest —— 客户端局内互动变更, 原本无 handler。
///
/// 本文件为这三个路由注册显式 handler: 记录请求体 hex 便于离线解码核对,
/// 并对 BattleQuestRewardRequest 按无尽任务链下发下一波任务 (EndlessQuestChain,
/// 2026-09-04: 客户端下一波任务开启完全由该响应驱动), SetQuestSteps 回主城
/// InteractionState 快照, IngameInteractionChange 回 NodeID+互动后列表
/// (映射反查, 见下方注释)。
///
/// 注册时机: 必须在 ClientRoutes / MissingInterfaceResponses 之后
/// (RegisterHandler 为覆盖语义), 由 MessageRouter 构造函数末尾调用。
/// </summary>
public static class QuestStepResponses
{
    /// <summary>
    /// 捕获的主城 InteractionStateListResponse 原始字节 (field1 = 64 条 InteractionState)。
    /// SetQuestStepsResponse.InteractionState 同为 field1 repeated InteractionState,
    /// protobuf 线上格式一致, 可直接作为 SetQuestStepsResponse 响应体。
    /// 来源: Data/responses/game_game_InteractionStateListRequest.bin (官方快照)。
    /// </summary>
    private static readonly byte[] InteractionStateSnapshotBytes = Convert.FromBase64String(
        "CgsIy/fKCRIEzbGbEQoLCIOPywkSBOyxmxEKBQjD2cwJCgUIzOjKCQoLCMWgzQkSBKixmxEKCwjV8MoJEgSNiZwRCgUIq97NCQoL" +
        "CKnwygkSBIWdnBEKCwiT7MsJEgStsJsRCgsIq+HMCRIEgLGbEQoFCOGUzAkKCwibh8sJEgTisZsRCgUIiPHKCQoFCInxygkKCwjz" +
        "wM8JEgS8spsRCgUI++3NCQoFCN6UzAkKCwijvcsJEgS1r5sRCgUI3ZTMCQoLCOKUzAkSBMCtnBEKCwitqM0JEgSmsZsRCgsIhfHK" +
        "CRIEuaScEQoLCPvzywkSBMGwmxEKCwiLuc8JEgS6spsRCgUI2vDKCQoLCNvUywkSBPGvmxEKBQj78MwJCgsI/5PMCRIE1KycEQoF" +
        "CLOFzgkKBQi9+MoJCgUIy/3NCQoFCMboygkKBQijgMsJCgsIi8XLCRIE3a+bEQoFCMWmywkKCwjglMwJEgT/nJwRCgUIovjKCQoL" +
        "CLP/ygkSBNixmxEKCwih58oJEgSx/ZsRCgsI3u/KCRIEkLGbEQoLCJDwygkSBLKymxEKCwjU78oJEgSYsJsRCgUIu6/NCQoLCKaE" +
        "zAkSBMPLnBEKBQiT5s0JCgsIo7HPCRIEubKbEQoLCPeTzAkSBMHLnBEKCwj978oJEgTTmZ0RCgUIyOjKCQoFCIrxygkKBQjj9c0J" +
        "CgsIgvHKCRIE0ZycEQoLCKvkywkSBKKvmxEKCwjT8MoJEgSlgZwRCgsIxpzMCRIE1a2cEQoFCIXfzQkKCwjzzMsJEgTLr5sRCgsI" +
        "/JPMCRIEt62cEQoFCMXoygkKCwiG8coJEgT8nJwRCg8I6e/KCRII0ZmdEdWZnREKCwj+k8wJEgSirJwRCgUIhfDKCQoLCICUzAkS" +
        "BIatnBE=");

    public static int Register(MessageRouter router)
    {
        int n = 0;

        // ------------------------------------------------------------------
        // 1) 局内任务结算回执
        // 响应 = 回显请求的 QuestData(field1) + Round(field4) + SeqID(field5)。
        // 空 Items / UpdateInfo = 不实际发放战斗奖励(与私服其它占位一致)。
        // ------------------------------------------------------------------
        router.RegisterHandler("game.game.BattleQuestRewardRequest", (payload, _) =>
        {
            LogRequest("BattleQuestRewardRequest", payload);
            var resp = BuildBattleQuestRewardResponse(payload);
            Log.Information("[BattleQuestReward] request={Req}B -> response {Resp}B",
                payload?.Length ?? 0, resp?.Length ?? 0);
            return Task.FromResult<byte[]?>(resp);
        });
        n++;

        // ------------------------------------------------------------------
        // 2) 局外任务步同步回执
        // 关键 (2026-09-03 深挖): SetQuestStepsResponse 不是空消息!
        //   客户端 QuestReport.SetQuestStepsResponse():
        //     if (response.InteractionState == null) return;
        //     foreach -> NPCStateDataService.InteractionStateChangeResponse()
        //                -> QuestEvent.QuesInteractionStateDispatch + RefreshSingleInteraction
        //   回空 = 互动状态分发链路死路 = 防守关保护目标无法互动、第二波不开。
        // 这里回捕获的主城 InteractionStateListResponse 全量条目 (64 条 201xxxxx
        // -> 36xxxxx StateSht, 官方快照)。其 field1 与 SetQuestStepsResponse 的
        // InteractionState 字段编号相同, protobuf 编码可原样复用。
        // ------------------------------------------------------------------
        router.RegisterHandler("game.game.SetQuestStepsRequest", (payload, _) =>
        {
            LogRequest("SetQuestStepsRequest", payload);
            return Task.FromResult<byte[]?>(InteractionStateSnapshotBytes);
        });
        n++;

        // ------------------------------------------------------------------
        // 3) 局内互动变更 (2026-09-03 深挖后升级为真实回包)
        // 请求  = IngameInteractionChangeRequest{InteractionID} (仅 field1 int32)。
        // 响应  = IngameInteractionChangeResponse{NodeID=1, InteractionIDs=2
        //         (repeated int32), UpdateInfo=3}。
        // 客户端 Lua NetLogic:IngameInteractionChangeResponse:
        //   data = MapProgressLogic:GetNodeData(resp.NodeID)  -- NodeID 无效直接报错 return
        //   data.InteractionIDs = resp.InteractionIDs         -- 用回包列表覆盖节点互动状态
        //   (UpdateInfo 为 nil 时 RecordInteractionReward / DealRewards 均有空值守卫, 安全)
        // 私服无法可靠推断关卡 LinkNode, 故用原始 CityNodeInfos 快照
        // (剥除互动标记前的备份) 建 InteractionID -> NodeID 映射;
        // 回包 InteractionIDs = [请求的 InteractionID]:
        //   客户端注册互动物时按 "节点列表是否含该 InGameInteractionStateID"
        //   判定关闭态, 所以把本次互动的 ID 写回列表 = 标记该互动物已完成关闭,
        //   同节点其它互动物不受影响。
        // 未命中映射 (局内非节点互动物) 回空消息, 客户端记录错误日志后自行推进。
        // ------------------------------------------------------------------
        router.RegisterHandler("game.game.IngameInteractionChangeRequest", (payload, _) =>
        {
            LogRequest("IngameInteractionChangeRequest", payload);
            var resp = BuildIngameInteractionChangeResponse(payload);
            Log.Information("[IngameInteractionChange] request={Req}B -> response {Resp}B",
                payload?.Length ?? 0, resp?.Length ?? 0);
            return Task.FromResult<byte[]?>(resp);
        });
        n++;

        // ------------------------------------------------------------------
        // 4) 结算数据 (2026-09-04 深挖, 防守关"波次结束后互动"的真正卡点)
        // 无尽/波次防守关 (LevelType.Endless, RoundType=2) 每波结束客户端发
        // SettleDataRequest (C# FightDataManager.SendSettleDataRequest, LevelClear
        // = FightDataManager.LevelClear(), 无尽关在 EndlessWaveContinue 状态下恒 true)。
        // 结算界面 CalculateVM:InitCalculateData:
        //   respList = GetSettleDataResponse(); if not respList then
        //     Logger.Error("重大Bug 没有获取的结算的数据！！！！！！") return
        // 原先回空 -> 结算流程中断/数据缺失 -> EndlessContinue(是否继续)->交互终端
        // (Step 473014100 -> 473014106, Monitor InteractEntity=22231231) 永不出现
        // -> "波次结束后才能互动的互动无法互动、第二波不刷"。
        // 响应 SettleDataResponse{LevelClear=1(bool), NodeInfos=3, SettleRewardInfos=4,
        //   QuestData=9, CharacterExpInfos=10 ...}。Lua 侧对空列表字段均有遍历守卫,
        //   最小合法回包 = 回显请求的 LevelClear。奖励字段暂留空 (界面显示 0 奖励,
        //   流程可走通); 结算后 ClearLevelQuest/下一波推进全在客户端本地。
        // ------------------------------------------------------------------
        router.RegisterHandler("game.game.SettleDataRequest", (payload, _) =>
        {
            LogRequest("SettleDataRequest", payload);
            var resp = BuildSettleDataResponse(payload);

            // 悖域巡查(突击警报) 进度推进：请求体 3=LevelID, 1=LevelClear(bool)。
            // 若本关属于本期 sortie 关卡，则把 AlertEventInfo.CurrentIndex 推进到下一关，
            // 并推 gate.AlertEventPush 催促客户端立刻重发 AlertEventInfoRequest
            // —— 否则 手册->悖域巡查 面板会一直沿用登录时拿到的旧 CurrentIndex，
            //    表现为"通关第 1 关后第 2 关永不解锁"。见 Pomelo/AlertEventProgress.cs。
            var sortieAdvanced = AlertEventProgress.OnLevelSettled(payload);

            // 悖域回归(局外周本) 进度推进：同一份 SettleDataRequest。若本关是"当前那一个"
            // 周本任务对应的关卡，则把 WeeklyQuestInfoResponse.ChoseQuests 多解锁一个任务
            // （= 地图上多解锁一个节点、面板进度 +1），并推 gate.WeeklyQuestNoticePush
            // 催促客户端立刻重发 WeeklyQuestInfoRequest —— 否则 手册->悖域回归 面板会一直
            // 沿用登录时拿到的旧 ChoseQuests，表现为"通关第 1 个任务后第 2 个不开"。
            // 见 Pomelo/WeeklyProgress.cs。
            var weeklyAdvanced = WeeklyProgress.OnLevelSettled(payload);

            Log.Information("[SettleData] request={Req}B -> response {Resp}B (levelClear={Lc})",
                payload?.Length ?? 0, resp?.Length ?? 0, (resp?.Length ?? 0) > 0);

            if (sortieAdvanced)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1500);
                        await IntoTheVoidServer.Net.PomeloTcpServer.PushToAllClientsAsync(
                            AlertEventProgress.PushRoute, Array.Empty<byte>());
                        Log.Information("[AlertEvent] 已推送 {Route}，客户端将重拉悖域巡查列表",
                            AlertEventProgress.PushRoute);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[AlertEvent] 推送 {Route} 失败: {Msg}",
                            AlertEventProgress.PushRoute, ex.Message);
                    }
                });
            }

            if (weeklyAdvanced)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1800);
                        await IntoTheVoidServer.Net.PomeloTcpServer.PushToAllClientsAsync(
                            WeeklyProgress.PushRoute, Array.Empty<byte>());
                        Log.Information("[Weekly] 已推送 {Route}，客户端将重拉悖域回归列表",
                            WeeklyProgress.PushRoute);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[Weekly] 推送 {Route} 失败: {Msg}",
                            WeeklyProgress.PushRoute, ex.Message);
                    }
                });
            }

            return Task.FromResult<byte[]?>(resp);
        });
        n++;

        return n;
    }

    /// <summary>
    /// 构造 SettleDataResponse: 回显请求 field1 (LevelClear bool) + 结算奖励 (field6)。
    /// 请求 1=LevelClear(bool) 2=CityID 3=LevelID 4=ChestlingPoolID 5=StatData ...
    /// 响应 1=LevelClear(bool) 6=QuestUpdateInfo(UpdateInfo{Items:ItemList{Items:BackPackItem}})
    ///
    /// 2026-09-27：补上 field6 结算奖励（此前只回 LevelClear，结算界面"没有随机奖励"）。
    /// 奖励来源 Level._RoundRewardShow -> QuestShow._Reward/_Amount/_Rate，见 SettleReward.cs。
    /// </summary>
    public static byte[] BuildSettleDataResponse(byte[]? requestPayload)
    {
        bool levelClear = false;
        int levelId = 0;
        var loot = new Dictionary<int, int>();   // field9: 客户端上报的"本局战利品"
        if (requestPayload != null && requestPayload.Length > 0)
        {
            int i = 0;
            try
            {
                while (i < requestPayload.Length)
                {
                    var tag = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
                    if (tag == null) break;
                    i += tag.Value.consumed;
                    int fieldNumber = (int)(tag.Value.value >> 3);
                    int wireType = (int)(tag.Value.value & 7);
                    if (wireType == 0)
                    {
                        var v = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
                        if (v == null) break;
                        i += v.Value.consumed;
                        // 注意: 不能一拿到 LevelClear 就 break —— LevelID 在其后 (field3),
                        // 结算奖励需要它。
                        if (fieldNumber == 1) levelClear = v.Value.value != 0;
                        else if (fieldNumber == 3) levelId = (int)v.Value.value;
                    }
                    else if (wireType == 2)
                    {
                        var len = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
                        if (len == null) break;
                        int bodyStart = i + len.Value.consumed;
                        int bodyEnd = bodyStart + (int)len.Value.value;
                        if (bodyEnd > requestPayload.Length) bodyEnd = requestPayload.Length;
                        // field9 = Loot (map<int32,int32>)：客户端上报的"本局战利品"。
                        // 结算界面 Tab[3]「掉落奖励」栏的数据源就是它 —— 官方由服务端回填到
                        // SettleDataResponse.field5 BattleUpdateInfo（见 CalculateVM:SetBattleUpdateInfo2）。
                        // 这里只做解析与记录，便于核对"掉落奖励"该显示什么。
                        if (fieldNumber == 9 && bodyEnd > bodyStart)
                            ParseLootEntry(requestPayload, bodyStart, bodyEnd, loot);
                        i = bodyEnd;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SettleData] 解析请求失败: {Msg}", ex.Message);
            }
        }

        // 诊断日志（2026-10-01）：把客户端上报的战利品落进日志。
        // 结算界面 Tab[3]「掉落奖励」= 服务端 field5，而服务端从未下发过该字段，
        // 所以那栏一直是空的；本日志用于确认 Loot 是否可用作回填数据源。
        Log.Information("[SettleData] 本局战利品(field9 Loot) {Cnt} 种{Detail}",
            loot.Count,
            loot.Count == 0
                ? "（客户端未上报）"
                : ": " + string.Join(" ", loot.Select(kv => $"{kv.Key}x{kv.Value}")));

        if (!levelClear)
            return Array.Empty<byte>();

        var writer = ProtoBuilder.Write();
        writer.WriteInt32(1, 1); // LevelClear = true (bool, proto3 显式编码)
        var body = writer.ToBytes();

        // 结算奖励 (field6 QuestUpdateInfo)：结算界面 CalculateVM / GameBattle_QuestRewardWindow
        // 的奖励条目完全取自这里，缺失即表现为"关卡没有随机结算奖励"。
        if (levelId > 0 && DropTables.IsLoaded)
        {
            try
            {
                // 客户端对同一次结算会连发多次相同请求（实测一口气 5 次）。
                // 奖励含随机项，重复 roll 会让两次响应内容不同 → 客户端把两份都入袋。
                // 因此按「关卡 + 请求原文」把整段 field6 缓存 1.5s，重复请求逐字节复用。
                var cacheKey = $"settle:{levelId}:{Convert.ToHexString(requestPayload ?? Array.Empty<byte>())}";
                if (SettleReward.TryGetCachedField6(cacheKey, out var cachedField6) && cachedField6 != null)
                {
                    body = body.Concat(cachedField6).ToArray();
                    Log.Information("[SettleData] 关卡 {Lv} 复用 1.5s 内同一结算包（客户端重复请求，{Size}B）",
                        levelId, cachedField6.Length);
                }
                else
                {
                    var rewards = SettleReward.Build(levelId, Random.Shared);
                    if (rewards.Count > 0)
                    {
                        var field6 = SettleReward.BuildQuestUpdateInfoField(rewards);
                        SettleReward.CacheField6(cacheKey, field6);
                        body = body.Concat(field6).ToArray();
                        ItemLedger.Flush();
                        Log.Information("[SettleData] 关卡 {Lv} 结算奖励 {Cnt} 项: {Items}",
                            levelId, rewards.Count, SettleReward.Summary(rewards));
                    }
                    else
                    {
                        Log.Information("[SettleData] 关卡 {Lv} 无结算奖励配置 (_RoundRewardShow 为空/无有效项)", levelId);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SettleData] 结算奖励生成失败: {Msg}", ex.Message);
            }
        }

        return body;
    }

    /// <summary>
    /// 解析 SettleDataRequest.field9 Loot 的一个 map entry。
    /// protobuf 的 map&lt;int32,int32&gt; 编码为 repeated message，每个 entry 是
    /// 一段独立子消息 { 1=key(ItemID), 2=value(Count) }，外层带 length 前缀。
    /// </summary>
    private static void ParseLootEntry(byte[] buf, int start, int end, Dictionary<int, int> loot)
    {
        int i = start;
        int key = 0, val = 0;
        while (i < end)
        {
            var tag = ProtoLevelIdRewriter.ReadVarint(buf, i);
            if (tag == null) break;
            i += tag.Value.consumed;
            int fn = (int)(tag.Value.value >> 3);
            int wt = (int)(tag.Value.value & 7);
            if (wt == 0)
            {
                var v = ProtoLevelIdRewriter.ReadVarint(buf, i);
                if (v == null) break;
                i += v.Value.consumed;
                if (fn == 1) key = (int)v.Value.value;
                else if (fn == 2) val = (int)v.Value.value;
            }
            else if (wt == 2)
            {
                var l = ProtoLevelIdRewriter.ReadVarint(buf, i);
                if (l == null) break;
                i += l.Value.consumed + (int)l.Value.value;
            }
            else
            {
                break;
            }
        }
        if (key != 0) loot[key] = val;
    }

    /// <summary>
    /// 解析 BattleQuestRewardRequest 并构造响应。
    /// 请求: 1=QuestData(repeated BattleQuestRewardData{1=QuestID,2=State,3=StepData,4=RewardIndex})
    ///       2=StatData 3=Round(int) 4=SeqID(int)
    /// 响应: 1=QuestData(repeated BattleQuestRewardData) 4=Round 5=SeqID
    ///
    /// 2026-09-04 深挖 (无尽关"继续后交互终端无法互动"的真根因):
    ///   客户端 QuestData.GotoNextQuest() 不自行处理 QuestSht._Goto, 只发本请求;
    ///   QuestManager.BattleQuestRewardResponseCB 拿响应 QuestData 逐个
    ///   OpenQuest —— 下一波任务 (如 34830141 "Q2监听是否继续", 其步骤链
    ///   473014100 EndlessContinue 监听 -> 473014106 交互终端) 完全靠服务端下发!
    ///   原先的"原样回显"会让 CB 重新 Open 已完成的旧任务, 下一波任务永不开启,
    ///   EndlessContinue 事件无人监听 -> 交互终端步(StartEvent ShowUIEvent
    ///   22231230) 不触发 -> "选继续后防御目标无法互动、第二波不刷"。
    ///   现行为: 请求 QuestID 命中 EndlessQuestChain.NextQuest 时,
    ///   响应 QuestData = 下一波任务 {QuestID=next, State=Unfinished(1)};
    ///   链尾 (最终波) 无映射 -> 不下发新任务 (关卡自然收尾)。
    ///   未命中映射的任务 (非无尽链) 保持原回显行为兼容。
    ///
    /// 2026-09-26 追加 (防御关"必须和防御目标交互才能开下一波"):
    ///   难度 1/5 的 Q2 与 2-8/3-6 等波次任务的步骤链是
    ///   EndlessContinue -> InteractEntity(交互终端/馈电终端/电池) -> WaitTime -> 刷怪,
    ///   客户端步骤只在监听事件派发时才完成, 而服务端无法代派发该事件
    ///   (无 ServerDriven_InteractEntity 通道; S_NotifyEntityEvent 客户端未注册路由属死代码;
    ///   BattleQuestRewardResponse.StepData 也不会被当作 reconnectStep 使用)。
    ///   → 服务端推进时把 EndlessQuestChain.InteractionGated 一并跳过,
    ///     表现为"波次结算选继续后直接开下一波, 不需要碰防御目标"。
    ///
    /// 2026-09-26 二次修正 (1-5 回归"没有倒计时、这波无法结束"):
    ///   跳过 1-5 的 Q2 (34830141/34830149) 是错的 —— 它们本身就是官方的
    ///   「60 秒守卫倒计时 + 自环无限波」, 跳过只会落到 34830142~34830147 这批
    ///   只靠 SpawnerClear 完成、没有倒计时的遗留任务上, 结果清完怪什么都不发生。
    ///   现改为客户端数据侧把这两关「1-5 交互终端」步骤的 StepMonitor
    ///   503014106 / 503014901 由 InteractEntity 换成 WaitTime(3), 步骤 3 秒自动完成,
    ///   服务端则老实下发 34830141/34830149 自环, 官方原生倒计时循环完整恢复。
    ///   (见 memory/_fix_xlsl.py, 备份 memory/_xlsl_backup/)
    /// </summary>
    public static byte[] BuildBattleQuestRewardResponse(byte[]? requestPayload)
    {
        if (requestPayload == null || requestPayload.Length == 0)
            return Array.Empty<byte>();

        var questChunks = new List<byte[]>();
        var questIds = new List<int>();
        long round = 0, seqId = 0;

        int i = 0;
        try
        {
            while (i < requestPayload.Length)
            {
                var tag = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
                if (tag == null) break;
                i += tag.Value.consumed;
                int fieldNumber = (int)(tag.Value.value >> 3);
                int wireType = (int)(tag.Value.value & 7);

                if (wireType == 0)
                {
                    var v = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
                    if (v == null) break;
                    i += v.Value.consumed;
                    switch (fieldNumber)
                    {
                        case 3: round = v.Value.value; break;
                        case 4: seqId = v.Value.value; break;
                    }
                }
                else if (wireType == 2)
                {
                    var len = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
                    if (len == null) break;
                    i += len.Value.consumed;
                    int chunkLen = (int)len.Value.value;
                    if (i + chunkLen > requestPayload.Length) break;
                    var chunk = new byte[chunkLen];
                    Buffer.BlockCopy(requestPayload, i, chunk, 0, chunkLen);
                    i += chunkLen;
                    if (fieldNumber == 1)
                    {
                        questChunks.Add(chunk);
                        questIds.Add(ParseQuestId(chunk));
                    }
                }
                else
                {
                    break; // 不认识的 wire type, 放弃继续解析
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning("[BattleQuestReward] 解析请求失败: {Msg}, 回退空响应", ex.Message);
            return Array.Empty<byte>();
        }

        var writer = ProtoBuilder.Write();

        // 命中无尽任务链 -> 下发下一波任务 (服务端驱动的任务推进)
        // 注意: 链上首步监听 CutCameraFinish 的任务 (EndlessQuestChain.EntryGated) 必须跳过 ——
        // CutCameraFinish 只在战斗进场流程派发, 单机无尽继续时不会再触发,
        // 这类任务 (Q1「加载完成」) 打开后会永远卡在首步。跳到下一个非门控任务 (Q2 类,
        // EndlessContinue 门控, 点继续后正常唤醒并自动刷怪)。
        var nextQuestIds = new List<int>();
        var sawChainQuest = false;   // 请求里至少有一个任务属于无尽链
        var chainStuck = false;      // 属于无尽链, 但一路跳过门控后到了链尾 / 无法解析
        foreach (var qid in questIds)
        {
            if (qid == 0)
                continue;
            if (!EndlessQuestChain.NextQuest.TryGetValue(qid, out var next))
                continue;   // 非无尽链任务 -> 走原回显分支
            sawChainQuest = true;
            var hops = 0;
            // 2026-09-26: 除进场门控 (CutCameraFinish, Q1 类) 外, 还要跳过交互门控
            // (InteractionGated, Q2 类「继续后必须手动点世界实体才刷怪」)。
            // 这类任务的步骤在客户端拿不到监听事件就永远不完成, 表现为
            // 「波次结算选继续后卡住、防御目标点不动、下一波不刷」。
            while ((EndlessQuestChain.EntryGated.Contains(next) ||
                    EndlessQuestChain.InteractionGated.Contains(next)) &&
                   EndlessQuestChain.NextQuest.TryGetValue(next, out var further) && ++hops < 24)
            {
                Log.Information("[BattleQuestReward] 跳过门控任务 {Skipped} ({Kind}) -> {Next}",
                    next,
                    EndlessQuestChain.EntryGated.Contains(next) ? "进场" : "交互",
                    further);
                next = further;
            }
            if (!EndlessQuestChain.EntryGated.Contains(next) &&
                !EndlessQuestChain.InteractionGated.Contains(next))
            {
                nextQuestIds.Add(next);
            }
            else
            {
                chainStuck = true;
                Log.Warning("[BattleQuestReward] 链尾仍是门控任务 {Next}, 不下发", next);
            }
        }

        if (nextQuestIds.Count > 0)
        {
            foreach (var next in nextQuestIds)
            {
                var inner = ProtoBuilder.Write();
                inner.WriteInt32(1, next);   // BattleQuestRewardData.QuestID
                inner.WriteInt32(2, 1);      // State = RewardStateType.Unfinished
                writer.WriteMessage(1, inner.ToBytes());
            }
            Log.Information("[BattleQuestReward] 无尽链推进: {Src} -> {Dst}",
                string.Join(",", questIds), string.Join(",", nextQuestIds));
        }
        else if (sawChainQuest && chainStuck)
        {
            // 无尽链走到底: 不回显请求里的旧任务 —— 回显会让客户端 QuestManager
            // 把已完成的旧任务重新 OpenQuest (链尾会原地重开一波)。
            // 下发空 QuestData, 客户端 BattleQuestRewardResponseCB 会清掉已完成任务后直接返回,
            // 关卡自然收尾 (结算窗里由玩家选择撤离)。 (2026-09-26)
            Log.Information("[BattleQuestReward] 无尽链已到链尾: {Src} (不下发新任务)", string.Join(",", questIds));
        }
        else
        {
            // 非无尽链任务: 保持原回显行为 (避免回归)
            foreach (var chunk in questChunks)
            {
                if (chunk.Length == 0) continue;
                writer.WriteMessage(1, chunk); // QuestData 原样回显
            }
        }

        if (round != 0)
            writer.WriteInt32(4, (int)round);
        if (seqId != 0)
            writer.WriteInt32(5, (int)seqId);
        return writer.ToBytes();
    }

    /// <summary>解析 BattleQuestRewardData chunk 的 field1 (QuestID varint)。失败返回 0。</summary>
    private static int ParseQuestId(byte[] chunk)
    {
        if (chunk == null || chunk.Length == 0) return 0;
        try
        {
            int i = 0;
            while (i < chunk.Length)
            {
                var tag = ProtoLevelIdRewriter.ReadVarint(chunk, i);
                if (tag == null) break;
                i += tag.Value.consumed;
                int fieldNumber = (int)(tag.Value.value >> 3);
                int wireType = (int)(tag.Value.value & 7);
                if (wireType == 0)
                {
                    var v = ProtoLevelIdRewriter.ReadVarint(chunk, i);
                    if (v == null) break;
                    i += v.Value.consumed;
                    if (fieldNumber == 1) return (int)v.Value.value;
                }
                else if (wireType == 2)
                {
                    var len = ProtoLevelIdRewriter.ReadVarint(chunk, i);
                    if (len == null) break;
                    i += len.Value.consumed + (int)len.Value.value;
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning("[BattleQuestReward] 解析 QuestID 失败: {Msg}", ex.Message);
        }
        return 0;
    }

    /// <summary>
    /// 原始 CityNodeInfos 快照 (Data/responses_backup/...bak_with_interids,
    /// 剥除互动标记前) 中各节点携带的互动 ID -> 所属节点映射。
    /// 用途: IngameInteractionChangeRequest 只有 InteractionID 一个字段,
    /// 响应却必须回客户端 MapProgressLogic 里存在的 NodeID, 以此映射反查。
    /// </summary>
    private static readonly Dictionary<int, int> KnownInteractionNode = new Dictionary<int, int>
    {
        [20300011] = 43600001,
        [20300012] = 43600001,
        [20300021] = 43600002,
        [20300022] = 43600002,
        [20300032] = 43600003,
        [20300051] = 43600005,
        [20300061] = 43600007,
        [20300062] = 43600007,
        [20300063] = 43600007,
        [20300081] = 43600010,
        [20300082] = 43600010,
        [20300101] = 43600012,
        [20300151] = 43600100,
        [20300152] = 43600100,
        [20300172] = 43600103,
        [20300221] = 43600108,
        [20300222] = 43600108,
        [20300231] = 43600109,
        [20300232] = 43600109,
        [20300261] = 43600302,
        [20300262] = 43600302,
        [20300272] = 43600303,
        [20300312] = 43600502,
        [20300331] = 43600200,
        [20300361] = 43600203,
        [20310011] = 43600001,
        [20310012] = 43600001,
        [20310021] = 43600002,
        [20310042] = 43600004,
        [20310051] = 43600005,
        [20310071] = 43600009,
        [20310102] = 43600012,
        [20310131] = 43600015,
        [20310172] = 43600103,
        [20310222] = 43600108,
        [20310231] = 43600109,
        [20310302] = 43600501,
        [20310332] = 43600200,
        [20320081] = 43600300,
        [20320101] = 43600302,
        [20320121] = 43600502,
        [20320151] = 43600103,
        [20320171] = 43600106,
        [20320181] = 43600100,
        [20320201] = 43600013,
        [20320211] = 43600108,
    };

    /// <summary>
    /// 构造 IngameInteractionChangeResponse。
    /// 请求: 1=InteractionID(int)。
    /// 响应: 1=NodeID(int) 2=InteractionIDs(repeated int, 解包编码)
    ///       3=UpdateInfo(msg) —— 省略, 客户端 Lua 侧有空值守卫。
    /// 命中映射: 回 NodeID + [InteractionID] (标记该互动物关闭);
    /// 未命中: 回空消息 (字段全默认, 客户端报错日志后跳过)。
    /// </summary>
    public static byte[] BuildIngameInteractionChangeResponse(byte[]? requestPayload)
    {
        long interactionId = 0;
        if (requestPayload != null && requestPayload.Length > 0)
        {
            int i = 0;
            try
            {
                while (i < requestPayload.Length)
                {
                    var tag = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
                    if (tag == null) break;
                    i += tag.Value.consumed;
                    int fieldNumber = (int)(tag.Value.value >> 3);
                    int wireType = (int)(tag.Value.value & 7);
                    if (wireType == 0)
                    {
                        var v = ProtoLevelIdRewriter.ReadVarint(requestPayload, i);
                        if (v == null) break;
                        i += v.Value.consumed;
                        if (fieldNumber == 1) { interactionId = v.Value.value; break; }
                    }
                    else
                    {
                        break; // 请求只有一个 varint 字段, 出现其它 wire type 即异常
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[IngameInteractionChange] 解析请求失败: {Msg}", ex.Message);
            }
        }

        if (interactionId != 0 && KnownInteractionNode.TryGetValue((int)interactionId, out var nodeId))
        {
            var writer = ProtoBuilder.Write();
            writer.WriteInt32(1, nodeId);
            writer.WriteInt32(2, (int)interactionId); // repeated int32 解包编码, C# 解析端兼容
            return writer.ToBytes();
        }

        return Array.Empty<byte>();
    }

    /// <summary>统一请求体 hex 记录(截断防刷屏)。</summary>
    private static void LogRequest(string route, byte[]? payload)
    {
        if (payload == null || payload.Length == 0)
        {
            Log.Information("[QuestStep] {Route}: 空请求体", route);
            return;
        }
        var max = Math.Min(payload.Length, 160);
        var hex = Convert.ToHexString(payload, 0, max);
        Log.Information("[QuestStep] {Route}: {Len}B hex={Hex}{Suffix}",
            route, payload.Length, hex, max < payload.Length ? "..." : "");
    }
}
