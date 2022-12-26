# C#-Client
![CodeGame Version](https://img.shields.io/badge/CodeGame-v0.8-orange)
![Target](https://img.shields.io/badge/Framework-.Net%206-blue)

The C# client library for [CodeGame](https://code-game.org).

## Installation

```
dotnet add package CodeGame.Client
```

## Usage

```csharp
// Create a new game socket.
using var socket = await GameSocket.Create("games.code-game.org/example");

// Create a new private game.
var gameId = await socket.CreateGame(false);

// Join a game.
await socket.Join(gameId, "username");

// Spectate a game.
await socket.Spectate(gameId);

// Connect with an existing session.
await socket.RestoreSession("username");

// Register an event listener for the `my_event` event.
socket.On<MyEvent>("my_event", (data) =>
{
    // TODO: do something with `data`
});

// Send a `hello_world` command.
socket.Send("hello_world", new HelloWorldCmd
{
    Message = "Hello, World!"
})

// Wait until the connection is closed.
socket.Wait();
```

## License

MIT License

Copyright (c) 2022 Julian Hofmann

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
