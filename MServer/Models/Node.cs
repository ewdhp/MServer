using System;
using System.Collections.Generic;

namespace MServer.Models
{
    public class Node
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

    public class NodeExecutionState
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

    public class GraphExecuteMessage
    {
        public string Type { get; set; }
        public SshDetails Ssh { get; set; }
        public List<Node> Nodes { get; set; }
    }

}