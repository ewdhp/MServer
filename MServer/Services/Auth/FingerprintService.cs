using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System;

namespace MServer.Services.Auth
{
    public class FingerprintService
    {
        public string GenerateFingerprint(HttpContext context)
        {
            // Retrieve IP address
            var ipAddress = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ??
                            context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            // Retrieve User-Agent
            var userAgent = context.Request.Headers["User-Agent"].ToString();

            // Combine IP and User-Agent
            var fingerprintData = $"{ipAddress}-{userAgent}";

            // Hash the fingerprint data
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(fingerprintData);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}
