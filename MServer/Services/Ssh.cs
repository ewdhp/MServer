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

            using var client = new SshClient
                (sshDetails.Host, sshDetails.Port,
                sshDetails.Username, sshDetails.Password);

            client.Connect();
            var cmd = client.CreateCommand(command);
            var asyncResult = await
            Task.Factory.StartNew(() => cmd.Execute());
            client.Disconnect();

            // Log both stdout and stderr for debugging
            Console.WriteLine($"[SSH DEBUG] STDOUT: {cmd.Result}");
            Console.WriteLine($"[SSH DEBUG] STDERR: {cmd.Error}");

            // Return both for debugging
            return string.IsNullOrWhiteSpace(cmd.Result) ? cmd.Error : cmd.Result;
        }
    }
}