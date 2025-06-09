#nullable enable
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MServer.Services.Auth
{
    public class TokenValidationService
    {
        private readonly string secretKey;

        public TokenValidationService(IConfiguration configuration)
        {
            // Retrieve the secret key from appsettings.json
            secretKey = configuration["Jwt:Key"] ?? throw new
            ArgumentNullException("Jwt:Key is missing in configuration.");
        }

        public (bool IsValid, ClaimsPrincipal? Claims) ValidateToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(secretKey);

            try
            {
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero // Avoid extra validity buffer
                };

                // Validate token
                var claimsPrincipal = tokenHandler
                .ValidateToken(token, validationParameters, out _);

                // If no exception is thrown, the token is valid
                return (true, claimsPrincipal);
            }
            catch (Exception)
            {
                // Return invalid result if token validation fails
                return (false, null);
            }
        }
    }
}