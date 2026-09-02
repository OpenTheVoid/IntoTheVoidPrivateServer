using System.Collections.Concurrent;
using System.Threading.Tasks;
using IntoTheVoidServer.Pomelo;
using Serilog;

namespace IntoTheVoidServer.Router;

public delegate Task<byte[]?> MessageHandler(byte[]? requestPayload, uint requestId);

public class MessageRouter
{
    private readonly ConcurrentDictionary<string, MessageHandler> _handlers = new();
    private readonly PlayerSessionManager _sessionManager;

    public MessageRouter(PlayerSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        RegisterDefaultHandlers();
    }

    public void RegisterHandler(string route, MessageHandler handler)
    {
        _handlers[route] = handler;
    }

    public async Task<byte[]?> HandleAsync(string route, byte[]? payload, MessageType msgType)
    {
        Log.Information("Route: {Route} (type={Type}, payload={Size} bytes)", route, msgType, payload?.Length ?? 0);

        if (_handlers.TryGetValue(route, out var handler))
            return await handler(payload, 0);

        Log.Warning("Unhandled route: {Route} - returning empty success", route);
        return Array.Empty<byte>();
    }

    private void RegisterDefaultHandlers()
    {
        // ===== String-based routes (gate server) =====
        RegisterHandler("gate.Entry", (_, _) => LogAndReturn("gate.Entry", ProtoBuilder.BuildEntryResponse(DateTimeOffset.UtcNow.ToUnixTimeSeconds())));

        // ===== String-based routes (game server - data requests) =====
        RegisterHandler("game.game.PlayerDataRequest", (_, _) => LogAndReturn("game.game.PlayerDataRequest", ProtoBuilder.BuildPlayerDataResponse()));
        RegisterHandler("game.game.QuestListRequest", (_, _) => LogAndReturn("game.game.QuestListRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.InteractionStateListRequest", (_, _) => LogAndReturn("game.game.InteractionStateListRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.CharacterSquadInfoRequest", (_, _) => LogAndReturn("game.game.CharacterSquadInfoRequest", ProtoBuilder.BuildCharacterSquadInfoResponse()));
        RegisterHandler("game.game.ShopInfoRequest", (_, _) => LogAndReturn("game.game.ShopInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.LimitedShopInfoRequest", (_, _) => LogAndReturn("game.game.LimitedShopInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.RivenShopInfoRequest", (_, _) => LogAndReturn("game.game.RivenShopInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.AirTypeEventInfoRequest", (_, _) => LogAndReturn("game.game.AirTypeEventInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.EventSingCheckInfoRequest", (_, _) => LogAndReturn("game.game.EventSingCheckInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.StageEventInfoRequest", (_, _) => LogAndReturn("game.game.StageEventInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.EventBountyInfoRequest", (_, _) => LogAndReturn("game.game.EventBountyInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.EventMilestoneInfoRequest", (_, _) => LogAndReturn("game.game.EventMilestoneInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.SystemBoostInfoRequest", (_, _) => LogAndReturn("game.game.SystemBoostInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.GetPhoneNumberBindInfoRequest", (_, _) => LogAndReturn("game.game.GetPhoneNumberBindInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.PurchaseRivenModInfoRequest", (_, _) => LogAndReturn("game.game.PurchaseRivenModInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.OverrideStrikeInfoRequest", (_, _) => LogAndReturn("game.game.OverrideStrikeInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.AlertEventInfoRequest", (_, _) => LogAndReturn("game.game.AlertEventInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.GetFriendSettingRequest", (_, _) => LogAndReturn("game.game.GetFriendSettingRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.ExaminationReadyTimestampRequest", (_, _) => LogAndReturn("game.game.ExaminationReadyTimestampRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.GetChatExpressionFavoriteRequest", (_, _) => LogAndReturn("game.game.GetChatExpressionFavoriteRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.PetIncubationTranscribeInfoRequest", (_, _) => LogAndReturn("game.game.PetIncubationTranscribeInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.GetPersonalizationDataRequest", (_, _) => LogAndReturn("game.game.GetPersonalizationDataRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.ItemDropWorldPoolRequest", (_, _) => LogAndReturn("game.game.ItemDropWorldPoolRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.CityExplorationInfoRequest", (_, _) => LogAndReturn("game.game.CityExplorationInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.SpecialLevelInfoRequest", (_, _) => LogAndReturn("game.game.SpecialLevelInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.WeeklyQuestInfoRequest", (_, _) => LogAndReturn("game.game.WeeklyQuestInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.SettleDataRequest", (_, _) => LogAndReturn("game.game.SettleDataRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.SetAvatarRequest", (_, _) => LogAndReturn("game.game.SetAvatarRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.SetNicknameRequest", (_, _) => LogAndReturn("game.game.SetNicknameRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.QuestRewardRequest", (_, _) => LogAndReturn("game.game.QuestRewardRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.ModRevealRequest", (_, _) => LogAndReturn("game.game.ModRevealRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.ModRecastRequest", (_, _) => LogAndReturn("game.game.ModRecastRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.PetTranscribeAccelerateRequest", (_, _) => LogAndReturn("game.game.PetTranscribeAccelerateRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.PetTranscribeClaimRequest", (_, _) => LogAndReturn("game.game.PetTranscribeClaimRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.PutAwayTheFurnitureRequest", (_, _) => LogAndReturn("game.game.PutAwayTheFurnitureRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.CheatGuildResearchRequest", (_, _) => LogAndReturn("game.game.CheatGuildResearchRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.RechargeRewardPickRequest", (_, _) => LogAndReturn("game.game.RechargeRewardPickRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.AddQuestPoolRequest", (_, _) => LogAndReturn("game.game.AddQuestPoolRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.GetEnemyKillInfoRequest", (_, _) => LogAndReturn("game.game.GetEnemyKillInfoRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.PetFreeRequest", (_, _) => LogAndReturn("game.game.PetFreeRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.LogPushRequest", (_, _) => LogAndReturn("game.game.LogPushRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("game.game.StepPushRequest", (_, _) => LogAndReturn("game.game.StepPushRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("scene.scene.StatusRequest", (_, _) => LogAndReturn("scene.scene.StatusRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("scene.scene.SceneMoveRequest", (_, _) => LogAndReturn("scene.scene.SceneMoveRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("scene.scene.ScenePlayerActionRequest", (_, _) => LogAndReturn("scene.scene.ScenePlayerActionRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("scene.scene.SceneInteractiveChangeRequest", (_, _) => LogAndReturn("scene.scene.SceneInteractiveChangeRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("scene.scene.CreateTownInteractiveRequest", (_, _) => LogAndReturn("scene.scene.CreateTownInteractiveRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("scene.scene.ChangeTownInteractiveRequest", (_, _) => LogAndReturn("scene.scene.ChangeTownInteractiveRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("scene.scene.DeleteTownInteractiveRequest", (_, _) => LogAndReturn("scene.scene.DeleteTownInteractiveRequest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("gate.EnterSceneRoomRequest", (_, _) => LogAndReturn("gate.EnterSceneRoomRequest", ProtoBuilder.BuildEnterSceneRoomResponse()));
        RegisterHandler("gate.LeaveSceneRoomRequest", (_, _) => LogAndReturn("gate.LeaveSceneRoomRequest", ProtoBuilder.BuildSuccess()));

        // ===== Login flow (compressed route IDs) =====
        RegisterHandler("230", (_, _) => LogAndReturn("AcquireSessionTicket", ProtoBuilder.BuildSuccess()));
        RegisterHandler("2", (_, _) => LogAndReturn("Entry", ProtoBuilder.BuildEntryResponse(DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
        RegisterHandler("232", (_, _) => LogAndReturn("LoginDeviceInfo", ProtoBuilder.BuildLoginDeviceInfoResponse()));
        RegisterHandler("234", (_, _) => LogAndReturn("LogOff", ProtoBuilder.BuildSuccess()));
        RegisterHandler("235", (_, _) => LogAndReturn("PlayerLogout", ProtoBuilder.BuildSuccess()));
        RegisterHandler("26", (_, _) => LogAndReturn("PlayerCheck", ProtoBuilder.BuildSuccess()));

        // ===== Player data =====
        RegisterHandler("6", (_, _) => LogAndReturn("PlayerData", ProtoBuilder.BuildPlayerDataResponse()));
        RegisterHandler("78", (_, _) => LogAndReturn("GetProfile", ProtoBuilder.BuildSuccess()));
        RegisterHandler("80", (_, _) => LogAndReturn("SetAvatar", ProtoBuilder.BuildSuccess()));
        RegisterHandler("81", (_, _) => LogAndReturn("SetNickname", ProtoBuilder.BuildSuccess()));

        // ===== Inventory =====
        RegisterHandler("1", (_, _) => LogAndReturn("BackPackList", ProtoBuilder.BuildBackPackListResponse()));
        RegisterHandler("4", (_, _) => LogAndReturn("AddBackItem", ProtoBuilder.BuildSuccess()));
        RegisterHandler("5", (_, _) => LogAndReturn("BagItemProcess", ProtoBuilder.BuildSuccess()));
        RegisterHandler("19", (_, _) => LogAndReturn("UseBackItem", ProtoBuilder.BuildSuccess()));
        RegisterHandler("11", (_, _) => LogAndReturn("ItemLock", ProtoBuilder.BuildSuccess()));
        RegisterHandler("42", (_, _) => LogAndReturn("UseRewardChestItem", ProtoBuilder.BuildSuccess()));
        RegisterHandler("124", (_, _) => LogAndReturn("ItemDropWorldPool", ProtoBuilder.BuildSuccess()));
        RegisterHandler("126", (_, _) => LogAndReturn("ReceiveDropWorldPool", ProtoBuilder.BuildSuccess()));

        // ===== Character =====
        RegisterHandler("9", (_, _) => LogAndReturn("CharacterSquadInfo", ProtoBuilder.BuildCharacterSquadInfoResponse()));
        RegisterHandler("10", (_, _) => LogAndReturn("ChangeSquad", ProtoBuilder.BuildSuccess()));
        RegisterHandler("3", (_, _) => LogAndReturn("SetCurrentSquad", ProtoBuilder.BuildSuccess()));
        RegisterHandler("8", (_, _) => LogAndReturn("ChangeWeapon", ProtoBuilder.BuildSuccess()));
        RegisterHandler("47", (_, _) => LogAndReturn("GetCurrentShowCharacter", ProtoBuilder.BuildSuccess()));
        RegisterHandler("48", (_, _) => LogAndReturn("SetCurrentShowCharacter", ProtoBuilder.BuildSuccess()));
        RegisterHandler("240", (_, _) => LogAndReturn("ChangeFrameSkin", ProtoBuilder.BuildSuccess()));
        RegisterHandler("241", (_, _) => LogAndReturn("CharacterRareUp", ProtoBuilder.BuildSuccess()));

        // ===== Quest =====
        RegisterHandler("20", (_, _) => LogAndReturn("QuestList", ProtoBuilder.BuildSuccess()));
        RegisterHandler("22", (_, _) => LogAndReturn("QuestReward", ProtoBuilder.BuildSuccess()));
        RegisterHandler("44", (_, _) => LogAndReturn("SetQuestSteps", ProtoBuilder.BuildSuccess()));
        RegisterHandler("46", (_, _) => LogAndReturn("ChooseQuestReward", ProtoBuilder.BuildSuccess()));
        RegisterHandler("77", (_, _) => LogAndReturn("ReceiveQuestReward", ProtoBuilder.BuildSuccess()));

        // ===== Shop =====
        RegisterHandler("24", (_, _) => LogAndReturn("ShopBuy", ProtoBuilder.BuildSuccess()));
        RegisterHandler("73", (_, _) => LogAndReturn("ShopInfo", ProtoBuilder.BuildSuccess()));

        // ===== Level/Scene =====
        RegisterHandler("110", (_, _) => LogAndReturn("LevelBegin", ProtoBuilder.BuildSuccess()));
        RegisterHandler("112", (_, _) => LogAndReturn("LevelResource", ProtoBuilder.BuildSuccess()));
        RegisterHandler("113", (_, _) => LogAndReturn("LevelEnemyDrop", ProtoBuilder.BuildSuccess()));
        RegisterHandler("114", (_, _) => LogAndReturn("CreateSceneRoom", ProtoBuilder.BuildSuccess()));
        RegisterHandler("115", (_, _) => LogAndReturn("EnterSceneRoom", ProtoBuilder.BuildSuccess()));
        RegisterHandler("117", (_, _) => LogAndReturn("GetSceneRoomList", ProtoBuilder.BuildSuccess()));
        RegisterHandler("119", (_, _) => LogAndReturn("SceneMove", ProtoBuilder.BuildSuccess()));

        // ===== Settlement =====
        RegisterHandler("14", (_, _) => LogAndReturn("SettleData", ProtoBuilder.BuildSuccess()));
        RegisterHandler("134", (_, _) => LogAndReturn("GetSettleData", ProtoBuilder.BuildSuccess()));
        RegisterHandler("135", (_, _) => LogAndReturn("GetTempSettleData", ProtoBuilder.BuildSuccess()));
        RegisterHandler("136", (_, _) => LogAndReturn("ReceiveSettleData", ProtoBuilder.BuildSuccess()));

        // ===== Craft =====
        RegisterHandler("31", (_, _) => LogAndReturn("Craft", ProtoBuilder.BuildSuccess()));
        RegisterHandler("32", (_, _) => LogAndReturn("CraftList", ProtoBuilder.BuildSuccess()));
        RegisterHandler("33", (_, _) => LogAndReturn("CraftAccelerate", ProtoBuilder.BuildSuccess()));
        RegisterHandler("35", (_, _) => LogAndReturn("CraftCancel", ProtoBuilder.BuildSuccess()));
        RegisterHandler("36", (_, _) => LogAndReturn("CraftClaim", ProtoBuilder.BuildSuccess()));

        // ===== Talent/Science =====
        RegisterHandler("200", (_, _) => LogAndReturn("TalentTreeInfo", ProtoBuilder.BuildSuccess()));
        RegisterHandler("201", (_, _) => LogAndReturn("UnlockTalent", ProtoBuilder.BuildSuccess()));
        RegisterHandler("210", (_, _) => LogAndReturn("ScienceTreeInfo", ProtoBuilder.BuildSuccess()));
        RegisterHandler("211", (_, _) => LogAndReturn("ScienceTreeActiveNode", ProtoBuilder.BuildSuccess()));

        // ===== Rogue =====
        RegisterHandler("190", (_, _) => LogAndReturn("RogueAllInfo", ProtoBuilder.BuildSuccess()));
        RegisterHandler("191", (_, _) => LogAndReturn("RogueBattleBegin", ProtoBuilder.BuildSuccess()));
        RegisterHandler("192", (_, _) => LogAndReturn("RogueGetLevelReward", ProtoBuilder.BuildSuccess()));
        RegisterHandler("193", (_, _) => LogAndReturn("RogueSettle", ProtoBuilder.BuildSuccess()));

        // ===== GM/Cheat Commands =====
        RegisterHandler("18", (_, _) => LogAndReturn("GM: CheatUnlockCity", ProtoBuilder.BuildSuccess()));
        RegisterHandler("30", (_, _) => LogAndReturn("GM: CheatAddMR", ProtoBuilder.BuildSuccess()));
        RegisterHandler("34", (_, _) => LogAndReturn("GM: CheatAddEquipmentExp", ProtoBuilder.BuildSuccess()));
        RegisterHandler("37", (_, _) => LogAndReturn("GM: CheatPlayerLvUp", ProtoBuilder.BuildSuccess()));
        RegisterHandler("38", (_, _) => LogAndReturn("GM: CheatGraduate", ProtoBuilder.BuildSuccess()));
        RegisterHandler("43", (_, _) => LogAndReturn("GM: CheatAddRewardChest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("45", (_, _) => LogAndReturn("GM: CheatSetQuest", ProtoBuilder.BuildSuccess()));
        RegisterHandler("75", (_, _) => LogAndReturn("GM: CheatSetSlice", ProtoBuilder.BuildSuccess()));
        RegisterHandler("88", (_, _) => LogAndReturn("GM: CheatCleanBag", ProtoBuilder.BuildSuccess()));
        RegisterHandler("89", (_, _) => LogAndReturn("GM: CheatClearShopBuyInfo", ProtoBuilder.BuildSuccess()));
        RegisterHandler("90", (_, _) => LogAndReturn("GM: CheatCompleteBounty", ProtoBuilder.BuildSuccess()));
        RegisterHandler("92", (_, _) => LogAndReturn("GM: CheatFullRankAllMod", ProtoBuilder.BuildSuccess()));
        RegisterHandler("94", (_, _) => LogAndReturn("GM: CheatPaymentFinish", ProtoBuilder.BuildSuccess()));
        RegisterHandler("98", (_, _) => LogAndReturn("GM: CheatUnlockPack", ProtoBuilder.BuildSuccess()));
        RegisterHandler("100", (_, _) => LogAndReturn("GM: CheatUnlockAllEnemy", ProtoBuilder.BuildSuccess()));
        RegisterHandler("101", (_, _) => LogAndReturn("GM: CheatStalkerWeightSet", ProtoBuilder.BuildSuccess()));
        RegisterHandler("102", (_, _) => LogAndReturn("GM: CheatTreeOpen", ProtoBuilder.BuildSuccess()));
        RegisterHandler("103", (_, _) => LogAndReturn("GM: CheatGuildResearch", ProtoBuilder.BuildSuccess()));
        RegisterHandler("250", (_, _) => LogAndReturn("GM: CheatAddBounty", ProtoBuilder.BuildSuccess()));
        RegisterHandler("251", (_, _) => LogAndReturn("GM: CheatAddLostMail", ProtoBuilder.BuildSuccess()));
        RegisterHandler("252", (_, _) => LogAndReturn("GM: CheatAddWellingPoints", ProtoBuilder.BuildSuccess()));
        RegisterHandler("253", (_, _) => LogAndReturn("GM: CheatChangeUserCamp", ProtoBuilder.BuildSuccess()));
        RegisterHandler("254", (_, _) => LogAndReturn("GM: CheatFrameSkillAndWeaponSlot", ProtoBuilder.BuildSuccess()));
        RegisterHandler("255", (_, _) => LogAndReturn("GM: CheatGetAllShownMod", ProtoBuilder.BuildSuccess()));
        RegisterHandler("256", (_, _) => LogAndReturn("GM: CheatPolarization", ProtoBuilder.BuildSuccess()));
        RegisterHandler("257", (_, _) => LogAndReturn("GM: CheatQuestShowMulti", ProtoBuilder.BuildSuccess()));
        RegisterHandler("258", (_, _) => LogAndReturn("GM: CheatRoadSRankBreak", ProtoBuilder.BuildSuccess()));
        RegisterHandler("259", (_, _) => LogAndReturn("GM: CheatRogueOutInfo", ProtoBuilder.BuildSuccess()));
        RegisterHandler("260", (_, _) => LogAndReturn("GM: CheatSearchPoint", ProtoBuilder.BuildSuccess()));
        RegisterHandler("261", (_, _) => LogAndReturn("GM: CheatSetBackFlag", ProtoBuilder.BuildSuccess()));
        RegisterHandler("262", (_, _) => LogAndReturn("GM: CheatSetMileScore", ProtoBuilder.BuildSuccess()));
        RegisterHandler("263", (_, _) => LogAndReturn("GM: CheatSignIn", ProtoBuilder.BuildSuccess()));
        RegisterHandler("264", (_, _) => LogAndReturn("GM: CheatTransformNormal", ProtoBuilder.BuildSuccess()));
        RegisterHandler("265", (_, _) => LogAndReturn("GM: CheatWildOffer", ProtoBuilder.BuildSuccess()));
        RegisterHandler("130", (_, _) => LogAndReturn("GM: GMTeamEnterLevel", ProtoBuilder.BuildSuccess()));
        RegisterHandler("266", (_, _) => LogAndReturn("GM: GMPetUpRank", ProtoBuilder.BuildSuccess()));
        RegisterHandler("267", (_, _) => LogAndReturn("GM: GMAddGuildPoint", ProtoBuilder.BuildSuccess()));
        RegisterHandler("268", (_, _) => LogAndReturn("GM: GMGuildRepair", ProtoBuilder.BuildSuccess()));

        // ===== Mail =====
        RegisterHandler("55", (_, _) => LogAndReturn("MailFetch", ProtoBuilder.BuildSuccess()));
        RegisterHandler("56", (_, _) => LogAndReturn("MailRead", ProtoBuilder.BuildSuccess()));
        RegisterHandler("57", (_, _) => LogAndReturn("MailReceive", ProtoBuilder.BuildSuccess()));
        RegisterHandler("58", (_, _) => LogAndReturn("MailDelete", ProtoBuilder.BuildSuccess()));

        // ===== Social =====
        RegisterHandler("63", (_, _) => LogAndReturn("SearchUser", ProtoBuilder.BuildSuccess()));
        RegisterHandler("64", (_, _) => LogAndReturn("GetFriendList", ProtoBuilder.BuildSuccess()));
        RegisterHandler("65", (_, _) => LogAndReturn("AddFriend", ProtoBuilder.BuildSuccess()));
        RegisterHandler("66", (_, _) => LogAndReturn("AcceptFriend", ProtoBuilder.BuildSuccess()));

        // ===== Pet =====
        RegisterHandler("150", (_, _) => LogAndReturn("PetIncubation", ProtoBuilder.BuildSuccess()));

        // ===== Guild =====
        RegisterHandler("160", (_, _) => LogAndReturn("CreateGuild", ProtoBuilder.BuildSuccess()));
        RegisterHandler("161", (_, _) => LogAndReturn("LoadGuildInfo", ProtoBuilder.BuildSuccess()));

        // ===== Reputation =====
        RegisterHandler("220", (_, _) => LogAndReturn("ReputationTaskInfo", ProtoBuilder.BuildSuccess()));
        RegisterHandler("221", (_, _) => LogAndReturn("ReputationTaskReward", ProtoBuilder.BuildSuccess()));

        // ===== Payment =====
        RegisterHandler("180", (_, _) => LogAndReturn("CreatePayment", ProtoBuilder.BuildSuccess()));
        RegisterHandler("181", (_, _) => LogAndReturn("CheatPaymentFinish", ProtoBuilder.BuildSuccess()));
    }

    private Task<byte[]?> LogAndReturn(string handlerName, byte[]? payload)
    {
        Log.Information("  -> {Handler}: returning {Size} bytes", handlerName, payload?.Length ?? 0);
        return Task.FromResult(payload);
    }
}

public class PlayerSessionManager
{
    public string CurrentPlayerId { get; set; } = "offline_player_001";
    public string SessionTicket { get; set; } = "offline_session_ticket";
    public bool IsLoggedIn { get; set; }
    public Dictionary<string, object> PlayerData { get; set; } = new();
}
