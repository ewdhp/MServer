using System;
using System.Collections.Generic;
namespace MServer.Services.Auth
{
    public class RBACService
    {
        private readonly Dictionary<string, List<string>> _rolePermissions;

        public RBACService()
        {
            // Define roles and their permissions
            _rolePermissions = new Dictionary<string, List<string>>
        {
            { "admin", new List<string> { "create", "read", "update", "delete" } },
            { "user", new List<string> { "read" } },
            { "moderator", new List<string> { "read", "update" } }
        };
        }

        public bool IsAuthorized(string role, string action)
        {
            if (_rolePermissions.ContainsKey(role))
            {
                return _rolePermissions[role].Contains(action);
            }
            return false; // Role not found or action not permitted
        }

        public string GetUserRole(Guid userId)
        {
            // Mock implementation – replace with database integration
            // Fetch role from the database based on userId
            // e.g., SELECT role FROM users WHERE id = @userId
            return "user"; // For simplicity, returning a default role
        }
    }

}
