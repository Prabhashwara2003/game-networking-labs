using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

// Server will listen on this port.
// A port is like a "door number" on your computer.
const int Port = 7777;

// Store connected clients.
// ConcurrentDictionary is safe when many tasks/threads access it.
var clients = new ConcurrentDictionary<int, TcpClient>();

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
    clients[id] = client;

    Console.WriteLine($"[SERVER] Client #{id} connected.");

    // Handle this client in the background so server can accept new clients too.
    _ = HandleClientAsync(id, client);
}

// This function handles reading messages from ONE client.
async Task HandleClientAsync(int id, TcpClient client)
{
    try
    {
        NetworkStream stream = client.GetStream();

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
            await BroadcastAsync($"Client#{id}: {line}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SERVER] Error with Client #{id}: {ex.Message}");
    }
    finally
    {
        // Remove client and close connection
        clients.TryRemove(id, out _);
        client.Close();
        Console.WriteLine($"[SERVER] Client #{id} disconnected.");
        await BroadcastAsync($"[SERVER] Client#{id} left.");
    }
}

// Send message to all clients
async Task BroadcastAsync(string message)
{
    Console.WriteLine(message);

    foreach (var kv in clients)
    {
        var c = kv.Value;
        try
        {
            await SendLineAsync(c.GetStream(), message);
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

