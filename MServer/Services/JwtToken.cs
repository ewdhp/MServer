using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MServer.Models;
using MServer.Data;
using Microsoft.AspNetCore.Http;

namespace MServer.Services
{
    public interface IJwtTokenService
    {
        void InvalidateToken(string userId);
        bool IsTokenInvalidated(string userId);
        string GenerateJwtToken(string phone, string fingerprint);
        bool IsTokenValid(string token, string currentFingerprint); // Add this method
    }

    public class JwtTokenService : IJwtTokenService
    {
        private readonly UDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<JwtTokenService> _logger;
        private readonly ConcurrentDictionary<string, string> _invalidated = new
        ConcurrentDictionary<string, string>();

        public JwtTokenService(
            IConfiguration configuration,
            ILogger<JwtTokenService> logger,
            UDbContext context)
        {
            _configuration = configuration;
            _logger = logger;
            _context = context;
        }

        public string GenerateJwtToken(string phone, string fingerprint)
        {
            var user = _context.Users.FirstOrDefault(u => u.Phone == phone);
            if (user == null)
                throw new Exception("User not found");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, phone),
                new Claim("PhoneNumber", phone),
                new Claim("Id", user.Id.ToString()),
                new Claim("Fingerprint", fingerprint),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = new SymmetricSecurityKey
            (Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: creds);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            _logger.LogInformation("Token generated for user: {Phone}", phone);

            return tokenString;
        }

        public void InvalidateToken(string userId)
        {
            _invalidated[userId] = userId;
        }

        public bool IsTokenInvalidated(string userId)
        {
            return _invalidated.ContainsKey(userId);
        }

        public bool IsTokenValid(string token, string currentFingerprint)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]);

                var principal = tokenHandler.ValidateToken
                (token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = _configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                var tokenFingerprint = jwtToken.Claims.FirstOrDefault
                (c => c.Type == "Fingerprint")?.Value;

                if (tokenFingerprint == null)
                {
                    _logger.LogWarning("Token does not contain a fingerprint claim.");
                    return false;
                }

                if (tokenFingerprint != currentFingerprint)
                {
                    _logger.LogWarning("Token fingerprint does not match the current fingerprint.");
                    return false;
                }

                _logger.LogInformation("Token is valid and fingerprint matches.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token validation failed.");
                return false;
            }
        }
    }
}