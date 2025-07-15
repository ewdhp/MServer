using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MServer.Middleware.Protocols;
using MServer.Services;

namespace MServer.Middleware.Session.Handlers
{
    public class TerminalSessionHandler
    {
        private readonly WebSocketMessageHandler _wsHandler;
        private readonly ILogger _logger;

        public TerminalSessionHandler(WebSocketMessageHandler wsHandler, ILogger logger)
        {
            _wsHandler = wsHandler;
            _logger = logger;
        }

        public async Task HandleSession(WebSocket ws, JsonElement root, CancellationToken sdTkn)
        {
            var shell = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            shell.Start();

            _ = Task.Run(async () =>
            {
                while (!shell.StandardOutput.EndOfStream)
                {
                    var line = await shell.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                        await _wsHandler.SendMessageAsync(ws, new { type = "output", data = line });
                }
            });

            _ = Task.Run(async () =>
            {
                while (!shell.StandardError.EndOfStream)
                {
                    var line = await shell.StandardError.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                        await _wsHandler.SendMessageAsync(ws, new { type = "output", data = line });
                }
            });

            await _wsHandler.SendMessageAsync(ws, new { type = "connected" });

            while (ws.State == WebSocketState.Open && !sdTkn.IsCancellationRequested)
            {
                string cmdMsg = null;
                try
                {
                    cmdMsg = await _wsHandler.ReceiveMessageAsync(ws);
                }
                catch (Exception ex)
                {
                    if (sdTkn.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, "Error receiving message in command loop");
                    continue;
                }
                if (sdTkn.IsCancellationRequested)
                    break;
                if (string.IsNullOrWhiteSpace(cmdMsg))
                    continue;

                try
                {
                    var cmdDoc = JsonDocument.Parse(cmdMsg);
                    var cmdRoot = cmdDoc.RootElement;
                    var cmdType = cmdRoot.TryGetProperty("type", out var cmdTypeProp) ? cmdTypeProp.GetString() : null;

                    if (cmdType == "command" && cmdRoot.TryGetProperty("command", out var commandProp))
                    {
                        var command = commandProp.GetString();
                        _logger.LogInformation("Terminal command: {Command}", command);
                        await shell.StandardInput.WriteLineAsync(command);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing command message: {Msg}", cmdMsg);
                    await _wsHandler.SendMessageAsync(ws, new { type = "error", message = "Invalid command message format." });
                }
            }

            try
            {
                if (!shell.HasExited)
                    shell.Kill();
                shell.Dispose();
            }
            catch { }
        }
    }
}
