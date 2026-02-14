# TCP Multi-Client Chat Server (Phase 1 - Networking Fundamentals)

This project was built to understand low-level TCP socket communication
using C# and .NET.

## What This Project Demonstrates

- TCP client-server architecture
- Accepting multiple clients
- Asynchronous handling using async/await
- Broadcasting messages to connected clients
- Detecting client disconnects (`read == 0`)
- Basic message framing using newline (`\n`)

## Technologies Used

- C#
- .NET 8
- TCP Sockets

## How to Run

### Start Server
cd ChatServer
dotnet run

### Start Client
cd ChatClient
dotnet run

Run multiple clients to test broadcasting.

## Learning Goal

This is Phase 1 of a Game Backend Developer roadmap.
Future upgrades will include:
- Custom message protocol
- Lobby/room system
- Heartbeat system
- UDP position sync
