using System;

namespace MServer.Services.Auth
{
    public interface IAuditLoggingService
    {
        public void LogAuthenticationAttempt
        (Guid userId, bool success, object details);
        public void LogAction(
            Guid userId,
            string action,
            string result,
            object details);
    }

    public class AuditLoggingService : IAuditLoggingService
    {
        //constructor
        public AuditLoggingService()
        {
        }
        public void LogAuthenticationAttempt
        (Guid userId, bool success, object details)
        {
            Console.WriteLine($"[Authentication Attempt] User ID: {userId}," +
            "Success: {success}, Details: {details}, Timestamp: {DateTime.UtcNow}");
            // Replace Console.WriteLine with actual database or file logging
        }

        public void LogAction
        (Guid userId,
        string action,
        string result,
        object details)
        {
            Console.WriteLine($"[Action Log] User ID: {userId}," +
            "Action: {action}, Result: {result}, Details: {details}," +
            " Timestamp: {DateTime.UtcNow}");
            // Replace Console.WriteLine with actual database or file logging
        }
    }
}