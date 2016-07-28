using System;
using System.Threading;
using HttpListenerWebSocket;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace ServerTest
{
    [TestClass]
    public class ServerTests
    {
        [TestMethod]
        public void TestWebSocketConnection()
        {
            // Start the server
            var server = new Server();
            Task.Factory.StartNew(() =>
            {
                server.Start("http://+:80/");
            });
            //server.Start("http://+:80/");
            // make a web socket with the connection string
            ClientWebSocket webSocket;
            Task.Factory.StartNew(() =>
            {
                Thread.Sleep(3000);
                webSocket = new ClientWebSocket();
                string uri = "ws://Managed:wqI+ApmtjNkq7FvYsnevOa8MJDQnqcrJlV1O2pF0alA=@localhost/testwebsocketreceiver/";
                webSocket.ConnectAsync(new Uri(uri), CancellationToken.None).Wait();
                Assert.IsTrue(webSocket.State == WebSocketState.Open);

            }).Wait();
        }
    }
}
