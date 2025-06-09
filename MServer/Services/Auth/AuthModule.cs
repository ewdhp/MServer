using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace MServer.Services.Auth
{
    public class AuthModule
    {
        private readonly TokenValidationService tokenValidationService;
        private readonly TokenManagementService tokenManagementService;
        private readonly RBACService rbacService;
        private readonly AuditLoggingService auditLoggingService;
        private readonly ErrorHandlingService errorHandlingService;

        private readonly IConfiguration configuration;

        public AuthModule(IConfiguration configuration)
        {
            this.configuration = configuration;

            // Initialize services in the correct order
            auditLoggingService = new AuditLoggingService();
            errorHandlingService = new ErrorHandlingService();
            tokenValidationService = new
            TokenValidationService(configuration);
            tokenManagementService = new TokenManagementService
            (auditLoggingService,
            configuration["TokenManagement:SecretKey"]);
            rbacService = new RBACService();
        }

        public async Task StartWebSocketServer(string url)
        {
            var httpListener = new HttpListener();
            httpListener.Prefixes.Add(url);
            httpListener.Start();
            Console.WriteLine("WebSocket Server started...");

            while (true)
            {
                var context = await httpListener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context
                    .AcceptWebSocketAsync(null);
                    var token = context.Request
                    .Headers["Authorization"];
                    await HandleWebSocketConnection
                    (wsContext.WebSocket, token);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }

        private async Task HandleWebSocketConnection
        (WebSocket socket, string token)
        {
            var workflow = new WebSocketWorkflow(
                tokenValidationService,
                tokenManagementService,
                rbacService,
                auditLoggingService,
                errorHandlingService);

            await workflow.HandleConnection(socket, token);
        }
    }

    internal class WebSocketWorkflow
    {
        private readonly TokenValidationService tokenValidationService;
        private readonly TokenManagementService tokenManagementService;
        private readonly RBACService rbacService;
        private readonly AuditLoggingService auditLoggingService;
        private readonly ErrorHandlingService errorHandlingService;

        public WebSocketWorkflow(
            TokenValidationService tokenValidationService,
            TokenManagementService tokenManagementService,
            RBACService rbacService,
            AuditLoggingService auditLoggingService,
            ErrorHandlingService errorHandlingService)
        {
            this.tokenValidationService = tokenValidationService;
            this.tokenManagementService = tokenManagementService;
            this.rbacService = rbacService;
            this.auditLoggingService = auditLoggingService;
            this.errorHandlingService = errorHandlingService;
        }

        public async Task HandleConnection(WebSocket socket, string token)
        {
            var validationResult = tokenValidationService
            .ValidateToken(token);
            if (validationResult.IsValid)
            {
                Console.WriteLine
                ("Token validated. Handling WebSocket connection...");
                var buffer = new byte[1024 * 4];
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync
                    (new ArraySegment<byte>(buffer),
                    System.Threading.CancellationToken.None);
                    Console.WriteLine
                    ($"Received: {System.Text.Encoding.UTF8
                    .GetString(buffer, 0, result.Count)}");
                } while (!result.CloseStatus.HasValue);

                await socket.CloseAsync(result.CloseStatus.Value,
                result.CloseStatusDescription,
                System.Threading.CancellationToken.None);
            }
            else
            {
                Console.WriteLine
                ("Invalid token. Closing WebSocket connection...");
                await socket.CloseAsync
                (WebSocketCloseStatus.PolicyViolation,
                "Invalid token", System.Threading
                .CancellationToken.None);
            }
        }
    }
}