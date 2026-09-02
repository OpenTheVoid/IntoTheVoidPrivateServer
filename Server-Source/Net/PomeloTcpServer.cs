using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using IntoTheVoidServer.Pomelo;
using IntoTheVoidServer.Router;
using Serilog;

namespace IntoTheVoidServer.Net;

public class PomeloTcpServer
{
    private TcpListener? _listener;
    private readonly int _port;
    private readonly MessageRouter _router;
    private readonly CancellationTokenSource _cts = new();

    // Active connected clients, keyed by remote endpoint string. Used by HTTP GM
    // endpoints to push live updates (e.g. currency grants) to online players.
    private static readonly ConcurrentDictionary<string, NetworkStream> ActiveClients = new();

    public PomeloTcpServer(int port, MessageRouter router)
    {
        _port = port;
        _router = router;
    }

    public async Task StartAsync()
    {
        _listener = new TcpListener(System.Net.IPAddress.Any, _port);
        _listener.Start();
        Log.Information("Pomelo TCP Server started on port {Port}", _port);

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error accepting TCP connection");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint;
        Log.Information("Client connected: {Remote}", remoteEndPoint);

        var stream = client.GetStream();
        bool handshakeDone = false;

        // Register the stream so GM endpoints can push live updates.
        ActiveClients[remoteEndPoint?.ToString() ?? Guid.NewGuid().ToString()] = stream;

        try
        {
            while (!_cts.IsCancellationRequested && client.Connected)
            {
                var packet = await ReadPacketAsync(stream, remoteEndPoint!);
                if (packet == null) break;

                var (packetType, payload) = packet.Value;

                switch (packetType)
                {
                    case PacketType.Handshake:
                        Log.Information("[{Remote}] Handshake received, payload length={Len}", remoteEndPoint, payload?.Length ?? 0);
                        if (payload != null && payload.Length > 0)
                        {
                            var previewLen = Math.Min(500, payload.Length);
                            var hexPreview = BitConverter.ToString(payload, 0, previewLen);
                            Log.Information("[{Remote}] Handshake payload hex (first {PreviewLen}): {Hex}", remoteEndPoint, previewLen, hexPreview);
                            try
                            {
                                var textPreview = System.Text.Encoding.UTF8.GetString(payload, 0, previewLen);
                                Log.Information("[{Remote}] Handshake payload text (first {PreviewLen}): {Text}", remoteEndPoint, previewLen, textPreview);
                            }
                            catch { }
                        }
                        var handshakeResp = PomeloCodec.EncodeHandshake(15);
                        await stream.WriteAsync(handshakeResp);
                        // The client relies on the SERVER actively sending heartbeat packets
                        // (packetType 0x03) to keep the connection alive. Its receive timer is
                        // 2 * heartbeatInterval (30s for heartbeat=15), so start a loop that
                        // pushes heartbeats well within that window.
                        _ = SendHeartbeatsAsync(stream, remoteEndPoint!, _cts.Token);
                        break;

                    case PacketType.HandshakeAck:
                        Log.Information("[{Remote}] HandshakeAck received", remoteEndPoint);
                        handshakeDone = true;
                        break;

                    case PacketType.Heartbeat:
                        var heartbeatResp = PomeloCodec.EncodeHeartbeat();
                        await stream.WriteAsync(heartbeatResp);
                        break;

                    case PacketType.Data:
                        if (!handshakeDone)
                        {
                            Log.Warning("[{Remote}] Data received before handshake", remoteEndPoint);
                            break;
                        }
                        var contract = PomeloCodec.Decode(payload!);
                        if (contract.HasValue)
                        {
                            await HandleDataPacketAsync(stream, contract.Value, remoteEndPoint!);
                        }
                        break;

                    case PacketType.Kick:
                        Log.Information("[{Remote}] Kick received", remoteEndPoint);
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Remote}] Client error", remoteEndPoint);
        }
        finally
        {
            ActiveClients.TryRemove(remoteEndPoint?.ToString() ?? "", out _);
            client.Close();
            Log.Information("[{Remote}] Client disconnected", remoteEndPoint);
        }
    }

    /// <summary>
    /// Push a message to all connected clients. Used by GM endpoints to deliver
    /// live updates (e.g. currency grants) without waiting for the next request.
    /// </summary>
    public static async Task PushToAllClientsAsync(string route, byte[] data)
    {
        foreach (var (key, stream) in ActiveClients.ToArray())
        {
            try
            {
                await SendPushAsync(stream, route, data);
                Log.Information("[GM Push] -> sent {Route} ({Size} bytes) to {Remote}", route, data.Length, key);
            }
            catch (Exception ex)
            {
                Log.Warning("[GM Push] Failed to push to {Remote}: {Msg}", key, ex.Message);
                ActiveClients.TryRemove(key, out _);
            }
        }
    }

    private async Task SendHeartbeatsAsync(NetworkStream stream, object remote, CancellationToken token)
    {
        const int IntervalSeconds = 5;
        var heartbeat = PomeloCodec.EncodeHeartbeat();
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), token);
                await stream.WriteAsync(heartbeat, token);
                Log.Information("[{Remote}] -> sent Heartbeat", remote);
            }
        }
        catch (Exception ex)
        {
            // Client closed the socket or the server is stopping.
            Log.Debug("[{Remote}] Heartbeat sender stopped: {Msg}", remote, ex.Message);
        }
    }

    private async Task HandleDataPacketAsync(NetworkStream stream, Contract contract, object remote)
    {
        var route = contract.compressed
            ? contract.compressRoute.ToString()
            : (contract.route ?? "");

        Log.Information("[{Remote}] Data: route={Route}, type={Type}, id={Id}, dataSize={Size}",
            remote, route, contract.type, contract.id, contract.data?.Length ?? 0);

        try
        {
            byte[]? responsePayload = null;

            if (CapturedData.Responses.TryGetValue(route, out var captured))
            {
                Log.Information("[{Remote}] Using captured response for {Route} ({Size} bytes)",
                    remote, route, captured.Length);
                responsePayload = captured;

                // The official capture's LevelBegin/LevelResource responses hard-code
                // the main-city level ID (43400330). When the client enters a battle
                // level, LevelManager.RecLevelBeginRes checks response.LevelID == levelID
                // and refuses to advance otherwise -> infinite loading. Rewrite the
                // LevelID field to echo the level the client actually requested.
                if ((route == "game.game.LevelBeginRequest" || route == "game.game.LevelResourceRequest")
                    && contract.data != null)
                {
                    var rewritten = ProtoLevelIdRewriter.TryRewriteLevelId(contract.data, responsePayload);
                    if (rewritten != null)
                    {
                        Log.Information("[{Remote}] Rewrote {Route} LevelID to match request ({Old} bytes -> {New} bytes)",
                            remote, route, responsePayload.Length, rewritten.Length);
                        responsePayload = rewritten;
                    }
                }
            }
            else
            {
                Log.Warning("[{Remote}] No captured response for {Route}, falling back to router", remote, route);
                responsePayload = await _router.HandleAsync(route, contract.data, contract.type);
            }

            if (contract.type == MessageType.Request && responsePayload != null)
            {
                var responseContract = new Contract
                {
                    type = MessageType.Response,
                    id = contract.id,
                    route = route,
                    data = responsePayload,
                    compressed = false,
                    error = false
                };
                var (packetData, _) = PomeloCodec.EncodePacket(responseContract, PacketType.Data);
                await stream.WriteAsync(packetData);
            }
            else if (responsePayload != null)
            {
                var pushContract = new Contract
                {
                    type = MessageType.Push,
                    route = route,
                    data = responsePayload,
                    compressed = false,
                    error = false
                };
                var (packetData, _) = PomeloCodec.EncodePacket(pushContract, PacketType.Data);
                await stream.WriteAsync(packetData);
            }

            // After gate.Entry, send initial push messages to complete loading
            if (route == "gate.Entry" && contract.type == MessageType.Request)
            {
                _ = SendInitialPushesAsync(stream, remote);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Remote}] Error handling route {Route}", remote, route);
            var errorContract = new Contract
            {
                type = MessageType.Response,
                id = contract.id,
                route = route,
                data = contract.data ?? Array.Empty<byte>(),
                compressed = false,
                error = true
            };
            var (errData, _) = PomeloCodec.EncodePacket(errorContract, PacketType.Data);
            await stream.WriteAsync(errData);
        }
    }

    private async Task SendInitialPushesAsync(NetworkStream stream, object remote)
    {
        try
        {
            await Task.Delay(200);

            Log.Information("[{Remote}] Sending {Count} captured push messages...", remote, CapturedData.Pushes.Count);

            foreach (var (route, data) in CapturedData.Pushes)
            {
                await SendPushAsync(stream, route, data);
                Log.Information("[{Remote}] -> sent {Route} ({Size} bytes)", remote, route, data.Length);
                await Task.Delay(30);
            }

            Log.Information("[{Remote}] All captured pushes sent", remote);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Remote}] Error sending initial pushes", remote);
        }
    }

    private static async Task SendPushAsync(NetworkStream stream, string route, byte[] data)
    {
        var pushContract = new Contract
        {
            type = MessageType.Push,
            route = route,
            data = data,
            compressed = false,
            error = false
        };
        var (packetData, _) = PomeloCodec.EncodePacket(pushContract, PacketType.Data);
        await stream.WriteAsync(packetData);
    }

    private static async Task<(PacketType, byte[]?)?> ReadPacketAsync(NetworkStream stream, object remote)
    {
        var headerBuffer = new byte[4];
        int bytesRead = 0;
        while (bytesRead < 4)
        {
            int n = await stream.ReadAsync(headerBuffer, bytesRead, 4 - bytesRead);
            if (n == 0)
            {
                if (bytesRead > 0)
                {
                    Log.Warning("[{Remote}] Connection closed after reading {BytesRead} bytes: {Hex}",
                        remote, bytesRead, BitConverter.ToString(headerBuffer, 0, bytesRead));
                }
                return null;
            }
            if (bytesRead == 0 && n > 0)
            {
                Log.Information("[{Remote}] First byte: 0x{Byte:X2} ({Char})",
                    remote, headerBuffer[0],
                    headerBuffer[0] >= 32 && headerBuffer[0] < 127 ? (char)headerBuffer[0] : '?');
            }
            bytesRead += n;
        }

        var packetType = (PacketType)headerBuffer[0];
        // 3-byte big-endian length (24-bit)
        var length = (headerBuffer[1] << 16) | (headerBuffer[2] << 8) | headerBuffer[3];

        Log.Information("[{Remote}] Packet header: type={Type} (0x{TypeByte:X2}), length={Length}",
            remote, packetType, headerBuffer[0], length);

        if (length < 0 || length > 16 * 1024 * 1024)
        {
            Log.Warning("[{Remote}] Invalid packet length: {Length}", remote, length);
            return null;
        }

        var payload = new byte[length];
        bytesRead = 0;
        while (bytesRead < length)
        {
            int n = await stream.ReadAsync(payload, bytesRead, length - bytesRead);
            if (n == 0) return null;
            bytesRead += n;
        }

        return (packetType, payload);
    }

    public void Stop()
    {
        _cts.Cancel();
        _listener?.Stop();
    }
}
