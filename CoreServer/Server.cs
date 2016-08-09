using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;

namespace CoreServer
{
    public class Server
    {
                private async void ProcessRequest(HttpListenerContext listenerContext)
        {
            WebSocketContext webSocketContext = null;
            // Try to make an event hub client.
            var url = GetUrl(listenerContext);
            // TODO: Get the regex pattern from app.config.
            var connectionString = GetConnectionString(url, new Regex(@"(?:https*:\/\/)(?<keyName>\w+):(?<key>.+)@(?<ns>\w+)(?:.\w+)(?:\:80)*\/(?<name>\w+)"));
            listenerContext.Response.StatusCode = 401;
            var client = EventHubClient.Create(connectionString);

            // Queue to hold event data in between receiving and sending.
            var data = new ConcurrentQueue<EventData>();

            Console.WriteLine(connectionString);

            try
            {
                // When calling `AcceptWebSocketAsync` the negotiated subprotocol must be specified. This sample assumes that no subprotocol 
                // was requested. 
                webSocketContext = await listenerContext.AcceptWebSocketAsync(subProtocol: null);
                Interlocked.Increment(ref count);
                Console.WriteLine("Processed: {0}", count);
            }
            catch (Exception e)
            {
                // The upgrade process failed somehow. For simplicity lets assume it was a failure on the part of the server and indicate this using 500.
                listenerContext.Response.StatusCode = 500;
                listenerContext.Response.Close();
                Console.WriteLine("Exception: {0}", e);
                return;
            }

            WebSocket webSocket = webSocketContext.WebSocket;
            // Make a thread that sends the messages.
            var sendingThread = Task.Factory.StartNew(() =>
            {
                // ReSharper disable once AccessToDisposedClosure
                SendEvents(client, data, webSocket);
            });

            try
            {
                //### Receiving
                // Define a receive buffer to hold data received on the WebSocket connection. The buffer will be reused.
                byte[] receiveBuffer = new byte[1024];
                while (webSocket.State == WebSocketState.Open)
                {
                    // The first step is to begin a receive operation on the WebSocket. `ReceiveAsync` takes two parameters:
                    WebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);

                    // Adding the message to a queue.
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("Received message of MessageType Close. Closing connection...");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                    else if (receiveResult.MessageType == WebSocketMessageType.Text)
                    {
                        var str = Encoding.Default.GetString(receiveBuffer, 0, receiveResult.Count);
                        data.Enqueue(new EventData(Encoding.UTF8.GetBytes(str)));
                    }
                    else
                    {
                        await webSocket.SendAsync(new ArraySegment<byte>(receiveBuffer, 0, receiveResult.Count), WebSocketMessageType.Binary, receiveResult.EndOfMessage, CancellationToken.None);
                        var str = Encoding.Default.GetString(receiveBuffer, 0, receiveResult.Count);

                        data.Enqueue(new EventData(Encoding.UTF8.GetBytes(str)));
                    }

                    // Message processing complete.
                }
            }
            catch (Exception e)
            {
                // Just log any exceptions to the console. Pretty much any 
                // exception that occurs when calling 
                // `SendAsync`/`ReceiveAsync`/`CloseAsync` is unrecoverable 
                // in that it will abort the connection and leave the 
                // `WebSocket` instance in an unusable state.
                Console.WriteLine("Exception: {0}", e);
            }
            finally
            {
                // Clean up by disposing the WebSocket once it is closed/aborted.
                await sendingThread; // Must this thread as it uses webSocket, which is about to be disposed.
                client.Close();
                webSocket?.Dispose();
            }
        }

        private void SendEvents(EventHubClient client, ConcurrentQueue<EventData> dataQueue, WebSocket webSocket)
        {
            const int maxBatchSize = 256*1000; // Max batch size is 256kb. 
            while (!dataQueue.IsEmpty || webSocket.State == WebSocketState.Open)
            {
                // Get a batch
                var batch = new List<EventData>();
                long sz = 0;
                var startBatchingTime = DateTime.Now;
                TimeSpan dt;
                var maxDt = TimeSpan.FromMilliseconds(3.0);
                do
                {
                    EventData data;
                    var result = dataQueue.TryDequeue(out data);
                    if (result)
                    {
                        sz += data.SerializedSizeInBytes;
                        batch.Add(data);
                    }

                    dt = DateTime.Now - startBatchingTime;
                } while (sz < maxBatchSize && dt < maxDt);
                // Send that data
                if (batch.Count > 0)
                {
                    client.SendBatch(batch);
                }
            }
        }

        private static string GetConnectionString(string url, Regex r)
        {
            var match = r.Match(url);
            if (!match.Success) throw new ArgumentException("Regex did not find a match in url.");
            var g = match.Groups;
            var conStr = $"Endpoint=sb://{g["ns"]}.servicebus.windows.net/;SharedAccessKeyName={g["keyName"]};SharedAccessKey={g["key"]};EntityPath={g["name"]}";
            return conStr;
        }



        private static string GetUrl(HttpListenerContext context)
        {
            var user = context.User.Identity as HttpListenerBasicIdentity;
            if (user == null) throw new ArgumentException("Did not find user auth in context.");
            var url = context.Request.Url.ToString().Replace("//", $"//{user.Name}:{user.Password}@");
            return url;
        }

    }
}
