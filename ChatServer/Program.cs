using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Linq;
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

const int Port = 7777;

var clients = new ConcurrentDictionary<int, ClientSession>();
int nextId = 0;

var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();
Console.WriteLine($"[SERVER] Listening on port {Port}...");

_= HeartBeatCheck();

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    client.NoDelay = true;

    int id = Interlocked.Increment(ref nextId);
    var session = new ClientSession(id, client);
    clients[id] = session;

    Console.WriteLine($"[SERVER] Client #{id} connected.");

    _ = HandleClientAsync(session);
}

 async Task HeartBeatCheck(){

    var PingInterval = TimeSpan.FromSeconds(10);
    var Timeout = TimeSpan.FromSeconds(30);

    while(true){
        
        await Task.Delay(PingInterval);

         var packet = new Packet(
            Type: "ping",
            From: "SERVER",
            Text: $"Server time: {DateTime.UtcNow:HH:mm:ss}",
            Name: null,
            Args: null
        );

        await BroadcastPacketAsync(packet);

        var now = DateTime.UtcNow;

         foreach (var kv in clients)
        {
            var s = kv.Value;
            if (now - s.LastSeenUtc > Timeout)
            {
                Console.WriteLine($"[SERVER] Timing out {s.Username} (last seen {s.LastSeenUtc:HH:mm:ss})");

                // Close socket; HandleClientAsync will clean up in finally
                try { s.Client.Close(); } catch { }
            }
        }
    }
 }

async Task HandleClientAsync(ClientSession session)
{
    try
    {
        NetworkStream stream = session.Client.GetStream();

        // Welcome as a SYSTEM packet
        await Wire.SendPacketAsync(stream, new Packet(
            Type: "system", From: "SERVER",
            Text: "Welcome! Type messages. Use /quit to leave.",
            Name: null, Args: null
        ));

        while (true)
        {
            var packet = await Wire.TryReadPacketAsync(stream);
            if (packet == null) break; // disconnect OR invalid framing
            session.LastSeenUtc = DateTime.UtcNow;

            switch (packet.Type)
            {
                case "chat":
                    if (string.IsNullOrWhiteSpace(packet.Text)) break;

                    await BroadcastPacketAsync(new Packet(
                        Type: "chat",
                        From: session.Username,              // server-authoritative
                        Text: packet.Text.Trim(),
                        Name: null, Args: null
                    ));
                    break;

                case "command":
                    await HandleCommandPacketAsync(session, packet);
                    break;

                case "pong":
                    session.LastSeenUtc = DateTime.UtcNow;
                     break;


                default:
                    await Wire.SendPacketAsync(stream, new Packet(
                        Type: "system", From: "SERVER",
                        Text: $"Unknown packet type: {packet.Type}",
                        Name: null, Args: null
                    ));
                    break;
            }
        }
    }
    catch (InvalidOperationException ex)
    {
        // e.g. invalid message length -> likely malicious or protocol mismatch
        Console.WriteLine($"[SERVER] Protocol error for {session.Username}: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SERVER] Error with Client #{session.Id}: {ex.Message}");
    }
    finally
    {
        clients.TryRemove(session.Id, out _);
        try { session.Client.Close(); } catch { }

        Console.WriteLine($"[SERVER] {session.Username} disconnected.");

        await BroadcastPacketAsync(new Packet(
            Type: "system", From: "SERVER",
            Text: $"{session.Username} left.",
            Name: null, Args: null
        ));
    }
}

async Task BroadcastPacketAsync(Packet packet)
{
    string json = JsonUtil.ToJson(packet);
    Console.WriteLine($"[BROADCAST] {json}");

    foreach (var kv in clients)
    {
        var s = kv.Value;
        try
        {
            await Wire.SendMessageAsync(s.Client.GetStream(), json);
        }
        catch { }
    }
}

async Task HandleCommandPacketAsync(ClientSession session, Packet packet)
{
    var stream = session.Client.GetStream();
    var name = (packet.Name ?? "").Trim().ToLowerInvariant();
    var args = (packet.Args ?? "").Trim();

    switch (name)
    {
        case "name":
            if (string.IsNullOrWhiteSpace(args))
            {
                await Wire.SendPacketAsync(stream, new Packet(
                    Type: "system", From: "SERVER",
                    Text: "Usage: /name <newname>",
                    Name: null, Args: null
                ));
                return;
            }

            var old = session.Username;
            session.Username = args;

            await BroadcastPacketAsync(new Packet(
                Type: "system", From: "SERVER",
                Text: $"{old} changed name to {session.Username}",
                Name: null, Args: null
            ));
            break;

        case "who":
            var list = string.Join(", ", clients.Values.Select(c => c.Username));
            await Wire.SendPacketAsync(stream, new Packet(
                Type: "system", From: "SERVER",
                Text: $"Online: {list}",
                Name: null, Args: null
            ));
            break;

        case "help":
            await Wire.SendPacketAsync(stream, new Packet(
                Type: "system", From: "SERVER",
                Text: "Commands: /name <newname>, /who, /help, /quit",
                Name: null, Args: null
            ));
            break;

        case "quit":
            try { session.Client.Close(); } catch { }
            break;

        default:
            await Wire.SendPacketAsync(stream, new Packet(
                Type: "system", From: "SERVER",
                Text: $"Unknown command: /{name}",
                Name: null, Args: null
            ));
            break;
    }
}

// -------------------- Utilities (BOTTOM) --------------------

static class JsonUtil
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Packet? TryParse(string json)
    {
        try { return JsonSerializer.Deserialize<Packet>(json, Opts); }
        catch { return null; }
    }

    public static string ToJson(Packet p) => JsonSerializer.Serialize(p, Opts);
}

static class Wire
{
    private const int MaxMessageSize = 64 * 1024;

    public static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;

        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer, offset, length - offset);
            if (read == 0) return null;
            offset += read;
        }

        return buffer;
    }

    public static async Task<string?> ReadMessageAsync(NetworkStream stream)
    {
        var lenBytes = await ReadExactAsync(stream, 4);
        if (lenBytes == null) return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(lenBytes);

        if (length < 0 || length > MaxMessageSize)
            throw new InvalidOperationException($"Invalid message length: {length}");

        var msgBytes = await ReadExactAsync(stream, length);
        if (msgBytes == null) return null;

        return Encoding.UTF8.GetString(msgBytes);
    }

    public static async Task SendMessageAsync(NetworkStream stream, string message)
    {
        byte[] msgBytes = Encoding.UTF8.GetBytes(message);
        if (msgBytes.Length > MaxMessageSize)
            throw new InvalidOperationException($"Message too large: {msgBytes.Length} bytes");

        byte[] lenBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBytes, msgBytes.Length);

        await stream.WriteAsync(lenBytes, 0, lenBytes.Length);
        await stream.WriteAsync(msgBytes, 0, msgBytes.Length);
    }

    public static async Task<Packet?> TryReadPacketAsync(NetworkStream stream)
    {
        var raw = await ReadMessageAsync(stream);
        if (raw == null) return null;

        // If JSON invalid, treat as "invalid packet" and keep connection alive OR close
        // Here: we return a SYSTEM error packet? We'll just return null? No—better:
        var pkt = JsonUtil.TryParse(raw);
        return pkt; // could be null; caller can decide
    }

    public static Task SendPacketAsync(NetworkStream stream, Packet packet)
        => SendMessageAsync(stream, JsonUtil.ToJson(packet));
}

class ClientSession
{
    public DateTime LastSeenUtc { get; set; }
    public int Id { get; }
    public TcpClient Client { get; }
    public string Username { get; set; }
    public DateTime ConnectedAt { get; }

    public ClientSession(int id, TcpClient client)
    {
        Id = id;
        Client = client;
        Username = $"Guest{id}";
        ConnectedAt = DateTime.UtcNow;
        LastSeenUtc = DateTime.UtcNow;
    }
}

record Packet(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("args")] string? Args
);
