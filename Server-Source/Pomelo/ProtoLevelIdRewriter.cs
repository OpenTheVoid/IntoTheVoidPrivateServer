namespace IntoTheVoidServer.Pomelo;

/// <summary>
/// Rewrites the LevelID field (protobuf field 1, varint) inside captured
/// LevelBegin/LevelResource responses so it echoes the level the client
/// actually requested. The official pcap hard-codes the main-city level
/// (43400330); battle levels would fail the client's
/// LevelManager.RecLevelBeginRes LevelID equality check and stall loading.
/// </summary>
public static class ProtoLevelIdRewriter
{
    /// <summary>
    /// Parse a varint from <paramref name="data"/> starting at <paramref name="offset"/>.
    /// Returns the decoded value and the number of bytes consumed.
    /// </summary>
    public static (long value, int consumed)? ReadVarint(byte[] data, int offset)
    {
        long result = 0;
        int shift = 0;
        int i = offset;
        while (i < data.Length && shift < 64)
        {
            byte b = data[i];
            result |= (long)(b & 0x7F) << shift;
            i++;
            if ((b & 0x80) == 0)
                return (result, i - offset);
            shift += 7;
        }
        return null;
    }

    /// <summary>
    /// Encode an int as a protobuf varint (max 5 bytes for int32-range values).
    /// </summary>
    public static byte[] EncodeVarint(int value)
    {
        var bytes = new List<byte>(5);
        uint v = (uint)value;
        while (v >= 0x80)
        {
            bytes.Add((byte)((v & 0x7F) | 0x80));
            v >>= 7;
        }
        bytes.Add((byte)v);
        return bytes.ToArray();
    }

    /// <summary>
    /// Given the client's request payload (which has field 1 = LevelID varint) and
    /// the captured response payload, rewrite the response's field 1 varint to the
    /// requested level ID. Returns null if the payloads don't have the expected shape.
    /// </summary>
    public static byte[]? TryRewriteLevelId(byte[] requestPayload, byte[] responsePayload)
    {
        // Request: tag 0x08 (field 1, wire type 0 = varint) then the level id varint.
        if (requestPayload.Length < 2 || requestPayload[0] != 0x08)
            return null;

        var requestLevelId = ReadVarint(requestPayload, 1);
        if (requestLevelId == null || requestLevelId.Value.value > int.MaxValue)
            return null;

        // Response: field 1 must also start with 0x08.
        if (responsePayload.Length < 2 || responsePayload[0] != 0x08)
            return null;

        var responseLevelId = ReadVarint(responsePayload, 1);
        if (responseLevelId == null)
            return null;

        int newLevelId = (int)requestLevelId.Value.value;
        var newVarint = EncodeVarint(newLevelId);

        // Rebuild: tag(1) + new varint + everything after the old varint.
        int oldVarintLen = responseLevelId.Value.consumed;
        var result = new byte[1 + newVarint.Length + (responsePayload.Length - 1 - oldVarintLen)];
        result[0] = 0x08;
        Buffer.BlockCopy(newVarint, 0, result, 1, newVarint.Length);
        Buffer.BlockCopy(responsePayload, 1 + oldVarintLen, result, 1 + newVarint.Length,
            responsePayload.Length - 1 - oldVarintLen);
        return result;
    }
}
