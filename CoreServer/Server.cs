using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Azure.EventHubs;

namespace CoreServer
{
    public class Server
    {
        /// <summary>
        /// Used to send events to the event hub.
        /// </summary>
        private EventHubClient _client;

        /// <summary>
        /// Holds event data before it is forwarded to the event hub.
        /// </summary>
        private readonly ConcurrentQueue<EventData> _data = new ConcurrentQueue<EventData>();

        public async void ProcessRequest(HttpContext context)
        {
            WebSocket webSocket;
            try
            {
                webSocket = await context.WebSockets.AcceptWebSocketAsync();
            }
            catch (Exception e)
            {
                // The upgrade process failed somehow. For simplicity lets assume it was a failure on the part of the server and indicate this using 500.
                context.Response.StatusCode = 500;
                Console.WriteLine("Exception: {0}", e);
                return;
            }

            // Make a thread that sends the messages.
            var sendingThread = Task.Factory.StartNew(() =>
            {
                // ReSharper disable once AccessToDisposedClosure
                SendEvents(_client, _data, webSocket);
            });

            try
            {
                // Define a receive buffer to hold data received on the WebSocket connection. The buffer will be reused.
                byte[] receiveBuffer = new byte[1024];
                while (webSocket.State == WebSocketState.Open)
                {
                    // The first step is to begin a receive operation on the WebSocket. `ReceiveAsync` takes two parameters:
                    WebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);

                    // Process message.
                    switch (receiveResult.MessageType)
                    {
                        case WebSocketMessageType.Close:
                            Console.WriteLine("Received message of MessageType Close. Closing connection...");
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            break;
                        case WebSocketMessageType.Text:
                        case WebSocketMessageType.Binary:
                            var str = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);
                            ProcessReceivedText(str);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    // Message processing complete.
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: {0}", e);
            }
            finally
            {
                // Cleanup
                // Must await, uses webSocket
                await sendingThread;
                _client.Close();
                webSocket?.Dispose();
            }
        }

        private void ProcessReceivedText(string msg)
        {
            // Process commands
            if (msg.Length > 0)
            {
                var command = msg[0];
                var data = msg.Length > 1 ? msg.Substring(1) : string.Empty;
                RunCommand(command, data);

            }
        }
        #region Commands

        private void RunCommand(char command, string param)
        {
            if (command == (char)CommandsEnum.Authenticate)
            {
                var auth = param.Split(':');
                if (auth.Length == 2)
                {
                    // TODO: New event hub client.
                }
            }
            else if (command == (char)CommandsEnum.Send)
            {
                _data.Enqueue(new EventData(Encoding.UTF8.GetBytes(param)));
            }
        }



        #endregion

        private void SendEvents(EventHubClient client, ConcurrentQueue<EventData> dataQueue, WebSocket webSocket)
        {
            // Batch size in bytes, max is 256kb
            const int maxBatchSize = 256 * 1000;
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
                        sz += data.Body.Count;
                        batch.Add(data);
                    }

                    dt = DateTime.Now - startBatchingTime;
                } while (sz < maxBatchSize && dt < maxDt);
                // Send that data
                if (batch.Count > 0)
                {
                    client.SendAsync(batch).Wait();
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
    }

    public enum CommandsEnum
    {
        Authenticate = 'A',
        Send = 'S',
    }
}
