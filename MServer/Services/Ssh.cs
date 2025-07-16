using System;
using System.Threading.Tasks;
using Renci.SshNet;
using MServer.Models;

namespace MServer.Services
{
    public class SshService
    {
        public async Task<string> ExecuteCommandAsync
        (SshDetails sshDetails, string command)
        {
            if (sshDetails == null)
                throw new ArgumentNullException
                (nameof(sshDetails));

            Console.WriteLine($"[SSH DEBUG] Attempting connection to {sshDetails.Host}:{sshDetails.Port} with user {sshDetails.Username}");

            try
            {
                using var client = new SshClient
                    (sshDetails.Host, sshDetails.Port,
                    sshDetails.Username, sshDetails.Password);

                // Set connection timeout
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                
                Console.WriteLine($"[SSH DEBUG] Connecting to SSH server...");
                client.Connect();
                Console.WriteLine($"[SSH DEBUG] SSH connection established successfully");
                
                var cmd = client.CreateCommand(command);
                Console.WriteLine($"[SSH DEBUG] Executing command: {command}");
                
                var asyncResult = await
                Task.Factory.StartNew(() => cmd.Execute());
                
                client.Disconnect();
                Console.WriteLine($"[SSH DEBUG] SSH connection closed");

                // Log both stdout and stderr for debugging
                Console.WriteLine($"[SSH DEBUG] STDOUT: {cmd.Result}");
                Console.WriteLine($"[SSH DEBUG] STDERR: {cmd.Error}");

                // Return both for debugging
                return string.IsNullOrWhiteSpace(cmd.Result) ? cmd.Error : cmd.Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SSH ERROR] Failed to connect/execute: {ex.Message}");
                Console.WriteLine($"[SSH ERROR] Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[SSH ERROR] Inner exception: {ex.InnerException.Message}");
                }
                throw new Exception($"SSH execution failed: {ex.Message}", ex);
            }
        }
    }
}