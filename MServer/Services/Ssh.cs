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
            return cmd.Result;
        }
    }
}