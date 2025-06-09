using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MServer.Models;

namespace MServer.Services.Auth
{
    public class TokenManagementService
    {
        private readonly string secretKey;
        private readonly IAuditLoggingService auditLoggingService;

        public TokenManagementService
        (IAuditLoggingService auditLoggingService,
            string secretKey)
        {
            this.secretKey = secretKey ??
            throw new ArgumentNullException(nameof(secretKey));
            this.auditLoggingService = auditLoggingService ??
            throw new ArgumentNullException(nameof(auditLoggingService));
        }

        public string IssueToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(secretKey);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("sub", user.Id.ToString()),
                    new Claim("name", user.Name),
                    new Claim("phone", user.Phone),
                    new Claim("role", user.FaceId)
                }),
                Expires = DateTime.UtcNow.AddHours(12),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public void LogTokenRefresh(Guid userId, string newToken)
        {
            auditLoggingService.LogAuthenticationAttempt
            (userId, true, new { NewToken = newToken });
        }
    }
}