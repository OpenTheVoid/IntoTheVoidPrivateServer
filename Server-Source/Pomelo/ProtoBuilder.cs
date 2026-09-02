using System.Text;

namespace IntoTheVoidServer.Pomelo;

/// <summary>
/// Manual protobuf wire-format encoder.
/// Tag = (field_number << 3) | wire_type
/// Wire types: 0=varint, 1=64-bit, 2=length-delimited, 5=32-bit
/// </summary>
public static class ProtoBuilder
{
    public static byte[] Build()
    {
        // Empty message
        return Array.Empty<byte>();
    }

    public static ProtoWriter Write() => new();

    /// <summary>
    /// Build EntryResponse: field 1 = Unix (int64)
    /// </summary>
    public static byte[] BuildEntryResponse(long unixTimestamp)
    {
        return Write()
            .WriteInt64(1, unixTimestamp)
            .ToBytes();
    }

    /// <summary>
    /// Build LoginDeviceInfoResponse: field 1 = DeviceInfo (map<int32,int32>)
    /// Empty map = success
    /// </summary>
    public static byte[] BuildLoginDeviceInfoResponse()
    {
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Build PlayerDataResponse: field 1 = UpdateInfo
    /// UpdateInfo: field 2 = PlayerData, field 4 = CurrencyData
    /// Currency comes from GameState (so GM additions persist across re-login).
    /// </summary>
    public static byte[] BuildPlayerDataResponse()
    {
        var playerData = BuildPlayerData(
            level: 30,
            exp: 0,
            nickName: "OfflinePlayer",
            weaponCapacity: 10,
            frameCatCapacity: 10,
            modCatCapacity: 30,
            gearCatCapacity: 10,
            rivenModCapacity: 10,
            squadSlot: 3,
            backID: 10001,
            backLength: 100000000
        );

        var updateInfo = Write()
            .WriteMessage(2, playerData)
            .WriteMessage(4, BuildAllCurrenciesUpdate())
            .ToBytes();

        return Write()
            .WriteMessage(1, updateInfo)
            .ToBytes();
    }

    /// <summary>
    /// Build UpdateInfo whose field 4 carries the full GameState currency snapshot
    /// (multiple CurrencyEntity entries as a repeated field is NOT valid for field 4,
    /// so we emit one message per currency by concatenating them inside a single
    /// UpdateInfo field 4 — client's UpdateInfo.CurrencyData is a repeated field).
    /// </summary>
    public static byte[] BuildAllCurrenciesUpdate()
    {
        var writer = Write();
        foreach (var (type, count) in IntoTheVoidServer.GameState.Snapshot())
        {
            writer.WriteMessage(4, BuildCurrencyEntity(type, count));
        }
        return writer.ToBytes();
    }

    /// <summary>
    /// Build PlayerData message (inner message in UpdateInfo field 2)
    /// </summary>
    public static byte[] BuildPlayerData(
        int level, int exp, string nickName,
        int weaponCapacity, int frameCatCapacity, int modCatCapacity,
        int gearCatCapacity, int rivenModCapacity,
        int squadSlot, long backID, long backLength)
    {
        return Write()
            .WriteInt32(1, level)          // Level
            .WriteInt32(2, exp)           // Exp
            .WriteString(3, nickName)     // NickName
            .WriteInt32(6, frameCatCapacity)  // FrameCatCapacity
            .WriteInt32(7, 5)               // MechaCatCapacity
            .WriteInt32(8, 5)               // ServantCatCapacity
            .WriteInt32(9, 5)               // TowerCatCapacity
            .WriteInt32(10, rivenModCapacity) // RivenModCapacity
            .WriteInt32(13, 5)              // PetCatCapacity
            .WriteInt32(15, weaponCapacity) // WeaponCapacity
            .WriteInt32(21, modCatCapacity) // ModCatCapacity
            .WriteInt32(22, gearCatCapacity)// GearCatCapacity
            .WriteInt32(29, 100)            // FurnitureSlotMax
            .WriteInt64(32, backLength)     // BackLength
            .WriteInt32(33, squadSlot)      // SquadSlot
            .ToBytes();
    }

    /// <summary>
    /// Build CurrencyEntity: field 1 = CurrencyType (int32), field 2 = Count (int32)
    /// </summary>
    public static byte[] BuildCurrencyEntity(int currencyType, int count)
    {
        return Write()
            .WriteInt32(1, currencyType)
            .WriteInt32(2, count)
            .ToBytes();
    }

    /// <summary>
    /// Build BackPackListResponse: field 1 = Items (ItemList)
    /// ItemList: field 8 = Items (repeated BackPackItem)
    /// BackPackItem: field 1=SeqID(int64), field 2=ItemID(int32), field 3=Amount(int32), field 4=GainTimeStamp(int64)
    /// </summary>
    public static byte[] BuildBackPackListResponse()
    {
        // Empty item list - client will start with no items
        // Items can be added via GM commands later
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Build CharacterSquadInfoResponse: field 1 = CurrentSquadID (int32), field 2 = SquadInfo (repeated)
    /// SquadInfo contains SquadID, SquadName, Members (repeated), CampType
    /// </summary>
    public static byte[] BuildCharacterSquadInfoResponse()
    {
        var member = Write()
            .WriteInt32(1, 1)           // CharacterID
            .WriteInt32(2, 1001)        // WeaponID
            .WriteInt32(3, 2001)        // FrameID
            .WriteInt32(4, 0)           // Position
            .ToBytes();

        var squad = Write()
            .WriteInt32(1, 1)           // SquadID
            .WriteString(2, "Squad 1")  // SquadName
            .WriteMessage(3, member)    // Members
            .WriteInt32(4, 0)           // CampType
            .ToBytes();

        return Write()
            .WriteInt32(1, 1)           // CurrentSquadID
            .WriteMessage(2, squad)     // SquadInfo
            .ToBytes();
    }

    /// <summary>
    /// Build a simple success response (code=0)
    /// </summary>
    public static byte[] BuildSuccess()
    {
        return Array.Empty<byte>();
    }

    // ===== Push message builders =====

    /// <summary>
    /// Build PlayerDataUpdatePush - notifies client of player data changes
    /// </summary>
    public static byte[] BuildPlayerDataUpdatePush()
    {
        // Same structure as PlayerDataResponse
        var playerData = BuildPlayerData(
            level: 30,
            exp: 0,
            nickName: "OfflinePlayer",
            weaponCapacity: 10,
            frameCatCapacity: 10,
            modCatCapacity: 30,
            gearCatCapacity: 10,
            rivenModCapacity: 10,
            squadSlot: 3,
            backID: 10001,
            backLength: 100000000
        );

        var updateInfo = Write()
            .WriteMessage(2, playerData)
            .ToBytes();

        return Write()
            .WriteMessage(1, updateInfo)
            .ToBytes();
    }

    /// <summary>
    /// Build SquadChangePush - notifies client of squad changes
    /// Contains CurrentSquadID and list of SquadEntity (Members)
    /// </summary>
    public static byte[] BuildSquadChangePush()
    {
        // Build a squad member
        var member = Write()
            .WriteInt32(1, 1)           // CharacterID
            .WriteInt32(2, 1001)        // WeaponID
            .WriteInt32(3, 2001)        // FrameID
            .WriteInt32(4, 0)           // Position index
            .ToBytes();

        // Build squad entity with members
        var squad = Write()
            .WriteInt32(1, 1)           // SquadID
            .WriteString(2, "Squad 1")  // SquadName
            .WriteMessage(3, member)    // Members (repeated)
            .WriteInt32(4, 0)           // CampType
            .ToBytes();

        return Write()
            .WriteInt32(1, 1)           // CurrentSquadID
            .WriteMessage(2, squad)     // SquadInfo
            .ToBytes();
    }

    /// <summary>
    /// Build RedPointPush - notification red dots
    /// </summary>
    public static byte[] BuildRedPointPush()
    {
        // Empty - no new notifications
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Build CurrencyDataPush - currency update.
    /// CurrencyPushResponse: field 1 = repeated CurrencyEntity(1=type, 2=count)
    /// </summary>
    public static byte[] BuildCurrencyDataPush()
    {
        var writer = Write();
        foreach (var (type, count) in IntoTheVoidServer.GameState.Snapshot())
        {
            writer.WriteMessage(1, BuildCurrencyEntity(type, count));
        }
        return writer.ToBytes();
    }

    /// <summary>
    /// Build a CurrencyPush with only the specified currencies (for GM single-currency grants).
    /// </summary>
    public static byte[] BuildCurrencyPush(params (int Type, int Count)[] currencies)
    {
        var writer = Write();
        foreach (var (type, count) in currencies)
        {
            writer.WriteMessage(1, BuildCurrencyEntity(type, count));
        }
        return writer.ToBytes();
    }

    /// <summary>
    /// Build SceneRoomStatusPush - tells client the current scene room status
    /// </summary>
    public static byte[] BuildSceneRoomStatusPush()
    {
        // Scene room info: roomId=1, sceneId=1 (town), players=1
        var roomInfo = Write()
            .WriteInt32(1, 1)        // RoomID
            .WriteInt32(2, 1)        // SceneID (town)
            .WriteInt32(3, 1)        // PlayerCount
            .WriteString(4, "Town")  // RoomName
            .ToBytes();

        return Write()
            .WriteMessage(1, roomInfo)
            .ToBytes();
    }

    /// <summary>
    /// Build TownAllInteractivePush - all town interactive objects
    /// </summary>
    public static byte[] BuildTownAllInteractivePush()
    {
        // Empty list - no interactive objects needed for basic offline play
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Build EnterSceneRoomResponse - response to entering a scene room
    /// </summary>
    public static byte[] BuildEnterSceneRoomResponse()
    {
        var roomInfo = Write()
            .WriteInt32(1, 1)        // RoomID
            .WriteInt32(2, 1)        // SceneID
            .WriteInt32(3, 1)        // PlayerCount
            .WriteString(4, "Town")
            .ToBytes();

        return Write()
            .WriteMessage(1, roomInfo)
            .ToBytes();
    }

    /// <summary>
    /// Build TimestampSyncPush - server timestamp sync
    /// </summary>
    public static byte[] BuildTimestampSyncPush()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Write()
            .WriteInt64(1, timestamp)
            .ToBytes();
    }

    /// <summary>
    /// Build empty push response
    /// </summary>
    public static byte[] BuildEmptyPush()
    {
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Build SceneInteractiveStatusAllPush - all interactive objects status in scene
    /// </summary>
    public static byte[] BuildSceneInteractiveStatusAllPush()
    {
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Build SceneRoomPlayerStatusPush - player status in scene room
    /// </summary>
    public static byte[] BuildSceneRoomPlayerStatusPush()
    {
        var playerStatus = Write()
            .WriteString(1, "player1")
            .WriteInt32(2, 1)
            .WriteInt32(3, 0)
            .WriteInt32(4, 0)
            .WriteInt32(5, 0)
            .ToBytes();

        return Write()
            .WriteMessage(1, playerStatus)
            .ToBytes();
    }

    /// <summary>
    /// Build TeamInfoPush - team information
    /// </summary>
    public static byte[] BuildTeamInfoPush()
    {
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Build FeatureChangePush - feature unlock status change
    /// </summary>
    public static byte[] BuildFeatureChangePush()
    {
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Build TeamLoadScenePush - signals team members are ready to load scene
    /// </summary>
    public static byte[] BuildTeamLoadScenePush()
    {
        // Scene ID 1 = town
        return Write()
            .WriteInt32(1, 1)           // SceneID
            .WriteInt32(2, 1)           // RoomID
            .WriteInt32(3, 0)           // LoadStatus (0 = ready)
            .ToBytes();
    }

    /// <summary>
    /// Build SceneCreateGameRoomPush - notifies client that a game room has been created
    /// </summary>
    public static byte[] BuildSceneCreateGameRoomPush()
    {
        var player = Write()
            .WriteString(1, "player1")  // PlayerID
            .WriteInt32(2, 1)           // CharacterID
            .WriteInt32(3, 0)           // PosX
            .WriteInt32(4, 0)           // PosY
            .WriteInt32(5, 0)           // PosZ
            .ToBytes();

        return Write()
            .WriteInt32(1, 1)           // RoomID
            .WriteInt32(2, 1)           // SceneID
            .WriteInt32(3, 1)           // RoomType (1 = town)
            .WriteMessage(4, player)    // Player list
            .WriteInt32(5, 1)           // MaxPlayers
            .ToBytes();
    }
}

/// <summary>
/// Fluent protobuf writer for constructing messages field by field
/// </summary>
public class ProtoWriter
{
    private readonly MemoryStream _ms = new();

    public ProtoWriter WriteVarint(int fieldNumber, long value)
    {
        WriteTag(fieldNumber, 0); // wire type 0 = varint
        WriteVarint((ulong)value);
        return this;
    }

    public ProtoWriter WriteInt32(int fieldNumber, int value)
    {
        WriteTag(fieldNumber, 0);
        WriteVarint((uint)value);
        return this;
    }

    public ProtoWriter WriteInt64(int fieldNumber, long value)
    {
        WriteTag(fieldNumber, 0);
        WriteVarint((ulong)value);
        return this;
    }

    public ProtoWriter WriteBool(int fieldNumber, bool value)
    {
        WriteTag(fieldNumber, 0);
        WriteVarint(value ? 1UL : 0UL);
        return this;
    }

    public ProtoWriter WriteString(int fieldNumber, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteTag(fieldNumber, 2); // wire type 2 = length-delimited
        WriteVarint((uint)bytes.Length);
        _ms.Write(bytes, 0, bytes.Length);
        return this;
    }

    public ProtoWriter WriteMessage(int fieldNumber, byte[] messageBytes)
    {
        WriteTag(fieldNumber, 2);
        WriteVarint((uint)messageBytes.Length);
        _ms.Write(messageBytes, 0, messageBytes.Length);
        return this;
    }

    public byte[] ToBytes()
    {
        return _ms.ToArray();
    }

    private void WriteTag(int fieldNumber, int wireType)
    {
        WriteVarint((uint)((fieldNumber << 3) | wireType));
    }

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _ms.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        _ms.WriteByte((byte)value);
    }
}
