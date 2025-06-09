using System;
using System.Text.Json;

namespace MServer.Services.Auth
{
    internal interface IErrorHandlingService
    {
        public string GenerateErrorResponse(string errorCode, string errorMessage);
        public void HandleAuthenticationFailure(Guid userId, string reason);
        public void HandleAuthorizationFailure(Guid userId, string action, string reason);
    }

    public class ErrorHandlingService : IErrorHandlingService
    {
        public ErrorHandlingService()
        {

        }
        public string GenerateErrorResponse(string errorCode, string errorMessage)
        {
            var errorResponse = new
            {
                success = false,
                error = new
                {
                    code = errorCode,
                    message = errorMessage
                }
            };
            return JsonSerializer.Serialize(errorResponse);
        }

        public void HandleAuthenticationFailure(Guid userId, string reason)
        {
            Console.WriteLine($"[Auth Failure] User ID: {userId},|" +
            " Reason: {reason}, Timestamp: {DateTime.UtcNow}");
            // Log the error securely (replace Console.WriteLine with actual logging)
        }

        public void HandleAuthorizationFailure(Guid userId, string action, string reason)
        {
            Console.WriteLine($"[Authz Failure] User ID: {userId}, " +
            "Action: {action}, Reason: {reason}, Timestamp: {DateTime.UtcNow}");
            // Log the error securely (replace Console.WriteLine with actual logging)
        }
    }
}