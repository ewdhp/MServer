using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MServer.Middleware
{
    public class WebSocketMessageHandler
    {
        public async Task SendMessageAsync
        (WebSocket webSocket, object message)
        {
            var json = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                System.Threading.CancellationToken.None
            );
        }

        public async Task<string>ReceiveMessageAsync(WebSocket webSocket)
        {
            var buffer = new byte[4096];
            var result = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                System.Threading.CancellationToken.None
            );
            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }
    }
}