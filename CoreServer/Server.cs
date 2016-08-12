using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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

        private WebSocket _webSocket;

        /// <summary>
        /// Holds event data before it is forwarded to the event hub.
        /// </summary>
        private readonly ConcurrentQueue<EventData> _data = new ConcurrentQueue<EventData>();

        public async Task ProcessRequest(HttpContext context)
        {
            Console.WriteLine("Processing request.");
            try
            {
                _webSocket = await context.WebSockets.AcceptWebSocketAsync(subProtocol: null);
                Console.WriteLine("Accepting websocket...");
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
               SendEvents(_data, _webSocket);
            });

            try
            {
                // Define a receive buffer to hold data received on the WebSocket connection. The buffer will be reused.
                var buffer = new ArraySegment<byte>(new byte[4096]);
                var token = CancellationToken.None;
                while (_webSocket.State == WebSocketState.Open)
                {
                    var receiveResult = await _webSocket.ReceiveAsync(buffer, token);

                    // Process message.
                    switch (receiveResult.MessageType)
                    {
                        case WebSocketMessageType.Close:
                            Console.WriteLine("Closing...");
                            Console.WriteLine("Received message of MessageType Close. Closing connection...");
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            break;
                        case WebSocketMessageType.Text:
                        case WebSocketMessageType.Binary:
                            Console.WriteLine("Processing message...");
                            var str = Encoding.UTF8.GetString(buffer.Array, 0, receiveResult.Count);
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
                _webSocket?.Dispose();
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

        public void RunCommand(char command, string param)
        {
            Console.WriteLine("Command: " + command);
            switch (command)
            {
                case (char)CommandsEnum.Authenticate:
                    Authenticate(param);
                    break;
                case (char)CommandsEnum.Receive:
                    Receive(param);
                    break;
                case (char)CommandsEnum.Send:
                    Send(param);
                    break;
            }
        }

        /// <summary>
        /// Sets <see cref="_client"/> to a new <see cref="EventHubClient"/> corresponding to the
        /// supplied connection string.
        /// </summary>
        /// <param name="connectionString">Connection string used to create the event hub.</param>
        public void Authenticate(string connectionString)
        {
            Console.WriteLine("Creating client...");
            _client = EventHubClient.Create(connectionString);
            Console.WriteLine("Done.");
        }

        public void Receive(string countStr)
        {
            int count;
            if (!int.TryParse(countStr, out count))
            {
                count = 1;
            }
            var rec = _client.CreateReceiver("test", "0", DateTime.Now - TimeSpan.FromMinutes(5));
            var received = rec.ReceiveAsync(count).Result;
            foreach (var eventData in received)
            {
                _webSocket.SendAsync(eventData.Body, WebSocketMessageType.Text, true, CancellationToken.None);
            }

        }

        public void Send(string message)
        {
            Console.WriteLine("Sending command");
            _data.Enqueue(new EventData(Encoding.UTF8.GetBytes(message)));
        }

        #endregion

        private void SendEvents(ConcurrentQueue<EventData> dataQueue, WebSocket webSocket)
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
                    Console.WriteLine("Sending...");
                    _client.SendAsync(batch).Wait();
                    Console.WriteLine("done");
                }
            }
        }
    }

    public enum CommandsEnum
    {
        /// <summary>
        /// Specifies a new connection string to use for sending to the event hub.
        /// </summary>
        Authenticate = 'A',
        Receive = 'R',
        Send = 'S',
    }
}
