using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MServer.Models;
using MServer.Services;

public class GraphExecutor(SshService sshService)
{
    // Directory where node outputs are stored
    private readonly string _outputDir = "/data";

    // Service to execute commands over SSH
    // This should be injected via dependency injection
    // and configured to connect to the remote server
    // where the scripts will be executed.
    private readonly SshService _sshService = sshService;

    // Lock object to ensure thread-safe file access
    // This prevents multiple threads from writing to 
    // the same file simultaneously,which could
    // corrupt the output data.
    private static readonly Lock FileLock = new();


    // Writes the output of a node to a JSON file
    // The output is serialized to JSON format 
    // and saved to a file named <nodeId>_output.json 
    // in the output directory.

    public async Task ExecuteGraphAsync(
        Node[] nodes,
        Func<execState, Task> onProgress,
        SshDetails mainSshDetails,
        CancellationToken cancellationToken,
        ConcurrentDictionary<string, execState> sst = null,
        Func<bool> isPaused = null
    )
    {
        var nst = sst ?? new
        ConcurrentDictionary<string, execState>(
            nodes.ToDictionary(
                n => (string)n.Id,
                n => new execState
                {
                    NodeId = (string)n.Id,
                    Status = "pending",
                    Inputs = n.Inputs,
                    Args = n.Args is IEnumerable<object> arr ?
                        string.Join(" ", arr.Select
                        (a => a?.ToString() ?? "")) :
                        n.Args?.ToString(),
                    Parallel = n.Parallel,
                    Times = n.Times,
                    Dependencies = n.Dependencies,
                    RetryCount = 0,
                    MaxRetries = n.MaxRetries,
                    TimeoutSeconds = n.TimeoutSeconds
                }));

        while (nst.Values.Any(s => s.Status == "pending" ||
                s.Status == "running"))
        {
            foreach (var node in nodes)
            {
                var state = nst[(string)node.Id];
                if (state.Status == "completed" ||
                    state.Status == "success" ||
                    state.Status == "failed")
                    continue;

                while (isPaused != null && isPaused())
                {
                    await Task.Delay(200, cancellationToken);
                }

                if (state.Status == "pending" &&
                    (state.Dependencies == null ||
                    state.Dependencies.All
                    (d => nst[d].Status == "completed" ||
                    nst[d].Status == "success")))
                {
                    state.Status = "running";
                    state.StartTime = DateTime.UtcNow;

                    // This runs the script in a separate task to avoid 
                    // blocking the main thread and allows for 
                    // parallel execution.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            string inDataArgs = "";
                            if (node.Inputs is Dictionary
                               <string, object> dict &&
                                dict.TryGetValue
                                ("data", out var val))
                            {
                                if (val is string s && s == "$parent.output" &&
                                    node.Dependencies != null &&
                                    node.Dependencies.Count != 0
                                 )
                                {
                                    var parentId = (string)node.Dependencies.First();
                                    if (nst.TryGetValue(parentId, out var parentState) &&
                                        parentState.Outputs != null
                                       )
                                        inDataArgs = parentState.Outputs.ToString();
                                    else
                                        inDataArgs = "";
                                }
                                else
                                    inDataArgs = val?.ToString() ?? "";
                            }

                            if (string.IsNullOrEmpty(inDataArgs) &&
                               node.Id.ToString() == "main1")
                                inDataArgs = "main_start";

                            Console.WriteLine(
                                "[DEBUG] Node '{0}' inDataArgs: '{1}'",
                                node.Id,
                                inDataArgs
                            );
                            Console.WriteLine(
                                "[DEBUG] Node '{0}' original node.Args: {1}",
                                node.Id,
                                JsonSerializer.Serialize(node.Args)
                            );

                            string[] resArgs;
                            if (node.Args is IEnumerable<object> arr)
                                resArgs = [.. arr.Select(a => a is string str &&
                                    str == "$inputs.data"?
                                    inDataArgs :a?.ToString() ?? ""
                                    )
                                ];
                            else if (node.Args is string s)
                                resArgs = s == "$inputs.data" ?
                                [inDataArgs] : [s];
                            else
                                resArgs = Array.Empty<string>();


                            Console.WriteLine(
                            "[DEBUG] Node '{0}' resolved args: [{1}]",
                            node.Id,
                            string.Join(", ", resArgs)
                            );

                            state.Args = resArgs.Length > 0 &&
                            resArgs.Any(arg => !string.IsNullOrWhiteSpace(arg)) ?
                                string.Join(" ", resArgs) : "";

                            // If script is not an absolute path, resolve to ../node-scripts/
                            string scriptPath = node.Script;
                            if (!string.IsNullOrEmpty(scriptPath) &&
                                !Path.IsPathRooted(scriptPath))
                            {
                                scriptPath = Path.Combine
                                ("..", "node-scripts", scriptPath);
                            }

                            // Print the command to be executed
                            Console.WriteLine(
                                "[DEBUG] Node '{0}' executing: sh {1} {2}",
                                node.Id,
                                scriptPath,
                                string.Join(" ", resArgs.Select(a => $"\"{a}\""))
                            );

                            if (!File.Exists(scriptPath))
                            {
                                Console.WriteLine(
                                    "[ERROR] Node '{0}': Script file not found: {1}",
                                    node.Id, Path.GetFullPath(scriptPath)
                                );
                                state.Error = $"Script file not found: " +
                                $"{Path.GetFullPath(scriptPath)}";
                                state.Status = "failed";
                                state.EndTime = DateTime.UtcNow;
                                if (onProgress != null)
                                    await onProgress(state);
                                return;
                            }
                            else
                            {
                                var fileInfo = new FileInfo(scriptPath);
                                Console.WriteLine(
                                    "[DEBUG] Node '{0}': Script file found: {1}, Size: {2}, " +
                                    "Permissions: {3}",
                                    node.Id,
                                    Path.GetFullPath(scriptPath),
                                    fileInfo.Length,
                                    fileInfo.Attributes
                                );
                            }

                            // --- DEBUG: Print the full SSH command and details ---
                            Console.WriteLine(
                                "[DEBUG] Node '{0}' SSH details: host={1}, user={2}, port={3}",
                                node.Id,
                                mainSshDetails.Host,
                                mainSshDetails.Username,
                                mainSshDetails.Port
                            );
                            Console.WriteLine(
                                "[DEBUG] Node '{0}' SSH command: sh {1} {2}",
                                node.Id,
                                scriptPath,
                                string.Join(" ", resArgs.Select(a => $"\"{a}\""))
                            );

                            // --- DEBUG: Print warning if resArgs is empty ---
                            if (resArgs.Length == 0 ||
                                resArgs.All(string.IsNullOrWhiteSpace))
                            {
                                Console.WriteLine(
                                    "[WARN] Node '{0}' is executing with NO arguments. " +
                                    "Check your input data and args mapping.",
                                    node.Id
                                );
                            }

                            string sshOutput;
                            try
                            {
                                string command =
                                    $"sh {scriptPath} " +
                                    $"{string.Join(" ", resArgs.Select(a => $"\"{a}\""))}";

                                sshOutput = await _sshService
                                .ExecuteCommandAsync(mainSshDetails, command);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    "[ERROR] Node '{0}' SSH execution failed: {1}",
                                    node.Id,
                                    ex.Message
                                );
                                sshOutput = "";
                            }
                            Console.WriteLine(
                                "[DEBUG] Node '{0}' SSH output (raw): [{1}]",
                                node.Id,
                                sshOutput
                            );

                            state.Outputs = sshOutput;
                            state.Status = "completed";
                        }
                        catch (Exception ex)
                        {
                            state.Error = ex.Message;
                            state.Status = "failed";
                            Console.WriteLine(
                                "[ERROR] Node '{0}' failed: {1}",
                                node.Id,
                                ex.Message
                            );
                        }
                        finally
                        {
                            state.EndTime = DateTime.UtcNow;
                            if (onProgress != null)
                                await onProgress(state);
                        }
                    }, cancellationToken);
                }
            }
            await Task.Delay(500, cancellationToken);
        }
    }

    public async Task<string> ExecNode(
        Node node, SshDetails sshd,
        Dictionary<string, execState> ns
        )
    {
        var resolvedArgs = ResolveArgs(node, ns);

        string scriptCmd = "";
        if (!string.IsNullOrEmpty(node.Script))
        {
            var argsJoined = string
             .Join(" ", resolvedArgs.Select(a => $"\"{a}\""));
            if (node.Script.EndsWith(".sh"))
                scriptCmd = $"sh {node.Script} {argsJoined}";
            else if (node.Script.EndsWith(".py"))
                scriptCmd = $"python3 {node.Script} {argsJoined}";
            else if (node.Script.EndsWith(".cs"))
                scriptCmd = $"dotnet run {node.Script} {argsJoined}";
            else
                scriptCmd = $"{node.Script} {argsJoined}";
        }
        else scriptCmd = string.Join(" ", resolvedArgs.Select(a => $"\"{a}\""));

        // Execute the command using the SSH service
        // This will run the script on the remote server
        // with the provided arguments.
        var result = await _sshService.ExecuteCommandAsync(sshd, scriptCmd);

        // Print the output for debugging
        Console.WriteLine($"[DEBUG] Node '{node.Id}'"+
            $" executed. Output:\n{result}");

        return result;
    }
   
    private static string[] ResolveArgs(Node node, Dictionary<string, execState> nst)
    {
        if (node.Args is string singleArg)
        {
            if (singleArg == "$inputs.data")
            {
                if (node.Inputs is Dictionary<string, object>
                dict && dict.TryGetValue("data", out var val))
                    return new[] { val?.ToString() ?? "" };
            }
            return new[] { singleArg };
        }
        else if (node.Args is IEnumerable<object> arr)
        {
            return arr.Select(arg =>
            {
                if (arg is string str && str == "$inputs.data")
                {
                    if (node.Inputs is Dictionary<string, object>
                    dict && dict.TryGetValue("data", out var val))
                        return val?.ToString() ?? "";
                }
                return arg?.ToString() ?? "";
            }).ToArray();
        }
        return Array.Empty<string>();
    }
}