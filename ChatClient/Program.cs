using System.Net.Sockets;
using System.Text;
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

const string Host = "127.0.0.1";
const int Port = 7777;

using var client = new TcpClient();
await client.ConnectAsync(Host, Port);
client.NoDelay = true;

Console.WriteLine($"[CLIENT] Connected to {Host}:{Port}");
Console.WriteLine("----------------------------------------");
Console.WriteLine("Welcome to the Multiplayer Chat Server");
Console.WriteLine();
Console.WriteLine("Available commands:");
Console.WriteLine();
Console.WriteLine("General:");
Console.WriteLine("  /name <newname>     - Change your username");
Console.WriteLine("  /who                - List online users");
Console.WriteLine("  /help               - Show help");
Console.WriteLine("  /quit               - Disconnect");
Console.WriteLine();
Console.WriteLine("Rooms:");
Console.WriteLine("  /create <room>      - Create and join a room");
Console.WriteLine("  /join <room>        - Join existing room");
Console.WriteLine("  /leave              - Leave current room");
Console.WriteLine("  /rooms              - List available rooms");
Console.WriteLine("  /where              - Show current room");
Console.WriteLine("----------------------------------------");
Console.WriteLine();

NetworkStream stream = client.GetStream();

// Reader task: receive packets and print nicely
var reader = Task.Run(async () =>
{
    try
    {
        while (true)
        {
            var packet = await Wire.TryReadPacketAsync(stream);
            if (packet == null) break;

            if (packet.Type == "ping")
            {
                await Wire.SendPacketAsync(stream, new Packet("pong", null, null, null, null));
                continue;
            }
            if (packet.Type == "chat")
                Console.WriteLine($"{packet.From}: {packet.Text}");
            else if (packet.Type == "system")
                Console.WriteLine($"[{packet.From}] {packet.Text}");
            else
                Console.WriteLine($"[{packet.Type}] {JsonUtil.ToJson(packet)}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CLIENT] Reader error: {ex.Message}");
    }
});

// Writer loop: read from keyboard and send packets
while (true)
{
    var input = Console.ReadLine();
    if (input == null) break;

    input = input.Trim();
    if (input.Length == 0) continue;

    if (input.StartsWith("/"))
    {
        // "/name Kasun" => name="name", args="Kasun"
        var parts = input.Substring(1).Split(' ', 2);
        var cmdName = parts[0];
        var cmdArgs = parts.Length > 1 ? parts[1] : "";

        var cmdPacket = new Packet("command", null, null, cmdName, cmdArgs);
        await Wire.SendPacketAsync(stream, cmdPacket);

        if (cmdName.Equals("quit", StringComparison.OrdinalIgnoreCase))
            break;
    }
    else
    {
        var chatPacket = new Packet("chat", null, input, null, null);
        await Wire.SendPacketAsync(stream, chatPacket);
    }
}

try { client.Close(); } catch { }
try { await reader; } catch { }

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
            if (read == 0) return null; // disconnected
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

        return JsonUtil.TryParse(raw);
    }

    public static Task SendPacketAsync(NetworkStream stream, Packet packet)
        => SendMessageAsync(stream, JsonUtil.ToJson(packet));
}

record Packet(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("args")] string? Args
);
