using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MServer.Models; // <-- Ensure this is present
using MServer.Services;
using MServer.Middleware.Protocols;
using MServer.Middleware.Session.Handlers;

namespace MServer.Middleware.Session
{
    public class GraphExecuteMessage
    {
        public string Type { get; set; }
        public Node[] Nodes { get; set; }
    }

public class SessionRouter
    {
        private readonly RequestDelegate _next;
    private readonly ILogger<SessionRouter> _logger;
        private readonly GraphExecutor _graphExecutor;
        private readonly StatePersistenceService _statePersistence;
        private readonly WebSocketMessageHandler _wsHandler;

        // Track execution state for each node by node ID
        private readonly ConcurrentDictionary<string, execState>
        _nodeStates = new();

        // Pause/Resume control
        private volatile bool _isPaused = false;

        // Persistence file path (for demonstration)
        private const string PersistenceFile = "node_states.json";

        // Add a field to track the current execution ID
        private string _currentExecutionId = null;

        // Add a field to track scheduled recurring jobs
        private readonly ConcurrentDictionary<string, CancellationTokenSource>
        _recurringExecutions = new();

        // Add this field to track the current execution's cancellation token source
        private CancellationTokenSource _currentExecutionCts = null;

    public SessionRouter(
        RequestDelegate next,
        ILogger<SessionRouter> logger,
        GraphExecutor graphExecutor,
        StatePersistenceService statePersistence,
        WebSocketMessageHandler wsHandler)
        {
            _next = next;
            _logger = logger;
            _graphExecutor = graphExecutor;
            _statePersistence = statePersistence;
            _wsHandler = wsHandler;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var ws = await context
                .WebSockets.AcceptWebSocketAsync();
                var sdTkn = context.RequestAborted;
                await WSCHandler
                (ws, sdTkn);
            }
            else
            {
                await _next(context);
            }
        }

        // Update the signature to accept a CancellationToken
        private async Task WSCHandler(WebSocket ws, CancellationToken sdTkn)
        {
            var buffer = new byte[1024 * 4];
            // SshDetails mainSshDetails = null; // No longer needed here after refactor

            try
            {
                // Receive the first message
                var message = await _wsHandler.ReceiveMessageAsync(ws);
                if (string.IsNullOrWhiteSpace(message))
                {
                    _logger.LogError("Received empty message.");
                    await ws.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Empty message received", CancellationToken.None);
                    return;
                }

                _logger.LogInformation("Received: {Message}", message);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;

                // Switch on type
                if (root.TryGetProperty("type", out var typeProp))
                {
                    var type = typeProp.GetString();
                    switch (type)
                    {
                        case "connect":
                            await HandleTerminalSession(ws, root, sdTkn);
                            return;
                        case "graph_execute":
                            await HandleGraphExecution(ws, root, sdTkn);
                            return;
                        default:
                            await SendErrorAsync(ws, $"Unknown type: {type}");
                            return;
                    }
                }
                else
                {
                    await SendErrorAsync(ws, "Missing 'type' property in message.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling WS connection.");
            }
            finally
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
                _logger.LogInformation("WebSocket connection closed.");
            }
        }
        // Handler for terminal session (type: connect)
        private async Task HandleTerminalSession(WebSocket ws, JsonElement root, CancellationToken sdTkn)
        {
            var handler = new TerminalSessionHandler(_wsHandler, _logger);
            await handler.HandleSession(ws, root, sdTkn);
        }
        // End of WSCHandler

        // Handler for graph execution (type: graph_execute)
        private async Task HandleGraphExecution(WebSocket ws, JsonElement root, CancellationToken sdTkn)
        {
            // Delegate to GraphSessionHandler
            var handler = new GraphSessionHandler(_graphExecutor, _statePersistence, _wsHandler, _logger, () => _isPaused, _nodeStates);
            await handler.HandleSession(ws, root, sdTkn);
            // End of UnifiedAuth class
        }
        private async Task SendProgressAsync(WebSocket ws, execState state)
        {
            if (state.Error != null && state.Error
                .Contains("Connection refused",
                StringComparison.OrdinalIgnoreCase))
            {
                state.Error = "SSH connection failed";
            }
            await _wsHandler.SendMessageAsync(ws, new
            {
                type = "progress",
                data = state
            });
        }

        // Add this helper to send error messages to the client
        private async Task SendErrorAsync(WebSocket ws, string error)
        {
            try
            {
                await _wsHandler.SendMessageAsync(ws, new
                {
                    type = "error",
                    message = error
                });
            }
            catch
            {
                // Ignore errors while sending error messages
            }
        }

        // Add this method to generate a unique state file name per execution
        private string GetPersistenceFile(string executionId)
        {
            return $"{PersistenceFile}.{executionId}.json";
        }

        // The backend generates a unique state file name for each execution:
        // private string GetPersistenceFile(string executionId) => $"{PersistenceFile}.{executionId}.json";
        // No need for the client to specify state file or execution ID in the config.
    }
}
