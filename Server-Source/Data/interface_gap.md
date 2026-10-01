# 客户端接口 vs 本地私服 · 覆盖缺口报告

> 本文件由 `tools/interface_gap.py` 自动生成, 请勿手工编辑。
> 数据源: `Data/proto_schema.json` (提取自热更程序集 `Assembly-CSharp.dll`)

## 总览

| 指标 | 数量 |
|---|---|
| 客户端 Request 类型总数 | 430 |
| 私服已覆盖 | 183 |
| 未覆盖 | 247 |
| 覆盖率 | 42.6% |

## 客户端实际调用但缺少回放响应的接口 (高优先级)

这些接口在服务端日志中出现过 `No captured response`, 
说明客户端**确实会调用**, 优先补齐:

| 接口 | 客户端字段数 | 响应类型 | 响应字段数 |
|---|---|---|---|
| `MRItemCompletedRankRequest` | 1 | `MRItemCompletedRankResponse` | 1 |
| `ProfileRequest` | 1 | `ProfileResponse` | 2 |
| `GetProfileRequest` | 1 | `GetProfileResponse` | 19 |
| `GetGuildMembersRequest` | 1 | `GetGuildMembersResponse` | 2 |
| `RechargeInfoRequest` | 0 | `RechargeInfoResponse` | 1 |
| `LogPushRequest` | 1 | `LogPushResponse` | 0 |
| `ClearRedPointRequest` | 1 | `ClearRedPointResponse` | 1 |
| `ServerTagListRequest` | 0 | `ServerTagListResponse` | 1 |
| `LoginDeviceInfoRequest` | 0 | `LoginDeviceInfoResponse` | 1 |
| `GetGuildRepairListRequest` | 0 | `GetGuildRepairListResponse` | 2 |
| `GetAtlasInfoRequest` | 0 | `GetAtlasInfoResponse` | 2 |

## 未覆盖接口全集 (247 个)

<details><summary>点击展开</summary>

| # | 接口 | 请求字段数 | 响应类型 | 响应字段数 |
|---|---|---|---|---|
| 1 | `AchievementRewardRequest` | 1 | `AchievementRewardResponse` | 2 |
| 2 | `ActivationInfoRequest` | 0 | `ActivationInfoResponse` | 4 |
| 3 | `ActivationRewardRequest` | 0 | `ActivationRewardResponse` | 2 |
| 4 | `ActivationTaskCompleteRequest` | 1 | `ActivationTaskCompleteResponse` | 2 |
| 5 | `ActivityChapterInfoRequest` | 0 | `ActivityChapterInfoResponse` | 1 |
| 6 | `ActivityPickRewardRequest` | 1 | `ActivityPickRewardResponse` | 2 |
| 7 | `AlertEventRefreshCheckRequest` | 1 | `AlertEventRefreshCheckResponse` | 1 |
| 8 | `AnnouncementInfoRequest` | 0 | `AnnouncementInfoResponse` | 1 |
| 9 | `AppleReceiptVerifyRequest` | 1 | `AppleReceiptVerifyResponse` | 4 |
| 10 | `ApplyJoinGuildRequest` | 1 | `ApplyJoinGuildResponse` | 3 |
| 11 | `BackMissionCompleteRequest` | 1 | `BackMissionCompleteResponse` | 1 |
| 12 | `BackMissionPickRewardRequest` | 1 | `BackMissionPickRewardResponse` | 3 |
| 13 | `BackNewActiveRewardRequest` | 0 | `BackNewActiveRewardResponse` | 1 |
| 14 | `BackRechargeRewardPickRequest` | 1 | `BackRechargeRewardPickResponse` | 3 |
| 15 | `BackSignCheckRewardRequest` | 1 | `BackSignCheckRewardResponse` | 2 |
| 16 | `BattlePassInfoRequest` | 0 | `BattlePassInfoResponse` | 8 |
| 17 | `BattlePassRewardRequest` | 3 | `BattlePassRewardResponse` | 3 |
| 18 | `BattlePassRewardedInfoRequest` | 0 | `BattlePassRewardedInfoResponse` | 2 |
| 19 | `BattlePassTaskInfoRequest` | 0 | `BattlePassTaskInfoResponse` | 3 |
| 20 | `BattlePassTaskRewardRequest` | 1 | `BattlePassTaskRewardResponse` | 2 |
| 21 | `BidMarketAuctionRequest` | 2 | `BidMarketAuctionResponse` | 6 |
| 22 | `BindPhoneNumberRequest` | 2 | `BindPhoneNumberResponse` | 2 |
| 23 | `BlockFriendRequest` | 1 | `BlockFriendResponse` | 1 |
| 24 | `CancelMarketAuctionRequest` | 1 | `CancelMarketAuctionResponse` | 2 |
| 25 | `CancelMarketOrderRequest` | 1 | `CancelMarketOrderResponse` | 2 |
| 26 | `CancelTrackItemRequest` | 1 | `CancelTrackItemResponse` | 1 |
| 27 | `CancelTrackTaskRequest` | 2 | `CancelTrackTaskResponse` | 1 |
| 28 | `ChangeCarrierNameRequest` | 4 | `ChangeCarrierNameResponse` | 4 |
| 29 | `ChangeDecorateRequest` | 2 | `ChangeDecorateResponse` | 2 |
| 30 | `ChangeFrameSkinAccessoryColorRequest` | 5 | `ChangeFrameSkinAccessoryColorResponse` | 6 |
| 31 | `ChangeRogueSquadRequest` | 2 | `ChangeRogueSquadResponse` | 2 |
| 32 | `CheatAllEquipmentLvUpToMaxRequest` | 0 | `CheatAllEquipmentLvUpToMaxResponse` | 1 |
| 33 | `CheatCustomizeRivenModRequest` | 4 | `CheatCustomizeRivenModResponse` | 1 |
| 34 | `ChooseOptionalBoxItemRequest` | 3 | `ChooseOptionalBoxItemResponse` | 1 |
| 35 | `ClearRedPointRequest` | 1 | `ClearRedPointResponse` | 1 |
| 36 | `ConveneGetCalledRewardRequest` | 1 | `ConveneGetCalledRewardResponse` | 3 |
| 37 | `ConveneInfoRequest` | 0 | `ConveneInfoResponse` | 8 |
| 38 | `ConveneMissionCompleteRequest` | 1 | `ConveneMissionCompleteResponse` | 2 |
| 39 | `CreateMarketAuctionRequest` | 4 | `CreateMarketAuctionResponse` | 2 |
| 40 | `CubeUpgradeRequest` | 1 | `CubeUpgradeResponse` | 2 |
| 41 | `CurrencyExchangeRequest` | 3 | `CurrencyExchangeResponse` | 4 |
| 42 | `DeleteRecentChatPlayerRequest` | 1 | `DeleteRecentChatPlayerResponse` | 1 |
| 43 | `DestroyItemInLostRequest` | 2 | `DestroyItemInLostResponse` | 2 |
| 44 | `DonateGuildRequest` | 2 | `DonateGuildResponse` | 2 |
| 45 | `EnterOtherTeamRequest` | 1 | `EnterOtherTeamResponse` | 0 |
| 46 | `EventAcceptBountyRequest` | 2 | `EventAcceptBountyResponse` | 2 |
| 47 | `EventBountyRewardRequest` | 2 | `EventBountyRewardResponse` | 3 |
| 48 | `EventCancelBountyRequest` | 2 | `EventCancelBountyResponse` | 2 |
| 49 | `EventChangeInfoRequest` | 4 | `EventChangeInfoResponse` | 15 |
| 50 | `EventChooseMileTypeRequest` | 1 | `EventChooseMileTypeResponse` | 2 |
| 51 | `EventPickRandomBountyRequest` | 1 | `EventPickRandomBountyResponse` | 2 |
| 52 | `EventRedPointRefreshCheckRequest` | 1 | `EventRedPointRefreshCheckResponse` | 1 |
| 53 | `EventSingCheckRewardRequest` | 2 | `EventSingCheckRewardResponse` | 3 |
| 54 | `ExchangeItemRequest` | 2 | `ExchangeItemResponse` | 2 |
| 55 | `ExitGuildRequest` | 0 | `ExitGuildResponse` | 0 |
| 56 | `ExplorationAreaRewardRequest` | 1 | `ExplorationAreaRewardResponse` | 3 |
| 57 | `ExplorationCityRewardRequest` | 1 | `ExplorationCityRewardResponse` | 3 |
| 58 | `FakeGambleInfoRequest` | 0 | `FakeGambleInfoResponse` | 1 |
| 59 | `FakeGamblePlayRequest` | 1 | `FakeGamblePlayResponse` | 4 |
| 60 | `FeedbackRequest` | 1 | `FeedbackResponse` | 0 |
| 61 | `FrameSetSlotSkillRequest` | 3 | `FrameSetSlotSkillResponse` | 2 |
| 62 | `FrameSkillActiveRequest` | 2 | `FrameSkillActiveResponse` | 2 |
| 63 | `FrameSkillLevelUpRequest` | 3 | `FrameSkillLevelUpResponse` | 2 |
| 64 | `FrameSkillRefreshRequest` | 4 | `FrameSkillRefreshResponse` | 2 |
| 65 | `GambleGuaranteeRewardRequest` | 1 | `GambleGuaranteeRewardResponse` | 3 |
| 66 | `GamblePlayRequest` | 1 | `GamblePlayResponse` | 4 |
| 67 | `GenerateCodeRequest` | 0 | `GenerateCodeResponse` | 1 |
| 68 | `GetAtlasInfoRequest` | 0 | `GetAtlasInfoResponse` | 2 |
| 69 | `GetBiddenAuctionRequest` | 0 | `GetBiddenAuctionResponse` | 1 |
| 70 | `GetGuildDonateRecordRequest` | 1 | `GetGuildDonateRecordResponse` | 2 |
| 71 | `GetGuildFurnitureBuildInfoRequest` | 1 | `GetGuildFurnitureBuildInfoResponse` | 1 |
| 72 | `GetGuildItemListRequest` | 0 | `GetGuildItemListResponse` | 1 |
| 73 | `GetGuildListRequest` | 1 | `GetGuildListResponse` | 3 |
| 74 | `GetGuildMembersRequest` | 1 | `GetGuildMembersResponse` | 2 |
| 75 | `GetGuildPointRankRequest` | 0 | `GetGuildPointRankResponse` | 2 |
| 76 | `GetGuildPointRewardRequest` | 1 | `GetGuildPointRewardResponse` | 3 |
| 77 | `GetGuildRecordRequest` | 0 | `GetGuildRecordResponse` | 1 |
| 78 | `GetGuildRepairListRequest` | 0 | `GetGuildRepairListResponse` | 2 |
| 79 | `GetGuildResearchInfoRequest` | 0 | `GetGuildResearchInfoResponse` | 2 |
| 80 | `GetHistoryMessageRequest` | 3 | `GetHistoryMessageResponse` | 1 |
| 81 | `GetItemFromLostRequest` | 2 | `GetItemFromLostResponse` | 3 |
| 82 | `GetItemPriceLadderRequest` | 2 | `GetItemPriceLadderResponse` | 1 |
| 83 | `GetLibraryDataRequest` | 0 | `GetLibraryDataResponse` | 2 |
| 84 | `GetMarketAuctionInfoRequest` | 1 | `GetMarketAuctionInfoResponse` | 1 |
| 85 | `GetMarketOrderItemLockRequest` | 1 | `GetMarketOrderItemLockResponse` | 2 |
| 86 | `GetMarketStatusRequest` | 0 | `GetMarketStatusResponse` | 2 |
| 87 | `GetMilestoneJoinRewardRequest` | 1 | `GetMilestoneJoinRewardResponse` | 3 |
| 88 | `GetMilestoneRewardRequest` | 3 | `GetMilestoneRewardResponse` | 5 |
| 89 | `GetPersonalMarketAuctionRequest` | 0 | `GetPersonalMarketAuctionResponse` | 1 |
| 90 | `GetPersonalMarketHistoryRequest` | 0 | `GetPersonalMarketHistoryResponse` | 7 |
| 91 | `GetPersonalMarketOrderRequest` | 0 | `GetPersonalMarketOrderResponse` | 1 |
| 92 | `GetPhoneNumberBindRewardRequest` | 1 | `GetPhoneNumberBindRewardResponse` | 3 |
| 93 | `GetRecentlyTeamPlayersRequest` | 0 | `GetRecentlyTeamPlayersResponse` | 1 |
| 94 | `GrowthRebateGetRequest` | 1 | `GrowthRebateGetResponse` | 2 |
| 95 | `GuildFurnitureBuildRequest` | 3 | `GuildFurnitureBuildResponse` | 5 |
| 96 | `GuildQuestRewardRequest` | 1 | `GuildQuestRewardResponse` | 4 |
| 97 | `GuildRepairRequest` | 3 | `GuildRepairResponse` | 6 |
| 98 | `GuildResearchBuyRequest` | 1 | `GuildResearchBuyResponse` | 3 |
| 99 | `GuildResearchRequest` | 3 | `GuildResearchResponse` | 5 |
| 100 | `GuildSueRequest` | 0 | `GuildSueResponse` | 1 |
| 101 | `HonorPaymentVerifyRequest` | 2 | `HonorPaymentVerifyResponse` | 0 |
| 102 | `HuaweiPaymentVerifyRequest` | 2 | `HuaweiPaymentVerifyResponse` | 0 |
| 103 | `IngameInteractionChangeRequest` | 1 | `IngameInteractionChangeResponse` | 3 |
| 104 | `InviteBackTeamRequest` | 2 | `InviteBackTeamResponse` | 0 |
| 105 | `InviteJoinBackSceneRoomRequest` | 3 | `InviteJoinBackSceneRoomResponse` | 0 |
| 106 | `InviteJoinSceneRoomRequest` | 1 | `InviteJoinSceneRoomResponse` | 1 |
| 107 | `InviteTeamRequest` | 1 | `InviteTeamResponse` | 0 |
| 108 | `JoinSceneRoomPosRequest` | 1 | `JoinSceneRoomPosResponse` | 1 |
| 109 | `JoinSceneRoomRequest` | 1 | `JoinSceneRoomResponse` | 1 |
| 110 | `JoinTeamRequest` | 1 | `JoinTeamResponse` | 0 |
| 111 | `LimitedShopBuyRequest` | 2 | `LimitedShopBuyResponse` | 3 |
| 112 | `LoadTowerRequest` | 3 | `LoadTowerResponse` | 2 |
| 113 | `LostItemPackInfoRequest` | 0 | `LostItemPackInfoResponse` | 1 |
| 114 | `MRItemCompletedRankRequest` | 1 | `MRItemCompletedRankResponse` | 1 |
| 115 | `MainNoobBookRewardRequest` | 0 | `MainNoobBookRewardResponse` | 4 |
| 116 | `MarketAuctionAttendRequest` | 1 | `MarketAuctionAttendResponse` | 1 |
| 117 | `MarketAuctionAttentionListRequest` | 0 | `MarketAuctionAttentionListResponse` | 1 |
| 118 | `MarketAuctionUnfollowRequest` | 1 | `MarketAuctionUnfollowResponse` | 1 |
| 119 | `MarketDailyActionCountRequest` | 0 | `MarketDailyActionCountResponse` | 2 |
| 120 | `MarketOrderAttendRequest` | 1 | `MarketOrderAttendResponse` | 1 |
| 121 | `MarketOrderAttentionListRequest` | 0 | `MarketOrderAttentionListResponse` | 1 |
| 122 | `MarketOrderUnfollowRequest` | 1 | `MarketOrderUnfollowResponse` | 1 |
| 123 | `MatchTeamAddRequest` | 1 | `MatchTeamAddResponse` | 1 |
| 124 | `MatchTeamCancelRequest` | 0 | `MatchTeamCancelResponse` | 0 |
| 125 | `ModAllUnloadRequest` | 1 | `ModAllUnloadResponse` | 2 |
| 126 | `ModCastRequest` | 2 | `ModCastResponse` | 1 |
| 127 | `ModFullRankWithCoreRequest` | 1 | `ModFullRankWithCoreResponse` | 4 |
| 128 | `ModSetLoadMapRequest` | 4 | `ModSetLoadMapResponse` | 6 |
| 129 | `MonthPassCheckRequest` | 0 | `MonthPassCheckResponse` | 3 |
| 130 | `MonthlyPassGetRequest` | 0 | `MonthlyPassGetResponse` | 4 |
| 131 | `NameSquadRequest` | 2 | `NameSquadResponse` | 2 |
| 132 | `NoobBookInfoRequest` | 0 | `NoobBookInfoResponse` | 3 |
| 133 | `NoobBookItemsRewardRequest` | 1 | `NoobBookItemsRewardResponse` | 2 |
| 134 | `NormalModTransformRequest` | 1 | `NormalModTransformResponse` | 2 |
| 135 | `NpcExchangeInfoRequest` | 1 | `NpcExchangeInfoResponse` | 3 |
| 136 | `NpcExchangeRequest` | 1 | `NpcExchangeResponse` | 4 |
| 137 | `OperateGuildRequest` | 5 | `OperateGuildResponse` | 4 |
| 138 | `OperateTeamRequest` | 3 | `OperateTeamResponse` | 3 |
| 139 | `OverrideStrikeRefreshCheckRequest` | 0 | `OverrideStrikeRefreshCheckResponse` | 0 |
| 140 | `PetIncubationAccelerateRequest` | 0 | `PetIncubationAccelerateResponse` | 2 |
| 141 | `PetIncubationClaimRequest` | 1 | `PetIncubationClaimResponse` | 1 |
| 142 | `PetLockRequest` | 2 | `PetLockResponse` | 2 |
| 143 | `PetNameRequest` | 2 | `PetNameResponse` | 4 |
| 144 | `PetTranscribeRequest` | 1 | `PetTranscribeResponse` | 4 |
| 145 | `PlaceMarketOrderRequest` | 4 | `PlaceMarketOrderResponse` | 3 |
| 146 | `PlacementOfFurnitureRequest` | 2 | `PlacementOfFurnitureResponse` | 2 |
| 147 | `PlayerCheckChooseOptionalBoxRequest` | 1 | `PlayerCheckChooseOptionalBoxResponse` | 1 |
| 148 | `PolarizationRequest` | 5 | `PolarizationResponse` | 2 |
| 149 | `ProductionClaimRequest` | 1 | `ProductionClaimResponse` | 2 |
| 150 | `ProductionDeviceUpgradeRequest` | 2 | `ProductionDeviceUpgradeResponse` | 3 |
| 151 | `ProductionFactoryUpgradeRequest` | 0 | `ProductionFactoryUpgradeResponse` | 2 |
| 152 | `ProductionItemChangeRequest` | 2 | `ProductionItemChangeResponse` | 2 |
| 153 | `ProfileRequest` | 1 | `ProfileResponse` | 2 |
| 154 | `PurchaseMarketOrderRequest` | 5 | `PurchaseMarketOrderResponse` | 2 |
| 155 | `PurchaseModRequest` | 1 | `PurchaseModResponse` | 5 |
| 156 | `QuestConditionAchievementPushRequest` | 2 | `QuestConditionAchievementPushResponse` | 2 |
| 157 | `QuestStartRequest` | 2 | `QuestStartResponse` | 2 |
| 158 | `QuestionnaireSubmitRequest` | 2 | `QuestionnaireSubmitResponse` | 2 |
| 159 | `ReactorClaimRequest` | 0 | `ReactorClaimResponse` | 2 |
| 160 | `ReactorMaterialChangeRequest` | 2 | `ReactorMaterialChangeResponse` | 2 |
| 161 | `ReactorStuffRankUpRequest` | 3 | `ReactorStuffRankUpResponse` | 1 |
| 162 | `ReactorUpgradeRequest` | 0 | `ReactorUpgradeResponse` | 3 |
| 163 | `RechargeAwardGetRequest` | 1 | `RechargeAwardGetResponse` | 3 |
| 164 | `RechargeInfoRequest` | 0 | `RechargeInfoResponse` | 1 |
| 165 | `RedPointCheckRequest` | 1 | `RedPointCheckResponse` | 1 |
| 166 | `RedeemCouponRequest` | 1 | `RedeemCouponResponse` | 1 |
| 167 | `RedeemRequest` | 1 | `RedeemResponse` | 1 |
| 168 | `RefundSubmitRequest` | 14 | `RefundSubmitResponse` | 5 |
| 169 | `RemoteCreateSceneRoomRequest` | 2 | `RemoteCreateSceneRoomResponse` | - |
| 170 | `RemoteJoinSceneRoomRequest` | 2 | `RemoteJoinSceneRoomResponse` | - |
| 171 | `RemoteLeaveSceneRoomRequest` | 2 | `RemoteLeaveSceneRoomResponse` | - |
| 172 | `RemoteRemoveRoomRequest` | 1 | `RemoteRemoveRoomResponse` | - |
| 173 | `RemoteSendPushRequest` | 4 | `RemoteSendPushResponse` | - |
| 174 | `RemoteServerControlRequest` | 2 | `RemoteServerControlResponse` | - |
| 175 | `RemoteServerInfoRequest` | 1 | `RemoteServerInfoResponse` | 5 |
| 176 | `RemoteSessionCloseRequest` | 1 | `RemoteSessionCloseResponse` | - |
| 177 | `RemoveFriendRequest` | 2 | `RemoveFriendResponse` | 2 |
| 178 | `ReportMessageRequest` | 2 | `ReportMessageResponse` | 2 |
| 179 | `ReportRequest` | 4 | `ReportResponse` | 2 |
| 180 | `ReportTokenRequest` | 0 | `ReportTokenResponse` | 1 |
| 181 | `ReputationBindRequest` | 1 | `ReputationBindResponse` | 1 |
| 182 | `ReputationRankUpRequest` | 1 | `ReputationRankUpResponse` | 3 |
| 183 | `ReservationRewardRequest` | 0 | `ReservationRewardResponse` | 1 |
| 184 | `RivenModTransformRequest` | 1 | `RivenModTransformResponse` | 2 |
| 185 | `RivenShopRefreshCheckRequest` | 0 | `RivenShopRefreshCheckResponse` | 0 |
| 186 | `RoadSRankAllRewardRequest` | 0 | `RoadSRankAllRewardResponse` | 2 |
| 187 | `RoadSRankBreakProgressRequest` | 1 | `RoadSRankBreakProgressResponse` | 1 |
| 188 | `RoadSRankBreakRequest` | 1 | `RoadSRankBreakResponse` | 2 |
| 189 | `RoadSRankIncentiveRewardRequest` | 0 | `RoadSRankIncentiveRewardResponse` | 2 |
| 190 | `RoadSRankRewardRequest` | 1 | `RoadSRankRewardResponse` | 2 |
| 191 | `RogueMissionRewardRequest` | 2 | `RogueMissionRewardResponse` | 3 |
| 192 | `RogueSquadInfoRequest` | 1 | `RogueSquadInfoResponse` | 3 |
| 193 | `RogueSquadRefreshRequest` | 1 | `RogueSquadRefreshResponse` | 3 |
| 194 | `SceneRoomStatusReportRequest` | 1 | `SceneRoomStatusReportResponse` | - |
| 195 | `SearchGuildInfoRequest` | 6 | `SearchGuildInfoResponse` | 3 |
| 196 | `SearchModMarketAuctionRequest` | 13 | `SearchModMarketAuctionResponse` | 3 |
| 197 | `SearchPetMarketAuctionRequest` | 2 | `SearchPetMarketAuctionResponse` | 3 |
| 198 | `SearchSliceMarketAuctionRequest` | 6 | `SearchSliceMarketAuctionResponse` | 3 |
| 199 | `SendPhoneVerificationCodeRequest` | 1 | `SendPhoneVerificationCodeResponse` | 0 |
| 200 | `SendTeamLoadSceneRequest` | 2 | `SendTeamLoadSceneResponse` | 0 |
| 201 | `ServerTagListRequest` | 0 | `ServerTagListResponse` | 1 |
| 202 | `SetChatExpressionFavoriteRequest` | 2 | `SetChatExpressionFavoriteResponse` | 2 |
| 203 | `SetCurrentTowerSquadRequest` | 1 | `SetCurrentTowerSquadResponse` | 1 |
| 204 | `SetFrameFavoriteRequest` | 2 | `SetFrameFavoriteResponse` | 2 |
| 205 | `SetFriendNotesRequest` | 2 | `SetFriendNotesResponse` | 2 |
| 206 | `SetFriendSettingRequest` | 1 | `SetFriendSettingResponse` | 1 |
| 207 | `SetIntroduceRequest` | 1 | `SetIntroduceResponse` | 1 |
| 208 | `SetNodeHiddenRequest` | 2 | `SetNodeHiddenResponse` | 1 |
| 209 | `SetTowerSquadNameRequest` | 2 | `SetTowerSquadNameResponse` | 2 |
| 210 | `SetTowerSquadRequest` | 2 | `SetTowerSquadResponse` | 2 |
| 211 | `SetUserIconFrameRequest` | 1 | `SetUserIconFrameResponse` | 1 |
| 212 | `SetUserIconRequest` | 1 | `SetUserIconResponse` | 1 |
| 213 | `SetUserThemeRequest` | 1 | `SetUserThemeResponse` | 1 |
| 214 | `SetUserTitleRequest` | 1 | `SetUserTitleResponse` | 1 |
| 215 | `SetWheelItemRequest` | 1 | `SetWheelItemResponse` | 1 |
| 216 | `SettleActivityLevelRequest` | 3 | `SettleActivityLevelResponse` | 3 |
| 217 | `SevenDaysCheckRewardRequest` | 1 | `SevenDaysCheckRewardResponse` | 2 |
| 218 | `ShopBuyMultipleRequest` | 1 | `ShopBuyMultipleResponse` | 4 |
| 219 | `ShopRedPointClearRequest` | 1 | `ShopRedPointClearResponse` | 1 |
| 220 | `SkinChangeAccessoryRequest` | 4 | `SkinChangeAccessoryResponse` | 3 |
| 221 | `SkinChangeColorRequest` | 4 | `SkinChangeColorResponse` | 5 |
| 222 | `SmeltObjectRequest` | 2 | `SmeltObjectResponse` | 2 |
| 223 | `SocialMediaRewardRequest` | 1 | `SocialMediaRewardResponse` | 2 |
| 224 | `SocialMediaShareRequest` | 1 | `SocialMediaShareResponse` | 1 |
| 225 | `StageMissionRewardRequest` | 1 | `StageMissionRewardResponse` | 3 |
| 226 | `StalkerDropRequest` | 1 | `StalkerDropResponse` | 2 |
| 227 | `SteamPaymentVerifyRequest` | 2 | `SteamPaymentVerifyResponse` | 2 |
| 228 | `SwitchModCarrierRequest` | 3 | `SwitchModCarrierResponse` | 3 |
| 229 | `TimedItemRefreshRequest` | 0 | `TimedItemRefreshResponse` | 2 |
| 230 | `TowerSquadInfoRequest` | 0 | `TowerSquadInfoResponse` | 2 |
| 231 | `TrackItemRequest` | 1 | `TrackItemResponse` | 1 |
| 232 | `TrackTaskRequest` | 2 | `TrackTaskResponse` | 1 |
| 233 | `UnBindPhoneNumberRequest` | 2 | `UnBindPhoneNumberResponse` | 1 |
| 234 | `UnblockFriendRequest` | 1 | `UnblockFriendResponse` | 1 |
| 235 | `UnloadTowerRequest` | 2 | `UnloadTowerResponse` | 2 |
| 236 | `UseInviteCodeRequest` | 1 | `UseInviteCodeResponse` | 2 |
| 237 | `V2BattlePassRewardRequest` | 3 | `V2BattlePassRewardResponse` | 3 |
| 238 | `V2BattlePassTaskRewardRequest` | 1 | `V2BattlePassTaskRewardResponse` | 4 |
| 239 | `VivoPaymentVerifyRequest` | 2 | `VivoPaymentVerifyResponse` | 2 |
| 240 | `WeaponRackLoadRequest` | 1 | `WeaponRackLoadResponse` | 1 |
| 241 | `WeaponRackUnloadRequest` | 1 | `WeaponRackUnloadResponse` | 1 |
| 242 | `WeaponSlotActiveRequest` | 2 | `WeaponSlotActiveResponse` | 3 |
| 243 | `WeaponSwitchAttachmentRequest` | 3 | `WeaponSwitchAttachmentResponse` | 2 |
| 244 | `WeeklyQuestRefreshCheckRequest` | 0 | `WeeklyQuestRefreshCheckResponse` | 0 |
| 245 | `WellingInfoRequest` | 0 | `WellingInfoResponse` | 3 |
| 246 | `WellingPointRewardRequest` | 1 | `WellingPointRewardResponse` | 2 |
| 247 | `WildOfferPurchaseRequest` | 0 | `WildOfferPurchaseResponse` | 2 |

</details>
