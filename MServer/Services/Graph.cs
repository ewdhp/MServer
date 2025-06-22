using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MServer.Models;

namespace MServer.Services
{
    public class GraphExecutor
    {
        private readonly SshService _sshService;

        public GraphExecutor(SshService sshService)
        {
            _sshService = sshService;
        }

        public async Task ExecuteGraphAsync(
            Node[] nodes,
            Dictionary<string, List<string>> dependencyMap,
            Func<NodeExecutionState, Task> onProgress,
            SshDetails mainSshDetails,
            CancellationToken cancellationToken)
        {
            var nodeStates = nodes.ToDictionary(
                n => n.Id,
                n => new NodeExecutionState
                {
                    NodeId = n.Id,
                    Status = "pending",
                    Inputs = n.Inputs,
                    Args = n.Args,
                    Parallel = n.Parallel,
                    Times = n.Times,
                    Dependencies = n.Dependencies,
                    RetryCount = 0,
                    MaxRetries = n.MaxRetries,
                    TimeoutSeconds = n.TimeoutSeconds
                });

            while (nodeStates.Values.Any
                (
                    s => s.Status == "pending" ||
                    s.Status == "running"
                ))
            {
                foreach (var node in nodes)
                {
                    var state = nodeStates[node.Id];
                    if (state.Status == "pending" &&
                        (state.Dependencies == null ||
                        state.Dependencies
                        .All(d => nodeStates[d]
                        .Status == "completed")))
                    {
                        state.Status = "running";
                        state.StartTime = DateTime.UtcNow;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var result = await _sshService
                                    .ExecuteCommandAsync
                                    (mainSshDetails, node.Args);

                                state.Outputs = result;
                                state.Status = "completed";
                            }
                            catch (Exception ex)
                            {
                                state.Error = ex.Message;
                                state.Status = "failed";
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
    }
}