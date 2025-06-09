using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace MServer.Middleware
{
    public class UnifiedAuth
    {
        private readonly RequestDelegate _next; // Define the _next field
        private readonly ILogger<UnifiedAuth> _logger;

        public UnifiedAuth(RequestDelegate next, ILogger<UnifiedAuth> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next)); // Initialize _next
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                await HandleWebSocketConnection(webSocket);
            }
            else
            {
                await _next(context); // Pass the request to the next middleware
            }
        }
private async Task HandleWebSocketConnection(System.Net.WebSockets.WebSocket webSocket)
{
    var buffer = new byte[1024 * 4];
    _logger.LogInformation("WebSocket connection established.");

    try
    {
        // Receive the first message containing SSH connection details
        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), System.Threading.CancellationToken.None);
        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogError("Received empty message from WebSocket client.");
            await webSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.InvalidPayloadData, "Empty message received", System.Threading.CancellationToken.None);
            return;
        }

        _logger.LogInformation("Received SSH connection details: {Message}", message);

        // Deserialize SSH details
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var sshDetails = JsonSerializer.Deserialize<SshDetails>(message, options);

        if (sshDetails == null || string.IsNullOrEmpty(sshDetails.Host) || string.IsNullOrEmpty(sshDetails.Username) || string.IsNullOrEmpty(sshDetails.Password))
        {
            throw new ArgumentException("Invalid SSH details. Host, username, and password must be provided.");
        }

        _logger.LogInformation("Attempting to connect to SSH server at {Host} with username {Username}", sshDetails.Host, sshDetails.Username);

        using (var sshClient = new SshClient(sshDetails.Host, sshDetails.Username, sshDetails.Password))
        {
            sshClient.Connect();
            _logger.LogInformation("SSH connection established to {Host}.", sshDetails.Host);

            var shellStream = sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024);
            _logger.LogInformation("SSH shell stream created.");

            // Start reading from the shell stream in a background task
            _ = Task.Run(async () =>
            {
                var sshBuffer = new byte[1024];
                while (sshClient.IsConnected)
                {
                    try
                    {
                        var bytesRead = shellStream.Read(sshBuffer, 0, sshBuffer.Length);
                        if (bytesRead > 0)
                        {
                            var sshOutput = Encoding.UTF8.GetString(sshBuffer, 0, bytesRead);
                            _logger.LogDebug("Received data from SSH: {Output}", sshOutput);

                            var responseBytes = Encoding.UTF8.GetBytes(sshOutput);
                            await webSocket.SendAsync(new ArraySegment<byte>(responseBytes), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while reading from SSH shell stream.");
                        break;
                    }
                }
            });

            // Handle WebSocket input and forward it to the SSH shell
            while (!result.CloseStatus.HasValue)
            {
                try
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), System.Threading.CancellationToken.None);
                    message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger.LogDebug("Received data from WebSocket client: {Message}", message);

                    // Parse the WebSocket message as JSON
                    var commandMessage = JsonSerializer.Deserialize<SshDetails>(message, options);
                    if (commandMessage?.Type == "command" && !string.IsNullOrEmpty(commandMessage.Command))
                    {
                        _logger.LogInformation("Executing command: {Command}", commandMessage.Command);
                        var cmd = sshClient.RunCommand(commandMessage.Command);
                        var output = cmd.Result + cmd.Error;
                        var responseBytes = Encoding.UTF8.GetBytes(output);
                        await webSocket.SendAsync(new ArraySegment<byte>(responseBytes), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while handling WebSocket input.");
                    break;
                }
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error handling WebSocket connection.");
    }
    finally
    {
        if (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
        {
            await webSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Connection closed", System.Threading.CancellationToken.None);
        }
        _logger.LogInformation("WebSocket connection closed.");
    }
}
        private class SshDetails
{
    public string Type { get; set; } // Handles the "type" field
    public string Host { get; set; } // SSH host
    public string Username { get; set; } // SSH username
    public string Password { get; set; } // SSH password
    public string Command { get; set; } // Command to execute
}
    }
}