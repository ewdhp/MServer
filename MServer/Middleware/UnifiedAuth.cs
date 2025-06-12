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

            SshDetails mainSshDetails = null; // Store main SSH details

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
                mainSshDetails = JsonSerializer.Deserialize<SshDetails>(message, options);

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

                            var baseMsg = JsonSerializer.Deserialize<BaseMessage>(message);
                            if (baseMsg?.Type == "graph_execute")
                            {
                                var graphMsg = JsonSerializer.Deserialize<GraphExecuteMessage>(message);
                                if (graphMsg?.Nodes != null)
                                {
                                    // Build dependency map
                                    var dependencyMap = new Dictionary<string, List<string>>();
                                    foreach (var node in graphMsg.Nodes)
                                    {
                                        dependencyMap[node.Id] = node.Dependencies?.ToList() ?? new List<string>();
                                    }

                                    // Track and initialize node states
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

                                    // Start execution, pass mainSshDetails
                                    _ = ExecuteGraphAsync(graphMsg.Nodes, dependencyMap, webSocket, mainSshDetails);

                                    // Send initial state back
                                    var stateJson = JsonSerializer.Serialize(_nodeStates.Values);
                                    await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(stateJson)), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                                }
                            }
                            else if (baseMsg?.Type == "graph_restore")
                            {
                                var restoreMsg = JsonSerializer.Deserialize<GraphRestoreMessage>(message);
                                if (restoreMsg?.NodeStates != null)
                                {
                                    foreach (var state in restoreMsg.NodeStates)
                                    {
                                        _nodeStates[state.NodeId] = state;
                                    }
                                    await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"restore_ack\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                                }
                            }
                            else if (baseMsg?.Type == "pause")
                            {
                                _isPaused = true;
                                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"paused\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                            }
                            else if (baseMsg?.Type == "resume")
                            {
                                _isPaused = false;
                                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"resumed\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                            }
                            else if (baseMsg?.Type == "save_state")
                            {
                                // Persist state to disk (simple demo)
                                File.WriteAllText(PersistenceFile, JsonSerializer.Serialize(_nodeStates.Values));
                                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"saved\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                            }
                            else if (baseMsg?.Type == "load_state")
                            {
                                // Load state from disk (simple demo)
                                if (File.Exists(PersistenceFile))
                                {
                                    var json = File.ReadAllText(PersistenceFile);
                                    var states = JsonSerializer.Deserialize<List<NodeExecutionState>>(json);
                                    if (states != null)
                                    {
                                        foreach (var state in states)
                                            _nodeStates[state.NodeId] = state;
                                    }
                                    await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"loaded\"}")), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                                }
                            }
                            else if (baseMsg?.Type == "command" && !string.IsNullOrEmpty(baseMsg.Command))
                            {
                                _logger.LogInformation("Executing command: {Command}", baseMsg.Command);
                                var cmd = sshClient.RunCommand(baseMsg.Command);
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

        // Graph execution with dependencies, parallelism, error handling, pause/resume, progress, timeouts/retries
        private async Task ExecuteGraphAsync(Node[] nodes, Dictionary<string, List<string>> dependencyMap, System.Net.WebSockets.WebSocket webSocket, SshDetails mainSshDetails)
        {
            var nodeDict = nodes.ToDictionary(n => n.Id, n => n);
            var runningTasks = new List<Task>();

            while (_nodeStates.Values.Any(s => s.Status == "pending" || s.Status == "running"))
            {
                // Pause support
                while (_isPaused)
                {
                    await Task.Delay(500);
                }

                // Find ready nodes (dependencies completed)
                var readyNodes = _nodeStates.Values
                    .Where(s => s.Status == "pending" &&
                                (s.Dependencies == null || s.Dependencies.All(d => _nodeStates.ContainsKey(d) && _nodeStates[d].Status == "completed")))
                    .ToList();

                foreach (var state in readyNodes)
                {
                    state.Status = "running";
                    state.StartTime = DateTime.UtcNow;
                    await SendProgressAsync(webSocket);

                    var node = nodeDict[state.NodeId];

                    // Robustly extract per-node SSH credentials from Inputs, or use main credentials
                    SshDetails nodeSshDetails = null;
                    try
                    {
                        if (node.Inputs is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
                        {
                            nodeSshDetails = JsonSerializer.Deserialize<SshDetails>(elem.GetRawText());
                        }
                        else if (node.Inputs is SshDetails details)
                        {
                            nodeSshDetails = details;
                        }
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
                                        await Task.Delay(1000, cts.Token);
                                    }
                                }
                                nodeSshClient.Disconnect();
                            }
                            state.Status = "completed";
                            state.EndTime = DateTime.UtcNow;
                            state.Error = null;
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
                        await SendProgressAsync(webSocket);
                        File.WriteAllText(PersistenceFile, JsonSerializer.Serialize(_nodeStates.Values));
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
        }

        private async Task SendProgressAsync(System.Net.WebSockets.WebSocket webSocket)
        {
            var progress = new
            {
                type = "progress",
                nodes = _nodeStates.Values.Select(s => new { s.NodeId, s.Status, s.Error, s.Outputs, s.StartTime, s.EndTime })
            };
            var json = JsonSerializer.Serialize(progress);
            await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
        }

        private class SshDetails
        {
            public string Host { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        private class BaseMessage
        {
            public string Type { get; set; }
            public string Command { get; set; }
        }

        private class GraphExecuteMessage
        {
            public string Type { get; set; }
            public Node[] Nodes { get; set; }
        }

        private class GraphRestoreMessage
        {
            public string Type { get; set; }
            public NodeExecutionState[] NodeStates { get; set; }
        }

        private class Node
        {
            public string Id { get; set; }
            public string Args { get; set; }
            public bool Parallel { get; set; }
            public int Times { get; set; }
            public string[] Dependencies { get; set; }
            public object Inputs { get; set; }
            public int TimeoutSeconds { get; set; }
            public int MaxRetries { get; set; }
        }

        private class NodeExecutionState
        {
            public string NodeId { get; set; }
            public string Status { get; set; }
            public object Inputs { get; set; }
            public string Args { get; set; }
            public bool Parallel { get; set; }
            public int Times { get; set; }
            public string[] Dependencies { get; set; }
            public int RetryCount { get; set; }
            public int MaxRetries { get; set; }
            public int TimeoutSeconds { get; set; }
            public string Error { get; set; }
            public string Outputs { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }
    }
}
/**

{
  "host": "example.com",
  "username": "user",
  "password": "pass"
}

{
  "type": "graph_execute",
  "nodes": [
    {
      "id": "node1",
      "args": "ls -l",
      "parallel": true,
      "times": 1,
      "dependencies": [],
      "inputs": {
        "host": "example.com",
        "username": "user",
        "password": "pass"
      },
      "timeoutSeconds": 30,
      "maxRetries": 3
    },
    {
      "id": "node2",
      "args": "echo Hello",
      "parallel": false,
      "times": 1,
      "dependencies": ["node1"],
      "inputs": {
        "host": "example.com",
        "username": "user",
        "password": "pass"
      },
      "timeoutSeconds": 30,
      "maxRetries": 3
    }
  ]
}**/