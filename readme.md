# Web socket endpoint for Event Hubs
This project provides a way to connect to send messages to an Azure Event Hub using the 
HTML 5 WebSocket API.

## Introduction
This project depends on .NET Core, which can be found [here](https://www.microsoft.com/net/core).

The CoreServer project acts as a proxy for Azure Event Hubs. The 
client (HTML5 WebSocket) connects to the CoreServer, sends
authentication information, then the server sends the messages
received from the client to the event hub.

## Getting Started
The project can be build by navigating to `\CoreServer\` and running
`dotnet build` or the project can be built in Visual Studio.

### Commands
   * Authenticate - 'A' - `A<ConnectionString>` - `AEndpoint=...`
   * Send - 'S' - `S<Message>` - `SHello, world!`

## Todo
 * Finish receive command.
 * Persistent commands - Add a command that tells the server to keep
 using the same command for each subsequent message received from
 the client.

## How to use the browser client.
The client sends commands to the server, then the server executes those commands.

1. Connect to the web socket server.
    In Javascript: `var socket = new WebSocket()`
2. Send the authenticate command.
   `A<connectionString>`
3. Send a message
   `SHello`
4. Check that the message was received by the event hub.
