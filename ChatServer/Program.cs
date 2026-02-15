using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Linq;

// Server will listen on this port.
// A port is like a "door number" on your computer.
const int Port = 7777;

// Store connected clients.
// ConcurrentDictionary is safe when many tasks/threads access it.
var clients = new ConcurrentDictionary<int, ClientSession>();

// Create a TCP listener that listens on any network interface (your laptop's IPs).
var listener = new TcpListener(IPAddress.Any, Port);

// Start listening
listener.Start();
Console.WriteLine($"[SERVER] Listening on port {Port}...");

// Accept clients forever
while (true)
{
    // Wait until a new client connects
    TcpClient client = await listener.AcceptTcpClientAsync();

    // NoDelay reduces latency (disables Nagle algorithm).
    // Games often care about low-latency messages.
    client.NoDelay = true;

    // Give this client an ID
    int id = clients.Count + 1;

    // Save the client
    var session = new ClientSession(id, client);
    clients[id] = session;


    Console.WriteLine($"[SERVER] Client #{id} connected.");

    // Handle this client in the background so server can accept new clients too.
    _ = HandleClientAsync(session);
}

// This function handles reading messages from ONE client.
async Task HandleClientAsync(ClientSession session)
{
    try
    {
        NetworkStream stream = session.Client.GetStream();

        // Inform the client they connected
        await SendLineAsync(stream, "Welcome! Type messages. Use /quit to leave.");

        // Read messages forever
        while (true)
        {
            // Read one line from client
            string? line = await ReadLineAsync(stream);

            // If null, client disconnected (TCP connection ended)
            if (line == null) break;

            line = line.Trim();

            if (line == "/quit") break;

            // Broadcast the message to everyone
            if (line.StartsWith("/"))
            {
                await HandleCommand(session, line);
            }
            else
            {
                await BroadcastAsync($"{session.Username}: {line}");
            }

        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SERVER] Error with Client #{session.Id}: {ex.Message}");
    }
    finally
    {
        // Remove client and close connection
        clients.TryRemove(session.Id, out _);
        session.Client.Close();
        Console.WriteLine($"[SERVER] Client #{session.Id} disconnected.");
        await BroadcastAsync($"[SERVER] Client#{session.Id} left.");
    }
}

// Send message to all clients
async Task BroadcastAsync(string message)
{
    Console.WriteLine(message);

    foreach (var kv in clients)
    {
        var session = kv.Value;
        try
        {
            await SendLineAsync(session.Client.GetStream(), message);
        }
        catch
        {
            // If sending fails, ignore. The read loop will detect disconnect later.
        }
    }
}

// Reads text until "\n". This is our simple "message boundary".
static async Task<string?> ReadLineAsync(NetworkStream stream)
{
    var sb = new StringBuilder();
    var buffer = new byte[1];

    while (true)
    {
        int read = await stream.ReadAsync(buffer, 0, 1);

        // read == 0 means the connection was closed
        if (read == 0) return null;

        char ch = (char)buffer[0];

        if (ch == '\n') break;       // end of message
        if (ch != '\r') sb.Append(ch); // ignore carriage return
    }

    return sb.ToString();
}

// Sends text + "\n" so the receiver knows where message ends.
static async Task SendLineAsync(NetworkStream stream, string line)
{
    byte[] data = Encoding.UTF8.GetBytes(line + "\n");
    await stream.WriteAsync(data, 0, data.Length);
}

async Task HandleCommand(ClientSession session, string command)
{
    var parts = command.Split(' ', 2);
    var cmd = parts[0].ToLower();

    switch (cmd)
    {
        case "/name":
            if (parts.Length < 2)
            {
                await SendLineAsync(session.Client.GetStream(), "Usage: /name yourName");
                return;
            }

            var oldName = session.Username;
            session.Username = parts[1].Trim();
            await BroadcastAsync($"{oldName} changed name to {session.Username}");
            break;

        case "/who":
            var userList = string.Join(", ", clients.Values.Select(c => c.Username));
            await SendLineAsync(session.Client.GetStream(), $"Online: {userList}");
            break;

        case "/help":
            await SendLineAsync(session.Client.GetStream(),
                "Commands: /name <newname>, /who, /help, /quit");
            break;

        default:
            await SendLineAsync(session.Client.GetStream(), "Unknown command.");
            break;
    }
}
class ClientSession
{
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
    }
}


