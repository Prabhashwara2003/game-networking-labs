using System.Net.Sockets;
using System.Text;

const string Host = "127.0.0.1"; // localhost (your own computer)
const int Port = 7777;

var client = new TcpClient();

// Connect to server
await client.ConnectAsync(Host, Port);
client.NoDelay = true;

Console.WriteLine($"[CLIENT] Connected to {Host}:{Port}");

NetworkStream stream = client.GetStream();

// Start a background task to read server messages
_ = Task.Run(async () =>
{
    while (true)
    {
        string? line = await ReadLineAsync(stream);
        if (line == null) break;
        Console.WriteLine(line);
    }
});

// Main loop: read from keyboard and send to server
while (true)
{
    string? input = Console.ReadLine();
    if (input == null) break;

    await SendLineAsync(stream, input);

    if (input.Trim() == "/quit")
        break;
}

client.Close();

// Same helper methods as server
static async Task<string?> ReadLineAsync(NetworkStream stream)
{
    var sb = new StringBuilder();
    var buffer = new byte[1];

    while (true)
    {
        int read = await stream.ReadAsync(buffer, 0, 1);
        if (read == 0) return null;

        char ch = (char)buffer[0];
        if (ch == '\n') break;
        if (ch != '\r') sb.Append(ch);
    }

    return sb.ToString();
}

static async Task SendLineAsync(NetworkStream stream, string line)
{
    byte[] data = Encoding.UTF8.GetBytes(line + "\n");
    await stream.WriteAsync(data, 0, data.Length);
}

