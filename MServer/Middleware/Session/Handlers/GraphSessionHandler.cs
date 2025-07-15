using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MServer.Middleware.Protocols;
using MServer.Models;
using MServer.Services;

namespace MServer.Middleware
{
    public class GraphSessionHandler
    {
        private readonly GraphExecutor _graphExecutor;
        private readonly StatePersistenceService _statePersistence;
        private readonly WebSocketMessageHandler _wsHandler;
        private readonly ILogger _logger;
        private readonly Func<bool> _isPaused;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, execState> _nodeStates;
        private string _currentExecutionId;
        private CancellationTokenSource _currentExecutionCts;
        private const string PersistenceFile = "node_states.json";

        public GraphSessionHandler(GraphExecutor graphExecutor, StatePersistenceService statePersistence, WebSocketMessageHandler wsHandler, ILogger logger, Func<bool> isPaused, System.Collections.Concurrent.ConcurrentDictionary<string, execState> nodeStates)
        {
            _graphExecutor = graphExecutor;
            _statePersistence = statePersistence;
            _wsHandler = wsHandler;
            _logger = logger;
            _isPaused = isPaused;
            _nodeStates = nodeStates;
        }

        public async Task HandleSession(WebSocket ws, JsonElement root, CancellationToken sdTkn)
        {
            // Parse SSH details if present
            SshDetails mainSshDetails = null;
            if (root.TryGetProperty("ssh", out var sshProp))
            {
                mainSshDetails = new SshDetails
                {
                    Host = sshProp.GetProperty("host").GetString(),
                    Port = sshProp.TryGetProperty("port", out var portProp) ? portProp.GetInt32() : 22,
                    Username = sshProp.GetProperty("username").GetString(),
                    Password = sshProp.GetProperty("password").GetString()
                };
            }

            // Parse repeat count (default 1)
            int repeat = 1;
            if (root.TryGetProperty("repeat", out var repeatProp) && repeatProp.TryGetInt32(out var repeatVal) && repeatVal > 0)
                repeat = repeatVal;

            Node[] initialNodes = null;
            Dictionary<string, List<string>> initialDependencyMap = null;
            bool initialGraphExecute = false;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (root.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "graph_execute" &&
                root.TryGetProperty("nodes", out var nodesProp) &&
                nodesProp.ValueKind == JsonValueKind.Array)
            {
                initialGraphExecute = true;
                initialNodes = JsonSerializer.Deserialize<Node[]>(nodesProp.GetRawText(), options);
                initialDependencyMap = initialNodes.ToDictionary(
                    n => (string)n.Id,
                    n => n.Dependencies?.ToList() ?? new List<string>()
                );
            }
            _logger.LogInformation("graph{Count} nodes.", initialNodes?.Length ?? 0);

            if (
                mainSshDetails == null ||
                string.IsNullOrEmpty(mainSshDetails.Host) ||
                string.IsNullOrEmpty(mainSshDetails.Username) ||
                string.IsNullOrEmpty(mainSshDetails.Password)
            )
            {
                await SendErrorAsync(ws, "Invalid SSH details");
                return;
            }

            if (initialGraphExecute && initialNodes != null)
            {
                for (int i = 0; i < repeat; i++)
                {
                    _currentExecutionId = Guid.NewGuid().ToString();
                    foreach (var node in initialNodes)
                    {
                        var state = new execState
                        {
                            NodeId = node.Id as string,
                            Status = "pending",
                            Inputs = node.Inputs,
                            Args = node.Args as string,
                            Parallel = node.Parallel,
                            Times = node.Times,
                            Dependencies = node.Dependencies,
                            RetryCount = 0,
                            MaxRetries = node.MaxRetries > 0 ? node.MaxRetries : 3,
                            TimeoutSeconds = node.TimeoutSeconds > 0 ? node.TimeoutSeconds : 30
                        };
                        _nodeStates[node.Id as string] = state;
                    }

                    // Use GraphExecutor to execute the graph
                    await _graphExecutor.ExecuteGraphAsync(
                        initialNodes,
                        async state => await SendProgressAsync(ws, state),
                        mainSshDetails,
                        sdTkn,
                        _nodeStates,
                        _isPaused
                    );

                    // --- Minimal summary logic ---
                    var nodeStates = _nodeStates.Values.ToList();
                    var nodeIds = nodeStates.Select(n => n.NodeId).ToArray();
                    var startTime = nodeStates.Min(n => n.StartTime);
                    var endTime = nodeStates.Max(n => n.EndTime);

                    var errors = nodeStates
                        .Where(n => !string.IsNullOrEmpty(n.Error))
                        .ToList();

                    string status;
                    if (nodeStates.All(n => n.Status == "completed"))
                        status = "completed";
                    else if (nodeStates.All(n => n.Status == "failed"))
                        status = "failed";
                    else
                        status = "partial";

                    var summary = new
                    {
                        type = "summary",
                        executionId = _currentExecutionId,
                        nodeIds,
                        startTime,
                        endTime,
                        repeat = i + 1,
                        error = errors.FirstOrDefault()?.Error,
                        status
                    };
                    await _wsHandler.SendMessageAsync(ws, summary);

                    // Only reset node states if another repeat is coming
                    if (i < repeat - 1)
                    {
                        foreach (var node in initialNodes)
                        {
                            var state = new execState
                            {
                                NodeId = node.Id as string,
                                Status = "pending",
                                Inputs = node.Inputs,
                                Args = node.Args as string,
                                Parallel = node.Parallel,
                                Times = node.Times,
                                Dependencies = node.Dependencies,
                                RetryCount = 0,
                                MaxRetries = node.MaxRetries > 0 ? node.MaxRetries : 3,
                                TimeoutSeconds = node.TimeoutSeconds > 0 ? node.TimeoutSeconds : 30
                            };
                            _nodeStates[node.Id as string] = state;
                        }
                    }
                }
            }

            // Main message loop for additional commands
            while (ws.State == WebSocketState.Open && !sdTkn.IsCancellationRequested)
            {
                try
                {
                    var wsMessage = await _wsHandler.ReceiveMessageAsync(ws);
                    if (string.IsNullOrWhiteSpace(wsMessage))
                        continue;

                    _logger.LogInformation("Received: {Message}", wsMessage);

                    var innerDoc = JsonDocument.Parse(wsMessage);
                    var innerRoot = innerDoc.RootElement;
                    string type = innerRoot.TryGetProperty("type", out var typeProp2) ? typeProp2.GetString() : null;

                    if (type == "graph_execute")
                    {
                        // Cancel any previous execution
                        _currentExecutionCts?.Cancel();
                        _currentExecutionCts = new CancellationTokenSource();

                        // Optionally clear _nodeStates to avoid interference
                        _nodeStates.Clear();

                        var graphMsg = JsonSerializer.Deserialize<MServer.Models.GraphExecuteMessage>(wsMessage);
                        if (graphMsg?.Nodes != null)
                        {
                            _currentExecutionId = Guid.NewGuid().ToString();
                            var dependencyMap = graphMsg.Nodes.ToDictionary(
                                n => (string)n.Id,
                                n => n.Dependencies?.ToList() ?? new List<string>()
                            );

                            foreach (var node in graphMsg.Nodes)
                            {
                                var state = new execState
                                {
                                    NodeId = (string)node.Id,
                                    Status = "pending",
                                    Inputs = node.Inputs,
                                    Args = node.Args as string,
                                    Parallel = node.Parallel,
                                    Times = node.Times,
                                    Dependencies = node.Dependencies,
                                    RetryCount = 0,
                                    MaxRetries = node.MaxRetries > 0 ? node.MaxRetries : 3,
                                    TimeoutSeconds = node.TimeoutSeconds > 0 ? node.TimeoutSeconds : 30
                                };
                                _nodeStates[(string)node.Id] = state;
                            }

                            // Combine sdTkn and execution token
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                                sdTkn, _currentExecutionCts.Token);

                            await _graphExecutor.ExecuteGraphAsync(
                                graphMsg.Nodes.ToArray(),
                                async state =>
                                {
                                    try
                                    {
                                        _logger.LogInformation("Progress for node {NodeId}: {Status}", state.NodeId, state.Status);
                                        await SendProgressAsync(ws, state);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error in progress callback for node {NodeId}", state.NodeId);
                                    }
                                },
                                mainSshDetails,
                                linkedCts.Token,
                                _nodeStates,
                                _isPaused
                            );

                            _logger.LogInformation("Completed graph_execute execution: {ExecutionId}", _currentExecutionId);

                            // --- Minimal summary logic (same as initial execution) ---
                            var nodeStates = _nodeStates.Values.ToList();
                            var nodeIds = nodeStates.Select(n => n.NodeId).ToArray();
                            var startTime = nodeStates.Min(n => n.StartTime);
                            var endTime = nodeStates.Max(n => n.EndTime);

                            var errors = nodeStates
                                .Where(n => !string.IsNullOrEmpty(n.Error))
                                .ToList();

                            string status;
                            if (nodeStates.All(n => n.Status == "completed"))
                                status = "completed";
                            else if (nodeStates.All(n => n.Status == "failed"))
                                status = "failed";
                            else
                                status = "partial";

                            var summary = new
                            {
                                type = "summary",
                                executionId = _currentExecutionId,
                                nodeIds,
                                startTime,
                                endTime,
                                repeat = 1,
                                error = errors.FirstOrDefault()?.Error,
                                status
                            };
                            await _wsHandler.SendMessageAsync(ws, summary);

                            // Optionally, send state after summary
                            var stateJson = JsonSerializer.Serialize(new { executionId = _currentExecutionId, nodes = _nodeStates.Values });
                            await _wsHandler.SendMessageAsync(ws, new { type = "state", data = stateJson });
                        }
                    }
                    else if (type == "pause")
                    {
                        // If you want to support pausing, you can implement it here
                        await _wsHandler.SendMessageAsync(ws, new { type = "paused" });
                    }
                    else if (type == "resume")
                    {
                        // If you want to support resuming, you can implement it here
                        await _wsHandler.SendMessageAsync(ws, new { type = "resumed" });
                    }
                    else if (type == "save_state")
                    {
                        if (!string.IsNullOrEmpty(_currentExecutionId))
                        {
                            var dict = new Dictionary<string, execState>(_nodeStates);
                            await _statePersistence.SaveNodeStatesAsync(GetPersistenceFile(_currentExecutionId), dict);
                        }
                        await _wsHandler.SendMessageAsync(ws, new { type = "saved" });
                    }
                    else if (type == "load_state")
                    {
                        var execId = innerRoot.TryGetProperty("executionId", out var execIdProp) ? execIdProp.GetString() : null;
                        if (!string.IsNullOrEmpty(execId))
                        {
                            var loadedStates = await _statePersistence.LoadNodeStatesAsync(GetPersistenceFile(execId));
                            foreach (var kv in loadedStates)
                                _nodeStates[kv.Key] = kv.Value;
                            _currentExecutionId = execId;
                            await _wsHandler.SendMessageAsync(ws, new { type = "loaded" });
                        }
                    }
                    else if (type == "cancel")
                    {
                        _logger.LogInformation("Cancelling current execution: {ExecutionId}", _currentExecutionId);
                        _currentExecutionCts?.Cancel();
                        await _wsHandler.SendMessageAsync(ws, new { type = "cancelled", executionId = _currentExecutionId });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling WS input.");
                    await SendErrorAsync(ws, ex.ToString());
                }
            }
        }

        private async Task SendProgressAsync(WebSocket ws, execState state)
        {
            if (state.Error != null && state.Error.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
            {
                state.Error = "SSH connection failed";
            }
            await _wsHandler.SendMessageAsync(ws, new { type = "progress", data = state });
        }

        private async Task SendErrorAsync(WebSocket ws, string error)
        {
            try
            {
                await _wsHandler.SendMessageAsync(ws, new { type = "error", message = error });
            }
            catch { }
        }

        private string GetPersistenceFile(string executionId)
        {
            return $"{PersistenceFile}.{executionId}.json";
        }
    }
}
