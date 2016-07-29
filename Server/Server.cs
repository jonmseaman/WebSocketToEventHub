// This program is a binary echo server for WebSockets using the new WebSockets API in .NET 4.5. It is designed to run on the Windows 8 developer preview.
//
// This console application uses `HttpListener` to receive WebSocket connections. It expects to receive binary data and it streams back the data as it receives it.
//
// This program takes advantage of the new asynchrony features in C# 5. Explaining these features is beyond the scope of this documentation - 
// to learn more visit the [async homepage](http://msdn.com/async) or read the [async articles](http://blogs.msdn.com/b/ericlippert/archive/tags/async) 
// on Eric Lippert's blog.   
//
// The [source](https://github.com/paulbatum/WebSocket-Samples) for this sample is on GitHub.
//
//### Imports
// Some standard imports, but note the last one is the new `System.Net.WebSockets` namespace.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Principal;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace HttpListenerWebSocket
{
    // Passes an HttpListener prefix for the server to listen on. The prefix 'http://+:80/wsDemo/' indicates that the server should listen on 
    // port 80 for requests to wsDemo (e.g. http://localhost/wsDemo). For more information on HttpListener prefixes see [MSDN](http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx).            
    class Program
    {
        static void Main(string[] args)
        {
            // Check if the server is running
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            if (!isAdmin)
            {
                Console.WriteLine("Program must be run as administrator.");
                Environment.Exit(1);
            }
            Console.WriteLine(Process.GetCurrentProcess().ProcessName);

            var server = new Server();
            server.Start("http://+:80/");
            Console.WriteLine("Press enter to exit...");
            Console.ReadLine();
        }
    }

    public class Server
    {
        private int count = 0;

        public async void Start(string listenerPrefix)
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(listenerPrefix);
            listener.AuthenticationSchemes = AuthenticationSchemes.Basic;
            listener.Start();
            Console.WriteLine("Listening...");

            while (true)
            {
                HttpListenerContext listenerContext = await listener.GetContextAsync();
                if (listenerContext.Request.IsWebSocketRequest)
                {
                    ProcessRequest(listenerContext);
                }
                else
                {
                    listenerContext.Response.StatusCode = 400;
                    listenerContext.Response.Close();
                }
            }
        }

        private async void ProcessRequest(HttpListenerContext listenerContext)
        {
            WebSocketContext webSocketContext = null;
            // Try to make an event hub client.
            var connectionString = GetConnectionStringFromContext(listenerContext);
            listenerContext.Response.StatusCode = 401;
            var client = EventHubClient.CreateFromConnectionString(connectionString);

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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="listenerContext"></param>
        /// <exception cref="ArgumentExcepton">Thrown when there is not enough
        /// information in the request to make the connection string
        /// or part of the url is not formatted correctly.</exception>
        /// <returns></returns>
        private static string GetConnectionStringFromContext(HttpListenerContext listenerContext)
        {
            // KeyName and Key
            var identity = listenerContext.User.Identity as HttpListenerBasicIdentity;
            string keyName = null;
            string key = null;
            if (!listenerContext.Request.IsAuthenticated || identity != null)
            {
                keyName = identity.Name;
                key = identity.Password;
            }
            else
            {
                throw new ArgumentException("Did not find authentication in request.");
            }

            // Namespace
            var host = listenerContext.Request.UserHostName;
            var periodLocation = host.IndexOf(".");
            string nsName = null;
            if (periodLocation < 0) throw new ArgumentException("Missing namespace name.");
            nsName = host.Substring(0, periodLocation);
            // Event hub name
            var rawUrl = listenerContext.Request.RawUrl;
            if (rawUrl.Length < 3) throw new ArgumentException("Missing event hub name..");
            string eventHubName = rawUrl.Substring(1);

            return $"Endpoint=sb://{nsName}.servicebus.windows.net/;SharedAccessKeyName={keyName};SharedAccessKey={key};EntityPath={eventHubName}";
        }
    }

    // This extension method wraps the BeginGetContext / EndGetContext methods on HttpListener as a Task, using a helper function from the Task Parallel Library (TPL).
    // This makes it easy to use HttpListener with the C# 5 asynchrony features.
    public static class HelperExtensions
    {
        public static Task GetContextAsync(this HttpListener listener)
        {
            return Task.Factory.FromAsync<HttpListenerContext>(listener.BeginGetContext, listener.EndGetContext, TaskCreationOptions.None);
        }
    }
}
