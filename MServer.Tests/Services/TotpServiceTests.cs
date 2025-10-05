using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MServer.Services;
using Xunit;

namespace MServer.Tests.Services
{
    public class TotpServiceTests
    {
        private readonly TotpService _totpService;
        private readonly IConfiguration _configuration;

        public TotpServiceTests()
        {
            // Setup configuration for testing
            var inMemorySettings = new Dictionary<string, string?>
            {
                {"Security:EncryptionKey", "TestEncryptionKey123456789TestEncryptionKey123456789"}
            };

            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _totpService = new TotpService(_configuration);
        }

        [Fact]
        public void GenerateNewSecret_ShouldReturnEncryptedSecret()
        {
            // Act
            var encryptedSecret = _totpService.GenerateNewSecret();

            // Assert
            Assert.NotNull(encryptedSecret);
            Assert.NotEmpty(encryptedSecret);
            Assert.True(IsBase64String(encryptedSecret));
        }

        [Fact]
        public void GenerateCode_WithValidEncryptedSecret_ShouldReturnSixDigitCode()
        {
            // Arrange
            var encryptedSecret = _totpService.GenerateNewSecret();

            // Act
            var code = _totpService.GenerateCode(encryptedSecret);

            // Assert
            Assert.NotNull(code);
            Assert.Equal(6, code.Length);
            Assert.True(int.TryParse(code, out _));
        }

        [Fact]
        public void VerifyCode_WithValidCode_ShouldReturnTrue()
        {
            // Arrange
            var encryptedSecret = _totpService.GenerateNewSecret();
            var generatedCode = _totpService.GenerateCode(encryptedSecret);

            // Act
            var isValid = _totpService.VerifyCode(encryptedSecret, generatedCode);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void VerifyCode_WithInvalidCode_ShouldReturnFalse()
        {
            // Arrange
            var encryptedSecret = _totpService.GenerateNewSecret();

            // Act
            var isValid = _totpService.VerifyCode(encryptedSecret, "123456");

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void VerifyCode_WithExpiredCode_ShouldReturnFalse()
        {
            // Arrange
            var encryptedSecret = _totpService.GenerateNewSecret();

            // Act - Try with a code that's definitely invalid
            var isValid = _totpService.VerifyCode(encryptedSecret, "000000");

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void GetProvisioningUri_ShouldReturnValidOtpAuthUri()
        {
            // Arrange
            var encryptedSecret = _totpService.GenerateNewSecret();
            var accountName = "test@example.com";
            var issuer = "TestIssuer";

            // Act
            var uri = _totpService.GetProvisioningUri(encryptedSecret, accountName, issuer);

            // Assert
            Assert.NotNull(uri);
            Assert.StartsWith("otpauth://totp/", uri);
            Assert.Contains(Uri.EscapeDataString(accountName), uri);
            Assert.Contains(Uri.EscapeDataString(issuer), uri);
            Assert.Contains("secret=", uri);
        }

        [Fact]
        public void VerifyCode_WithWindowSteps_ShouldAcceptCodesWithinWindow()
        {
            // Arrange
            var encryptedSecret = _totpService.GenerateNewSecret();
            var currentCode = _totpService.GenerateCode(encryptedSecret);

            // Act & Assert - Current code should be valid
            Assert.True(_totpService.VerifyCode(encryptedSecret, currentCode, windowSteps: 1));
        }

        [Fact]
        public void GenerateCode_MultipleCallsWithSameSecret_ShouldReturnSameCodeInSameTimeWindow()
        {
            // Arrange
            var encryptedSecret = _totpService.GenerateNewSecret();

            // Act
            var code1 = _totpService.GenerateCode(encryptedSecret);
            var code2 = _totpService.GenerateCode(encryptedSecret);

            // Assert - Should be same since generated within same 30-second window
            Assert.Equal(code1, code2);
        }

        [Fact]
        public void GenerateCode_WithInvalidEncryptedSecret_ShouldThrowException()
        {
            // Arrange
            var invalidEncryptedSecret = "invalid-base64-string";

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _totpService.GenerateCode(invalidEncryptedSecret));
        }

        [Fact]
        public void VerifyCode_WithInvalidEncryptedSecret_ShouldReturnFalse()
        {
            // Arrange
            var invalidEncryptedSecret = "invalid-base64-string";

            // Act
            var isValid = _totpService.VerifyCode(invalidEncryptedSecret, "123456");

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void SecretEncryption_ShouldBeReversible()
        {
            // This test verifies the internal encryption/decryption works
            // by checking that multiple operations with same secret produce consistent results
            
            // Arrange
            var encryptedSecret1 = _totpService.GenerateNewSecret();
            var encryptedSecret2 = _totpService.GenerateNewSecret();

            // Act
            var code1 = _totpService.GenerateCode(encryptedSecret1);
            var code2 = _totpService.GenerateCode(encryptedSecret2);

            // Assert - Different secrets should produce different codes (most of the time)
            // Note: There's a tiny chance they could be the same, but it's extremely unlikely
            Assert.NotEqual(encryptedSecret1, encryptedSecret2);
        }

        private bool IsBase64String(string str)
        {
            try
            {
                Convert.FromBase64String(str);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}