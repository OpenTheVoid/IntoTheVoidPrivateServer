using System.IO;
using System.Text;

namespace IntoTheVoidServer.Pomelo;

public struct Contract
{
    public MessageType type;
    public uint id;
    public uint oriId;
    public string route;
    public int compressRoute;
    public bool compressed;
    public byte[] data;
    public bool error;
}

public static class PomeloCodec
{
    public static byte[] Encode(Contract contract)
    {
        using var ms = new MemoryStream();
        byte b = (byte)((int)contract.type << 1);
        if (contract.compressed) b |= 1;
        if (contract.error) b |= 0x20;
        ms.WriteByte(b);

        if (contract.type == MessageType.Request || contract.type == MessageType.Response)
        {
            WriteVarint(ms, contract.id);
        }

        if (contract.type == MessageType.Request || contract.type == MessageType.Notify || contract.type == MessageType.Push)
        {
            if (contract.compressed)
            {
                ms.WriteByte((byte)(contract.compressRoute >> 8));
                ms.WriteByte((byte)contract.compressRoute);
            }
            else
            {
                var routeBytes = Encoding.ASCII.GetBytes(contract.route ?? "");
                ms.WriteByte((byte)routeBytes.Length);
                ms.Write(routeBytes, 0, routeBytes.Length);
            }
        }

        if (contract.data != null && contract.data.Length > 0)
        {
            ms.Write(contract.data, 0, contract.data.Length);
        }

        return ms.ToArray();
    }

    public static Contract? Decode(byte[] data)
    {
        if (data.Length < 1) return null;

        var contract = new Contract();
        int offset = 0;
        byte b = data[offset++];
        contract.type = (MessageType)((b >> 1) & 0x07);
        contract.compressed = (b & 0x01) != 0;
        contract.error = (b & 0x20) != 0;

        if (contract.type == MessageType.Request || contract.type == MessageType.Response)
        {
            (contract.id, offset) = ReadVarint(data, offset);
        }

        if (contract.type == MessageType.Request || contract.type == MessageType.Notify || contract.type == MessageType.Push)
        {
            if (contract.compressed)
            {
                if (offset + 2 > data.Length) return null;
                contract.compressRoute = (data[offset] << 8) | data[offset + 1];
                offset += 2;
            }
            else
            {
                if (offset >= data.Length) return null;
                int routeLen = data[offset++];
                if (offset + routeLen > data.Length) return null;
                contract.route = Encoding.ASCII.GetString(data, offset, routeLen);
                offset += routeLen;
            }
        }

        if (offset < data.Length)
        {
            contract.data = new byte[data.Length - offset];
            Buffer.BlockCopy(data, offset, contract.data, 0, contract.data.Length);
        }

        return contract;
    }

    public static (byte[] data, PacketType packetType) EncodePacket(Contract contract, PacketType packetType)
    {
        var payload = Encode(contract);
        using var ms = new MemoryStream();
        ms.WriteByte((byte)packetType);
        // 3-byte big-endian length (24-bit)
        ms.WriteByte((byte)((payload.Length >> 16) & 0xFF));
        ms.WriteByte((byte)((payload.Length >> 8) & 0xFF));
        ms.WriteByte((byte)(payload.Length & 0xFF));
        ms.Write(payload, 0, payload.Length);
        return (ms.ToArray(), packetType);
    }

    public static (PacketType packetType, byte[]? payload) DecodePacket(byte[] data)
    {
        if (data.Length < 4) return (PacketType.Invalid, null);
        var packetType = (PacketType)data[0];
        // 3-byte big-endian length (24-bit)
        var len = (data[1] << 16) | (data[2] << 8) | data[3];
        if (data.Length < 4 + len) return (PacketType.Invalid, null);
        var payload = new byte[len];
        Buffer.BlockCopy(data, 4, payload, 0, len);
        return (packetType, payload);
    }

    public static byte[] EncodeHandshake(int heartbeatInterval = 15)
    {
        // Standard Pomelo handshake response requires code:200; without it the client
        // rejects the handshake, treats the game server as unreachable ("系统维护中"),
        // and closes+retries the connection.
        var json = $"{{\"code\":200,\"sys\":{{\"heartbeat\":{heartbeatInterval}}}}}";
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream();
        ms.WriteByte((byte)PacketType.Handshake);
        // 3-byte big-endian length (24-bit)
        ms.WriteByte((byte)((jsonBytes.Length >> 16) & 0xFF));
        ms.WriteByte((byte)((jsonBytes.Length >> 8) & 0xFF));
        ms.WriteByte((byte)(jsonBytes.Length & 0xFF));
        ms.Write(jsonBytes, 0, jsonBytes.Length);
        return ms.ToArray();
    }

    public static byte[] EncodeHeartbeat()
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)PacketType.Heartbeat);
        // 3-byte big-endian length (24-bit)
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        return ms.ToArray();
    }

    public static byte[] EncodeHandshakeAck()
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)PacketType.HandshakeAck);
        // 3-byte big-endian length (24-bit)
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        return ms.ToArray();
    }

    private static void WriteVarint(Stream ms, uint value)
    {
        while (value >= 0x80)
        {
            ms.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        ms.WriteByte((byte)value);
    }

    private static (uint value, int newOffset) ReadVarint(byte[] data, int offset)
    {
        uint result = 0;
        int shift = 0;
        while (offset < data.Length)
        {
            byte b = data[offset++];
            result |= (uint)(b & 0x7F) << shift;
            if (b < 0x80) break;
            shift += 7;
        }
        return (result, offset);
    }
}
