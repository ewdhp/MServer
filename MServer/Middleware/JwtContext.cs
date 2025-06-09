using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

public class JwtMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtMiddleware> _logger;
    private readonly FingerprintService _fingerprintService;

    public JwtMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<JwtMiddleware> logger,
        FingerprintService fingerprintService)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
        _fingerprintService = fingerprintService;
    }

    public async Task Invoke(HttpContext context)
    {
        var token = context.Request.Headers["Authorization"]
        .FirstOrDefault()?.Split(" ").Last();

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning
            ("Authorization header is missing or does not contain a token.");
            await _next(context); // Allow the request to proceed without a token
            return;
        }

        _logger.LogInformation("Token found in request");

        try
        {
            AttachUserToContext(context, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token validation failed");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return; // Stop further processing
        }

        await _next(context);
    }

    private void AttachUserToContext(HttpContext context, string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);

        if (string.IsNullOrEmpty
        (_configuration["Jwt:Key"]) ||
        _configuration["Jwt:Key"].Length < 32)
        {
            throw new ArgumentException
            ("JWT Key must be at least 32 characters long.");
        }

        tokenHandler.ValidateToken(token,
        new TokenValidationParameters
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
        var userId = jwtToken.Claims.FirstOrDefault
        (x => x.Type == "Id")?.Value;
        var fingerprint = jwtToken.Claims.FirstOrDefault
        (x => x.Type == "Fingerprint")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Id claim not found in token.");
            throw new SecurityTokenException("Id claim not found in token.");
        }

        var requestFingerprint = _fingerprintService.GenerateFingerprint(context);
        _logger.LogInformation("Token Claims: {Claims}",
        string.Join(", ", jwtToken.Claims.Select(c => $"{c.Type}: {c.Value}")));
        _logger.LogInformation("Token Fingerprint: {TokenFingerprint}, " +
        " Request Fingerprint: {RequestFingerprint}", fingerprint, requestFingerprint);

        if (fingerprint != requestFingerprint)
        {
            _logger.LogWarning("Invalid fingerprint. Expected: " +
            " {Expected}, Actual: {Actual}", fingerprint, requestFingerprint);
            throw new SecurityTokenException("Invalid fingerprint");
        }

        _logger.LogInformation("Token validated successfully. Id: {Id}", userId);
        context.Items["UserId"] = userId;
    }
}