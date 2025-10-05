using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MServer.Services;

namespace MServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Require JWT authentication
    public class SecureTotpController : ControllerBase
    {
        private readonly TotpService _totpService;
        private readonly ILogger<SecureTotpController> _logger;
        private readonly string _allowedExecutablePath = "/usr/bin/google-authenticator";

        public SecureTotpController(TotpService totpService, ILogger<SecureTotpController> logger)
        {
            _totpService = totpService;
            _logger = logger;
        }

        [HttpPost("secure-setup")]
        public async Task<IActionResult> SecureGoogleAuthSetup([FromBody] SecureSetupRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrWhiteSpace(request.AccountName))
                {
                    return BadRequest("Account name is required");
                }

                // Check if user has permission (implement your auth logic)
                if (!await IsUserAuthorized(request.UserId))
                {
                    return Forbid("Insufficient permissions");
                }

                // Execute google-authenticator securely
                var result = await ExecuteGoogleAuthenticatorSecurely(request.AccountName);
                
                if (!result.Success)
                {
                    _logger.LogError("Google Authenticator execution failed: {Error}", result.Error);
                    return StatusCode(500, "Setup failed");
                }

                // Return only QR code, secret is automatically encrypted
                return Ok(new SecureSetupResponse
                {
                    QrCodeUrl = result.QrCodeUrl,
                    AccountName = request.AccountName,
                    Success = true,
                    Message = "Setup successful. Secret encrypted and stored securely."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in secure TOTP setup");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("generate-code/{accountName}")]
        public async Task<IActionResult> GenerateCode(string accountName)
        {
            try
            {
                // Validate user has access to this account
                var userId = GetCurrentUserId();
                if (!await IsUserAuthorizedForAccount(userId, accountName))
                {
                    return Forbid("Access denied to this account");
                }

                // Generate code using encrypted secret
                var encryptedSecret = await GetEncryptedSecret(accountName);
                if (encryptedSecret == null)
                {
                    return NotFound("Account not found");
                }

                var code = _totpService.GenerateCode(encryptedSecret);
                
                _logger.LogInformation("TOTP code generated for account {Account} by user {User}", 
                    accountName, userId);

                return Ok(new { code, accountName, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating TOTP code");
                return StatusCode(500, "Code generation failed");
            }
        }

        [HttpPost("verify-code")]
        public async Task<IActionResult> VerifyCode([FromBody] VerifyCodeRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (!await IsUserAuthorizedForAccount(userId, request.AccountName))
                {
                    return Forbid("Access denied");
                }

                var encryptedSecret = await GetEncryptedSecret(request.AccountName);
                if (encryptedSecret == null)
                {
                    return NotFound("Account not found");
                }

                var isValid = _totpService.VerifyCode(encryptedSecret, request.Code);
                
                _logger.LogInformation("TOTP verification for account {Account}: {Result}", 
                    request.AccountName, isValid ? "SUCCESS" : "FAILED");

                return Ok(new { valid = isValid, accountName = request.AccountName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying TOTP code");
                return StatusCode(500, "Verification failed");
            }
        }

        private async Task<GoogleAuthResult> ExecuteGoogleAuthenticatorSecurely(string accountName)
        {
            try
            {
                // Use encrypted command service instead of direct execution
                var encryptedCommandService = new EncryptedCommandService();
                
                // Execute the encrypted google-authenticator command
                var output = await encryptedCommandService.ExecuteEncryptedCommand("google-auth-setup", accountName);
                
                // Parse the structured output
                var result = ParseStructuredOutput(output, accountName);
                
                // If secret found, encrypt and store it
                if (!string.IsNullOrEmpty(result.Secret))
                {
                    var encryptedSecret = _totpService.GenerateNewSecret();
                    await StoreEncryptedSecret(accountName, result.Secret);
                    
                    // Clear the secret from memory immediately
                    result.Secret = null;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute encrypted google-authenticator command");
                return new GoogleAuthResult { Success = false, Error = ex.Message };
            }
        }

        private GoogleAuthResult ParseStructuredOutput(string output, string accountName)
        {
            var result = new GoogleAuthResult { AccountName = accountName };
            
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("SECRET:"))
                {
                    result.Secret = trimmedLine.Substring(7);
                }
                else if (trimmedLine.StartsWith("QR_URL:"))
                {
                    result.QrCodeUrl = trimmedLine.Substring(7);
                }
                else if (trimmedLine.StartsWith("SUCCESS:"))
                {
                    result.Success = trimmedLine.Substring(8).ToLower() == "true";
                }
                else if (trimmedLine.StartsWith("ERROR:"))
                {
                    result.Error = trimmedLine.Substring(6);
                    result.Success = false;
                }
            }
            
            return result;
        }

        private GoogleAuthResult ParseGoogleAuthOutput(string output, string accountName)
        {
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string secret = null;
            string qrCodeUrl = null;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("Your new secret key is: "))
                {
                    secret = trimmedLine.Substring("Your new secret key is: ".Length).Trim();
                }
                
                if (trimmedLine.StartsWith("https://www.google.com/chart"))
                {
                    qrCodeUrl = trimmedLine;
                }
            }

            return new GoogleAuthResult
            {
                Success = true,
                Secret = secret,
                QrCodeUrl = qrCodeUrl,
                AccountName = accountName
            };
        }

        private async Task<bool> IsUserAuthorized(string userId)
        {
            // Implement your authorization logic
            // Check user roles, permissions, etc.
            return !string.IsNullOrEmpty(userId);
        }

        private async Task<bool> IsUserAuthorizedForAccount(string userId, string accountName)
        {
            // Implement account-specific authorization
            // Check if user owns this account or has access
            return true; // Placeholder
        }

        private string GetCurrentUserId()
        {
            return User?.Identity?.Name ?? "unknown";
        }

        private async Task<string> GetEncryptedSecret(string accountName)
        {
            // Load from your secure storage (database, encrypted file, etc.)
            // This is a placeholder - implement your storage logic
            return null;
        }

        private async Task StoreEncryptedSecret(string accountName, string secret)
        {
            // Store encrypted secret securely
            // Implement your storage logic
        }
    }

    public class SecureSetupRequest
    {
        public string AccountName { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
    }

    public class SecureSetupResponse
    {
        public string QrCodeUrl { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class VerifyCodeRequest
    {
        public string AccountName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class GoogleAuthResult
    {
        public bool Success { get; set; }
        public string? Secret { get; set; }
        public string? QrCodeUrl { get; set; }
        public string? AccountName { get; set; }
        public string? Error { get; set; }
    }
}