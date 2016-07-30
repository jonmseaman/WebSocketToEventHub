using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HttpListenerWebSocket;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.WebSockets;
using System.Security.Permissions;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using WebSocketException = System.Net.WebSockets.WebSocketException;
using WebSocketState = System.Net.WebSockets.WebSocketState;

namespace ServerTest
{
    [TestClass]
    public class ServerTests
    {
        [ClassInitialize]
        public static void Setup(TestContext testContext)
        {
        }

        [TestMethod]
        public void FoundServer()
        {
            // Make sure the server is running to run our tests.
            var processes = Process.GetProcesses();
            bool foundServer = processes.Any(process => process.ProcessName.Equals("EventHubWebSocketServer"));
            Assert.IsTrue(foundServer);
        }

        [TestMethod]
        [ExpectedException(typeof(WebSocketException))]
        public void TestWebSocketConnection()
        {
            // make a web socket with the connection string
            var webSocket = new ClientWebSocket();
            // Should throw an exception because of bad auth info
            string uri = "ws://user:pass@ns.lvh.me/ehName";
            webSocket.ConnectAsync(new Uri(uri), CancellationToken.None).Wait();
            Assert.IsTrue(webSocket.State == WebSocketState.Open);


        }
    }
}
