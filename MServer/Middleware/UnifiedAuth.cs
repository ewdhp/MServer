using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MServer.Models;
using MServer.Services;

namespace MServer.Middleware
{
    // Define the missing GraphExecuteMessage class
    public class GraphExecuteMessage
    {
        public string Type { get; set; }
        public Node[] Nodes { get; set; }
    }

    public class UnifiedAuth
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<UnifiedAuth> _logger;
        private readonly GraphExecutor _graphExecutor;
        private readonly StatePersistenceService _statePersistence;
        private readonly WebSocketMessageHandler _wsHandler;

        // Track execution state for each node by node ID
        private readonly ConcurrentDictionary<string, NodeExecutionState> _nodeStates = new();

        // Pause/Resume control
        private volatile bool _isPaused = false;

        // Persistence file path (for demonstration)
        private const string PersistenceFile = "node_states.json";

        // Add a field to track the current execution ID
        private string _currentExecutionId = null;

        // Add a field to track scheduled recurring jobs
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _recurringExecutions = new();

        public UnifiedAuth(
            RequestDelegate next,
            ILogger<UnifiedAuth> logger,
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
                using var webSocket = await context
                    .WebSockets.AcceptWebSocketAsync();

                await HandleWebSocketConnection(webSocket);
            }
            else
            {
                await _next(context);
            }
        }

        private async Task HandleWebSocketConnection(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            _logger.LogInformation("WebSocket connection established.");

            SshDetails mainSshDetails = null;
            Node[] initialNodes = null;
            Dictionary<string, List<string>> initialDependencyMap = null;
            bool initialGraphExecute = false;

            WebSocketReceiveResult result = null;
            CancellationTokenSource shutdownCts = new CancellationTokenSource();

            try
            {
                // Receive the first message
                var message = await _wsHandler.ReceiveMessageAsync(webSocket);
                if (string.IsNullOrWhiteSpace(message))
                {
                    _logger.LogError("Received empty message from WebSocket client.");
                    await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Empty message received", CancellationToken.None);
                    return;
                }

                _logger.LogInformation("Received initial message: {Message}", message);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;

                // Parse SSH details and nodes if present
                if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "graph_execute" && root.TryGetProperty("ssh", out var sshProp))
                {
                    mainSshDetails = new SshDetails
                    {
                        Host = sshProp.GetProperty("host").GetString(),
                        Port = sshProp.TryGetProperty("port", out var portProp) ? portProp.GetInt32() : 22,
                        Username = sshProp.GetProperty("username").GetString(),
                        Password = sshProp.GetProperty("password").GetString()
                    };
                    _logger.LogInformation("Parsed SSH details: Host={Host}, Username={Username}", mainSshDetails.Host, mainSshDetails.Username);

                    if (root.TryGetProperty("nodes", out var nodesProp) && nodesProp.ValueKind == JsonValueKind.Array)
                    {
                        initialGraphExecute = true;
                        initialNodes = JsonSerializer.Deserialize<Node[]>(nodesProp.GetRawText(), options);
                        initialDependencyMap = initialNodes.ToDictionary(
                            n => n.Id,
                            n => n.Dependencies?.ToList() ?? new List<string>()
                        );
                    }
                    _logger.LogInformation("Initial graph execution detected with {Count} nodes.", initialNodes?.Length ?? 0);
                }

                if (mainSshDetails == null || string.IsNullOrEmpty(mainSshDetails.Host) || string.IsNullOrEmpty(mainSshDetails.Username) || string.IsNullOrEmpty(mainSshDetails.Password))
                {
                    throw new ArgumentException("Invalid SSH details. Host, username, and password must be provided.");
                }

                // SSH connection is now handled by SshService as needed

                if (initialGraphExecute && initialNodes != null)
                {
                    _currentExecutionId = Guid.NewGuid().ToString();

                    foreach (var node in initialNodes)
                    {
                        var state = new NodeExecutionState
                        {
                            NodeId = node.Id,
                            Status = "pending",
                            Inputs = node.Inputs,
                            Args = node.Args,
                            Parallel = node.Parallel,
                            Times = node.Times,
                            Dependencies = node.Dependencies,
                            RetryCount = 0,
                            MaxRetries = node.MaxRetries > 0 ? node.MaxRetries : 3,
                            TimeoutSeconds = node.TimeoutSeconds > 0 ? node.TimeoutSeconds : 30
                        };
                        _nodeStates[node.Id] = state;
                    }

                    // Use GraphExecutor to execute the graph
                    _ = _graphExecutor.ExecuteGraphAsync(
                        initialNodes,
                        initialDependencyMap,
                        async state => await SendProgressAsync(webSocket, state),
                        mainSshDetails,
                        shutdownCts.Token
                    );

                    var stateJson = JsonSerializer.Serialize(new { executionId = _currentExecutionId, nodes = _nodeStates.Values });
                    await _wsHandler.SendMessageAsync(webSocket, new { type = "state", data = stateJson });
                }

                // Main message loop
                while (webSocket.State == WebSocketState.Open && !shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        var wsMessage = await _wsHandler.ReceiveMessageAsync(webSocket);
                        if (string.IsNullOrWhiteSpace(wsMessage))
                            continue;

                        _logger.LogInformation("Received data from WebSocket client: {Message}", wsMessage);

                        var innerDoc = JsonDocument.Parse(wsMessage);
                        var innerRoot = innerDoc.RootElement;
                        string type = innerRoot.TryGetProperty("type", out var typeProp2) ? typeProp2.GetString() : null;

                        if (type == "graph_execute")
                        {
                            var graphMsg = JsonSerializer.Deserialize<MServer.Models.GraphExecuteMessage>(wsMessage);
                            if (graphMsg?.Nodes != null)
                            {
                                _currentExecutionId = Guid.NewGuid().ToString();
                                var dependencyMap = graphMsg.Nodes.ToDictionary(
                                    n => n.Id,
                                    n => n.Dependencies?.ToList() ?? new List<string>()
                                );

                                foreach (var node in graphMsg.Nodes)
                                {
                                    var state = new NodeExecutionState
                                    {
                                        NodeId = node.Id,
                                        Status = "pending",
                                        Inputs = node.Inputs,
                                        Args = node.Args,
                                        Parallel = node.Parallel,
                                        Times = node.Times,
                                        Dependencies = node.Dependencies,
                                        RetryCount = 0,
                                        MaxRetries = node.MaxRetries > 0 ? node.MaxRetries : 3,
                                        TimeoutSeconds = node.TimeoutSeconds > 0 ? node.TimeoutSeconds : 30
                                    };
                                    _nodeStates[node.Id] = state;
                                }

                                _ = _graphExecutor.ExecuteGraphAsync(
                                    graphMsg.Nodes.ToArray(),
                                    dependencyMap,
                                    async state => await SendProgressAsync(webSocket, state),
                                    mainSshDetails,
                                    shutdownCts.Token
                                );

                                var stateJson = JsonSerializer.Serialize(new { executionId = _currentExecutionId, nodes = _nodeStates.Values });
                                await _wsHandler.SendMessageAsync(webSocket, new { type = "state", data = stateJson });
                            }
                        }
                        else if (type == "pause")
                        {
                            _isPaused = true;
                            await _wsHandler.SendMessageAsync(webSocket, new { type = "paused" });
                        }
                        else if (type == "resume")
                        {
                            _isPaused = false;
                            await _wsHandler.SendMessageAsync(webSocket, new { type = "resumed" });
                        }
                        else if (type == "save_state")
                        {
                            if (!string.IsNullOrEmpty(_currentExecutionId))
                            {
                                var dict = new Dictionary<string, NodeExecutionState>(_nodeStates);
                                // Delegates state persistence:
                                await _statePersistence.SaveNodeStatesAsync(GetPersistenceFile(_currentExecutionId), dict);
                            }
                            await _wsHandler.SendMessageAsync(webSocket, new { type = "saved" });
                        }
                        else if (type == "load_state")
                        {
                            var execId = innerRoot.TryGetProperty("executionId", out var execIdProp)
                                ? execIdProp.GetString()
                                : null;
                            if (!string.IsNullOrEmpty(execId))
                            {
                                var loadedStates = await _statePersistence.LoadNodeStatesAsync(GetPersistenceFile(execId));
                                foreach (var kv in loadedStates)
                                    _nodeStates[kv.Key] = kv.Value;
                                _currentExecutionId = execId;
                                await _wsHandler.SendMessageAsync(webSocket, new { type = "loaded" });
                            }
                        }
                        // Add more message types as needed, using the appropriate services
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while handling WebSocket input.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling WebSocket connection.");
            }
            finally
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
                _logger.LogInformation("WebSocket connection closed.");
            }
        }

        private async Task SendProgressAsync
        (WebSocket webSocket, NodeExecutionState state)
        {
            // Optionally, customize error message for connection refused
            if (state.Error != null && state.Error.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
            {
                state.Error = "SSH connection failed: Connection refused. Please check if the SSH server is running and accessible.";
            }
            await _wsHandler.SendMessageAsync(webSocket, new
            {
                type = "progress",
                data = state
            });
        }

        private string GetPersistenceFile
        (string executionId) => $"{PersistenceFile}.{executionId}.json";
    }
}