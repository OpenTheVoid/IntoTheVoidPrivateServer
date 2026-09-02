namespace IntoTheVoidServer.Pomelo;

public enum PacketType
{
    Invalid = 0,
    Handshake = 1,
    HandshakeAck = 2,
    Heartbeat = 3,
    Data = 4,
    Kick = 5
}

public enum MessageType
{
    Request = 0,
    Notify = 1,
    Response = 2,
    Push = 3
}
