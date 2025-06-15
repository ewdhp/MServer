using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;

namespace MServer.Middleware
{
    public class UnifiedAuth
    {
        private readonly RequestDelegate _next; // Define the _next field
        private readonly ILogger<UnifiedAuth> _logger;

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

            SshDetails mainSshDetails = null;
            Node[] initialNodes = null;
            Dictionary<string, List<string>> initialDependencyMap = null;
            bool initialGraphExecute = false;

            System.Net.WebSockets.WebSocketReceiveResult result = null;

            CancellationTokenSource shutdownCts = new CancellationTokenSource();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => shutdownCts.Cancel();
            Console.CancelKeyPress += (s, e) => { shutdownCts.Cancel(); e.Cancel = false; };

            try
            {
                // Receive the first message (could be SSH details or all-in-one graph_execute)
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), System.Threading.CancellationToken.None);
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                if (string.IsNullOrWhiteSpace(message))
                {
                    _logger.LogError("Received empty message from WebSocket client.");
                    await webSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.InvalidPayloadData, "Empty message received", System.Threading.CancellationToken.None);
                    return;
                }

                _logger.LogInformation("Received initial message: {Message}", message);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;

                // Parse the message just once and fill the needed data types
                if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "graph_execute" && root.TryGetProperty("ssh", out var sshProp))
                {
                    mainSshDetails = new SshDetails
                    {
                        Host = sshProp.GetProperty("host").GetString(),
                        Port = sshProp.TryGetProperty("port", out var portProp) ? portProp.GetInt32() : 22,
                        Username = sshProp.GetProperty("username").GetString(),
                        Password = sshProp.GetProperty("password").GetString()
                    };
                    _logger.LogInformation("Parsed SSH details from all-in-one message: Host={Host}, Username={Username}", mainSshDetails.Host, mainSshDetails.Username);

                    // Check for nodes in the initial message
                    if (root.TryGetProperty("nodes", out var nodesProp) && nodesProp.ValueKind == JsonValueKind.Array)
                    {
                        initialGraphExecute = true;
                        initialNodes = JsonSerializer.Deserialize<Node[]>(nodesProp.GetRawText(), options);
                        initialDependencyMap = initialNodes.ToDictionary(
                            n => n.Id,
                            n => n.Dependencies?.ToList() ?? new List<string>()
                        );
                    }
                    _logger.LogInformation("Initial graph execution detected with {Count} nodes, initialgraph {initialGraphExecute}.", initialNodes?.Length ?? 0, initialGraphExecute);
                }

                if (mainSshDetails == null || string.IsNullOrEmpty(mainSshDetails.Host) || string.IsNullOrEmpty(mainSshDetails.Username) || string.IsNullOrEmpty(mainSshDetails.Password))
                {
                    throw new ArgumentException("Invalid SSH details. Host, username, and password must be provided.");
                }

                _logger.LogInformation("Attempting to connect to SSH server at {Host} with username {Username}", mainSshDetails.Host, mainSshDetails.Username);

                using (var sshClient = new SshClient(mainSshDetails.Host, mainSshDetails.Username, mainSshDetails.Password))
                {
                    sshClient.Connect();
                    _logger.LogInformation("SSH connection established to {Host}.", mainSshDetails.Host);

                    var shellStream = sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024);
                    _logger.LogInformation("SSH shell stream created.");

                    // Start reading from the shell stream in a background task
                    var shellTask = Task.Run(async () =>
                    {
                        var sshBuffer = new byte[1024];
                        while (sshClient.IsConnected && !shutdownCts.IsCancellationRequested)
                        {
                            try
                            {
                                var bytesRead = shellStream.Read(sshBuffer, 0, sshBuffer.Length);
                                if (bytesRead > 0)
                                {
                                    var sshOutput = Encoding.UTF8.GetString(sshBuffer, 0, bytesRead);
                                    _logger.LogDebug("Received data from SSH: {Output}", sshOutput);

                                    var responseBytes = Encoding.UTF8.GetBytes(sshOutput);
                                    await webSocket.SendAsync(new ArraySegment<byte>(responseBytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error while reading from SSH shell stream.");
                                break;
                            }
                        }
                    }, shutdownCts.Token);

                    try
                    {
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

                            // Pass already parsed initialNodes and initialDependencyMap
                            _ = ExecuteGraphAsync(initialNodes, initialDependencyMap, webSocket, mainSshDetails, _currentExecutionId);

                            var stateJson = JsonSerializer.Serialize(new { executionId = _currentExecutionId, nodes = _nodeStates.Values });
                            await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(stateJson)), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception during initial node execution setup.");
                    }

                    // Handle WebSocket input and forward it to the SSH shell
                    while (webSocket.State == System.Net.WebSockets.WebSocketState.Open && (result == null || !result.CloseStatus.HasValue) && !shutdownCts.IsCancellationRequested)
                    {
                        _logger.LogInformation("Waiting for WebSocket input...");
                        try
                        {
                            var receiveTask = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), shutdownCts.Token);
                            result = await receiveTask;
                            var wsMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            if (string.IsNullOrWhiteSpace(wsMessage))
                            {
                                continue;
                            }

                            _logger.LogInformation("Received data from WebSocket client: {Message}", wsMessage);

                            // Parse the message only once
                            var innerDoc = JsonDocument.Parse(wsMessage);
                            var innerRoot = innerDoc.RootElement;

                            string type = innerRoot.TryGetProperty("type", out var typeProp2) ? typeProp2.GetString() : null;

                            if (type == "graph_execute")
                            {
                                // Deserialize GraphExecuteMessage only once
                                var graphMsg = JsonSerializer.Deserialize<GraphExecuteMessage>(wsMessage);
                                if (graphMsg?.Nodes != null)
                                {
                                    _currentExecutionId = Guid.NewGuid().ToString();

                                    var dependencyMap = graphMsg.Nodes.ToDictionary(
                                        n => n.Id,
                                        n => n.Dependencies?.ToList() ?? new List<string>()
                                    );

                                    foreach (var node in graphMsg.Nodes)
                                    {
                                        _logger.LogInformation("Initializing node {NodeId} with arguments: {Args}, inputs: {Inputs}, parallel: {Parallel}, times: {Times}, dependencies: {Dependencies}", node.Id, node.Args, node.Inputs, node.Parallel, node.Times, string.Join(", ", node.Dependencies ?? Enumerable.Empty<string>()));
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

                                    // Pass already parsed nodes and dependencyMap
                                    _ = ExecuteGraphAsync(graphMsg.Nodes, dependencyMap, webSocket, mainSshDetails, _currentExecutionId);

                                    var stateJson = JsonSerializer.Serialize(new { executionId = _currentExecutionId, nodes = _nodeStates.Values });
                                    await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(stateJson)), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                                }
                            }
                            else if (type == "graph_restore")
                            {
                                var restoreMsg = JsonSerializer.Deserialize<GraphRestoreMessage>(wsMessage);
                                if (restoreMsg?.NodeStates != null)
                                {
                                    foreach (var state in restoreMsg.NodeStates)
                                    {
                                        _nodeStates[state.NodeId] = state;
                                    }
                                    await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"restore_ack\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                                }
                            }
                            else if (type == "pause")
                            {
                                _isPaused = true;
                                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"paused\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                            }
                            else if (type == "resume")
                            {
                                _isPaused = false;
                                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"resumed\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                            }
                            else if (type == "save_state")
                            {
                                if (!string.IsNullOrEmpty(_currentExecutionId))
                                    File.WriteAllText(GetPersistenceFile(_currentExecutionId), JsonSerializer.Serialize(_nodeStates.Values));
                                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"saved\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                            }
                            else if (type == "load_state")
                            {
                                var execId = innerRoot.TryGetProperty("executionId", out var execIdProp)
                                    ? execIdProp.GetString()
                                    : null;
                                if (!string.IsNullOrEmpty(execId) && File.Exists(GetPersistenceFile(execId)))
                                {
                                    var json = File.ReadAllText(GetPersistenceFile(execId));
                                    var states = JsonSerializer.Deserialize<List<NodeExecutionState>>(json);
                                    if (states != null)
                                    {
                                        foreach (var state in states)
                                            _nodeStates[state.NodeId] = state;
                                    }
                                    _currentExecutionId = execId;
                                    await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"loaded\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                                }
                            }
                            else if (type == "command" && innerRoot.TryGetProperty("command", out var cmdProp) && !string.IsNullOrEmpty(cmdProp.GetString()))
                            {
                                var command = cmdProp.GetString();
                                _logger.LogInformation("Executing command: {Command}", command);
                                var cmd = sshClient.RunCommand(command);
                                var output = cmd.Result + cmd.Error;
                                var responseBytes = Encoding.UTF8.GetBytes(output);
                                await webSocket.SendAsync(new ArraySegment<byte>(responseBytes), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                            }
                            else if (type == "schedule_recurring")
                            {
                                var intervalSeconds = innerRoot.GetProperty("intervalSeconds").GetInt32();
                                var nodes = innerRoot.GetProperty("nodes").Deserialize<Node[]>();
                                var recurringId = Guid.NewGuid().ToString();
                                var dependencyMap = nodes.ToDictionary(n => n.Id, n => n.Dependencies?.ToList() ?? new List<string>());
                                var cts = new CancellationTokenSource();
                                _recurringExecutions[recurringId] = cts;

                                _ = Task.Run(async () =>
                                {
                                    while (!cts.Token.IsCancellationRequested)
                                    {
                                        var execId = Guid.NewGuid().ToString();
                                        foreach (var node in nodes)
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
                                        await ExecuteGraphAsync(nodes, dependencyMap, webSocket, mainSshDetails, execId);
                                        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cts.Token);
                                    }
                                }, cts.Token);

                                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                                    JsonSerializer.Serialize(new { type = "recurring_scheduled", recurringId }))),
                                    System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                            }
                            else if (type == "cancel_recurring")
                            {
                                var recurringId = innerRoot.GetProperty("recurringId").GetString();
                                if (!string.IsNullOrEmpty(recurringId) && _recurringExecutions.TryRemove(recurringId, out var cts))
                                {
                                    cts.Cancel();
                                    await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                                        JsonSerializer.Serialize(new { type = "recurring_cancelled", recurringId }))),
                                        System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                                }
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

        // Graph execution with dependencies, parallelism, error handling, pause/resume, progress, timeouts/retries
        private async Task ExecuteGraphAsync(Node[] nodes, Dictionary<string, List<string>> dependencyMap, System.Net.WebSockets.WebSocket webSocket, SshDetails mainSshDetails, string executionId)
        {
            var nodeDict = nodes.ToDictionary(n => n.Id, n => n);
            var runningTasks = new List<Task>();
            _logger.LogInformation("Starting graph execution with {NodeCount} nodes.", nodes.Length);
            while (_nodeStates.Values.Any(s => s.Status == "pending" || s.Status == "running"))
            {
                // Pause support: Only pauses scheduling of new nodes, not currently running nodes.
                while (_isPaused)
                {
                    await Task.Delay(500);
                }

                // Find ready nodes (dependencies completed)
                var readyNodes = _nodeStates.Values
                    .Where(s => s.Status == "pending" &&
                                (s.Dependencies == null || s.Dependencies.All(d => _nodeStates.ContainsKey(d) && _nodeStates[d].Status == "completed")))
                    .ToList();

                // Prevent rescheduling nodes that are already running
                foreach (var state in readyNodes)
                {
                    // Only schedule if not already running
                    if (state.Status == "running")
                        continue;

                    state.Status = "running";
                    state.StartTime = DateTime.UtcNow;
                    await SendProgressAsync(webSocket);

                    var node = nodeDict[state.NodeId];
                    _logger.LogInformation("Executing node {NodeId} with args: {Args}, inputs: {Inputs}, parallel: {Parallel}, times: {Times}, dependencies: {Dependencies}",
                        state.NodeId, node.Args, node.Inputs, node.Parallel, node.Times, string.Join(", ", node.Dependencies ?? Enumerable.Empty<string>()));

                    SshDetails nodeSshDetails = null;
                    try
                    {
                        // Only treat inputs as SSH details if it contains the expected properties
                        if (node.Inputs is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
                        {
                            // Avoid deserializing again if already a SshDetails object
                            if (elem.TryGetProperty("host", out var hostProp) &&
                                elem.TryGetProperty("username", out var userProp) &&
                                elem.TryGetProperty("password", out var passProp))
                            {
                                nodeSshDetails = new SshDetails
                                {
                                    Host = hostProp.GetString(),
                                    Username = userProp.GetString(),
                                    Password = passProp.GetString(),
                                    Port = elem.TryGetProperty("port", out var portProp) && portProp.ValueKind == JsonValueKind.Number
                                        ? portProp.GetInt32()
                                        : 22
                                };
                            }
                        }
                        else if (node.Inputs is SshDetails details)
                        {
                            nodeSshDetails = details;
                        }
                        else if (node.Inputs is Dictionary<string, object> dict &&
                                 dict.ContainsKey("host") && dict.ContainsKey("username") && dict.ContainsKey("password"))
                        {
                            nodeSshDetails = new SshDetails
                            {
                                Host = dict["host"]?.ToString(),
                                Username = dict["username"]?.ToString(),
                                Password = dict["password"]?.ToString(),
                                Port = dict.ContainsKey("port") && dict["port"] != null ? Convert.ToInt32(dict["port"]) : 22
                            };
                        }
                        // If node.Inputs is a JsonElement but not an object, ignore
                    }
                    catch
                    {
                        nodeSshDetails = null;
                    }
                    nodeSshDetails ??= mainSshDetails;

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(state.TimeoutSeconds));
                            using (var nodeSshClient = new SshClient(nodeSshDetails.Host, nodeSshDetails.Username, nodeSshDetails.Password))
                            {
                                nodeSshClient.Connect();
                                _logger.LogInformation(
                                    "SSH connection established for node {NodeId} at {Host}.",
                                    state.NodeId, nodeSshDetails.Host);
                                for (int i = 0; i < state.Times; i++)
                                {
                                    if (!string.IsNullOrWhiteSpace(node.Args))
                                    {
                                        var cmd = nodeSshClient.RunCommand(node.Args);
                                        state.Outputs = cmd.Result;
                                        if (!string.IsNullOrEmpty(cmd.Error))
                                            throw new Exception(cmd.Error);
                                    }
                                    else
                                    {
                                        var errMsg = $"Node {state.NodeId} has null or empty Args (command).";
                                        _logger.LogError(errMsg);
                                        throw new ArgumentException(errMsg);
                                    }
                                }
                                nodeSshClient.Disconnect();
                            }
                            state.Status = "completed";
                            state.EndTime = DateTime.UtcNow;
                            state.Error = null;
                            _logger.LogInformation("cmd result: {Outputs}", state.Outputs);

                            // --- NEW LOGIC: Pass output to dependent nodes' Inputs ---
                            if (dependencyMap.TryGetValue(state.NodeId, out var dependents))
                            {
                                foreach (var depNodeId in dependencyMap
                                    .Where(kvp => kvp.Value.Contains(state.NodeId))
                                    .Select(kvp => kvp.Key))
                                {
                                    if (_nodeStates.TryGetValue(depNodeId, out var depState))
                                    {
                                        // Always append the parent's output to the child node's Inputs["outputs"] array
                                        // If Inputs is a JsonElement, convert to Dictionary for manipulation
                                        Dictionary<string, object> inputsDict = null;
                                        if (depState.Inputs is JsonElement je && je.ValueKind == JsonValueKind.Object)
                                        {
                                            inputsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
                                        }
                                        else if (depState.Inputs is Dictionary<string, object> dict)
                                        {
                                            inputsDict = dict;
                                        }
                                        else
                                        {
                                            // If Inputs is not set or not a dictionary, initialize it as a new dictionary
                                            inputsDict = new Dictionary<string, object>();
                                        }

                                        // Ensure SSH credentials are preserved (if present)
                                        if (inputsDict.Count == 0 && depState.Inputs != null)
                                        {
                                            // Try to copy known SSH fields from the original Inputs
                                            var sshFields = new[] { "host", "username", "password", "port" };
                                            if (depState.Inputs is SshDetails ssh)
                                            {
                                                inputsDict["host"] = ssh.Host;
                                                inputsDict["username"] = ssh.Username;
                                                inputsDict["password"] = ssh.Password;
                                                inputsDict["port"] = ssh.Port;
                                            }
                                            else if (depState.Inputs is JsonElement sshElem && sshElem.ValueKind == JsonValueKind.Object)
                                            {
                                                foreach (var field in sshFields)
                                                {
                                                    if (sshElem.TryGetProperty(field, out var val))
                                                    {
                                                        inputsDict[field] = val.ValueKind == JsonValueKind.Number ? val.GetInt32() : val.GetString();
                                                    }
                                                }
                                            }
                                        }

                                        // Add or append to the "outputs" array
                                        if (!inputsDict.TryGetValue("outputs", out var outputsObj) || outputsObj == null)
                                        {
                                            inputsDict["outputs"] = new List<object> { state.Outputs };
                                        }
                                        else if (outputsObj is List<object> outputsList)
                                        {
                                            outputsList.Add(state.Outputs);
                                        }
                                        else if (outputsObj is JsonElement outputsElem && outputsElem.ValueKind == JsonValueKind.Array)
                                        {
                                            var list = JsonSerializer.Deserialize<List<object>>(outputsElem.GetRawText());
                                            list.Add(state.Outputs);
                                            inputsDict["outputs"] = list;
                                        }
                                        else
                                        {
                                            // If "outputs" exists but is not a list, replace it with a new list
                                            inputsDict["outputs"] = new List<object> { outputsObj, state.Outputs };
                                        }

                                        depState.Inputs = inputsDict;
                                    }
                                }
                            }
                            // --- END NEW LOGIC ---
                        }
                        catch (Exception ex)
                        {
                            state.RetryCount++;
                            state.Error = ex.Message;
                            if (state.RetryCount <= state.MaxRetries)
                            {
                                state.Status = "pending"; // Retry
                            }
                            else
                            {
                                state.Status = "error";
                                state.EndTime = DateTime.UtcNow;
                            }
                        }
                        // Send node finished message to frontend
                        var nodeFinishedMsg = new
                        {
                            type = "node_finished",
                            nodeId = state.NodeId,
                            status = state.Status,
                            outputs = state.Outputs,
                            error = state.Error,
                            startTime = state.StartTime,
                            endTime = state.EndTime
                        };
                        var nodeFinishedJson = JsonSerializer.Serialize(nodeFinishedMsg);
                        await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(nodeFinishedJson)), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);

                        await SendProgressAsync(webSocket);
                        File.WriteAllText(GetPersistenceFile(executionId), JsonSerializer.Serialize(_nodeStates.Values));
                    });
                    if (state.Parallel)
                        runningTasks.Add(task);
                    else
                        await task;
                }

                runningTasks.RemoveAll(t => t.IsCompleted);
                await Task.Delay(200);
            }
            await SendProgressAsync(webSocket);

            // Send summary report to frontend after all nodes have finished
            var summary = new
            {
                type = "summary_report",
                executionId = executionId,
                nodes = _nodeStates.Values.Select(s => new
                {
                    nodeId = s.NodeId,
                    status = s.Status,
                    outputs = s.Outputs,
                    error = s.Error,
                    startTime = s.StartTime,
                    endTime = s.EndTime,
                    retryCount = s.RetryCount
                }).ToList(),
                startedAt = _nodeStates.Values.Min(s => s.StartTime),
                finishedAt = _nodeStates.Values.Max(s => s.EndTime)
            };
            var summaryJson = JsonSerializer.Serialize(summary);
            await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(summaryJson)), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
        }

        private async Task SendProgressAsync(System.Net.WebSockets.WebSocket webSocket)
        {
            var progress = new
            {
                type = "progress",
                nodes = _nodeStates.Values.Select(s => new
                {
                    nodeId = s.NodeId,
                    status = s.Status,
                    outputs = s.Outputs,
                    error = s.Error,
                    startTime = s.StartTime,
                    endTime = s.EndTime,
                    retryCount = s.RetryCount
                }).ToList()
            };
            var progressJson = JsonSerializer.Serialize(progress);
            await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(progressJson)), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
        }

        private string GetPersistenceFile(string executionId) => $"{PersistenceFile}.{executionId}.json";

        // Node execution state
        private class NodeExecutionState
        {
            public string NodeId { get; set; }
            public string Status { get; set; }
            public object Inputs { get; set; }
            public string Args { get; set; }
            public bool Parallel { get; set; }
            public int Times { get; set; }
            public IEnumerable<string> Dependencies { get; set; }
            public int RetryCount { get; set; }
            public int MaxRetries { get; set; }
            public int TimeoutSeconds { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string Error { get; set; }
            public object Outputs { get; set; }
        }
        private class ExecuteNodeWrapper
        {
            public string Type { get; set; }
            public ExecuteNodeData Data { get; set; }
        }
        private class ExecuteNodeData
        {
            public SshDetails SshDetails { get; set; }
            public int NodeId { get; set; }
            public string Command { get; set; }
        }

        // SSH connection details
        private class SshDetails
        {
            public string Host { get; set; }
            public int Port { get; set; } // Optional, defaults to 22 if not set
            public string Username { get; set; }
            public string Password { get; set; }
        }
        private class SshDetailsWrapper
        {
            // Used for legacy/compatibility: expects { "type": "...", "data": { ...ssh fields... } }
            public string Type { get; set; }
            public SshDetails Data { get; set; }
        }

        // Base message structure
        private class BaseMessage
        {
            public string Type { get; set; }
            public string Command { get; set; }
        }

        // Graph execution message
        private class GraphExecuteMessage : BaseMessage
        {
            public Node[] Nodes { get; set; }
        }

        // Graph restore message
        private class GraphRestoreMessage : BaseMessage
        {
            public NodeExecutionState[] NodeStates { get; set; }
        }

        // Node definition
        private class Node
        {
            public string Id { get; set; }
            public object Inputs { get; set; }
            public string Args { get; set; }
            public bool Parallel { get; set; }
            public int Times { get; set; }
            public IEnumerable<string> Dependencies { get; set; }
            public int MaxRetries { get; set; }
            public int TimeoutSeconds { get; set; }
        }
    }
}