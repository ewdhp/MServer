using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using System.Net.Http;
using Xunit;
using MServer.Controllers;
using MServer.Services;
using Microsoft.Extensions.Configuration;

namespace MServer.Tests.Controllers
{
    public class SecureTotpControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public SecureTotpControllerTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task Test_ShouldReturnSuccessMessage()
        {
            // Act
            var response = await _client.GetAsync("/api/SecureTotp/test");

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("TOTP Controller is working", content);
        }

        [Fact]
        public async Task SecureSetup_WithValidRequest_ShouldReturnQrCodeUrl()
        {
            // Arrange
            var request = new SecureSetupRequest
            {
                AccountName = "test@example.com",
                UserId = "testuser"
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await _client.PostAsync("/api/SecureTotp/secure-setup", content);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            var setupResponse = JsonConvert.DeserializeObject<SecureSetupResponse>(responseContent);

            Assert.NotNull(setupResponse);
            Assert.True(setupResponse.Success);
            Assert.NotEmpty(setupResponse.QrCodeUrl);
            Assert.Equal(request.AccountName, setupResponse.AccountName);
            Assert.StartsWith("otpauth://totp/", setupResponse.QrCodeUrl);
        }

        [Fact]
        public async Task SecureSetup_WithEmptyAccountName_ShouldReturnBadRequest()
        {
            // Arrange
            var request = new SecureSetupRequest
            {
                AccountName = "",
                UserId = "testuser"
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await _client.PostAsync("/api/SecureTotp/secure-setup", content);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GenerateCode_WithValidAccount_ShouldReturnCode()
        {
            // Arrange - First setup an account
            var setupRequest = new SecureSetupRequest
            {
                AccountName = "testaccount@example.com",
                UserId = "testuser"
            };

            var setupJson = JsonConvert.SerializeObject(setupRequest);
            var setupContent = new StringContent(setupJson, Encoding.UTF8, "application/json");
            await _client.PostAsync("/api/SecureTotp/secure-setup", setupContent);

            // Act
            var response = await _client.GetAsync("/api/SecureTotp/generate-code/testaccount@example.com");

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(content);

            Assert.NotNull(result.code);
            Assert.Equal(6, ((string)result.code).Length);
            Assert.True(int.TryParse((string)result.code, out _));
        }

        [Fact]
        public async Task GenerateCode_WithNonExistentAccount_ShouldReturnNotFound()
        {
            // Act
            var response = await _client.GetAsync("/api/SecureTotp/generate-code/nonexistent@example.com");

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task VerifyCode_WithValidCode_ShouldReturnValid()
        {
            // Arrange - Setup account and get current code
            var setupRequest = new SecureSetupRequest
            {
                AccountName = "verify@example.com",
                UserId = "testuser"
            };

            var setupJson = JsonConvert.SerializeObject(setupRequest);
            var setupContent = new StringContent(setupJson, Encoding.UTF8, "application/json");
            await _client.PostAsync("/api/SecureTotp/secure-setup", setupContent);

            // Get current code
            var codeResponse = await _client.GetAsync("/api/SecureTotp/generate-code/verify@example.com");
            codeResponse.EnsureSuccessStatusCode();
            var codeContent = await codeResponse.Content.ReadAsStringAsync();
            dynamic codeResult = JsonConvert.DeserializeObject(codeContent);
            var currentCode = (string)codeResult.code;

            // Verify the code
            var verifyRequest = new VerifyCodeRequest
            {
                AccountName = "verify@example.com",
                Code = currentCode
            };

            var verifyJson = JsonConvert.SerializeObject(verifyRequest);
            var verifyContent = new StringContent(verifyJson, Encoding.UTF8, "application/json");

            // Act
            var response = await _client.PostAsync("/api/SecureTotp/verify-code", verifyContent);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(responseContent);

            Assert.True((bool)result.valid);
        }

        [Fact]
        public async Task VerifyCode_WithInvalidCode_ShouldReturnInvalid()
        {
            // Arrange - Setup account
            var setupRequest = new SecureSetupRequest
            {
                AccountName = "invalid@example.com",
                UserId = "testuser"
            };

            var setupJson = JsonConvert.SerializeObject(setupRequest);
            var setupContent = new StringContent(setupJson, Encoding.UTF8, "application/json");
            await _client.PostAsync("/api/SecureTotp/secure-setup", setupContent);

            // Try to verify with invalid code
            var verifyRequest = new VerifyCodeRequest
            {
                AccountName = "invalid@example.com",
                Code = "000000"
            };

            var verifyJson = JsonConvert.SerializeObject(verifyRequest);
            var verifyContent = new StringContent(verifyJson, Encoding.UTF8, "application/json");

            // Act
            var response = await _client.PostAsync("/api/SecureTotp/verify-code", verifyContent);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(responseContent);

            Assert.False((bool)result.valid);
        }

        [Fact]
        public async Task VerifyCode_WithNonExistentAccount_ShouldReturnNotFound()
        {
            // Arrange
            var verifyRequest = new VerifyCodeRequest
            {
                AccountName = "doesnotexist@example.com",
                Code = "123456"
            };

            var verifyJson = JsonConvert.SerializeObject(verifyRequest);
            var verifyContent = new StringContent(verifyJson, Encoding.UTF8, "application/json");

            // Act
            var response = await _client.PostAsync("/api/SecureTotp/verify-code", verifyContent);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task CompleteWorkflow_SetupGenerateAndVerify_ShouldWork()
        {
            // This test demonstrates the complete OTP workflow
            var accountName = $"workflow@example.com";
            
            // Step 1: Setup TOTP
            var setupRequest = new SecureSetupRequest
            {
                AccountName = accountName,
                UserId = "testuser"
            };

            var setupJson = JsonConvert.SerializeObject(setupRequest);
            var setupContent = new StringContent(setupJson, Encoding.UTF8, "application/json");
            var setupResponse = await _client.PostAsync("/api/SecureTotp/secure-setup", setupContent);
            setupResponse.EnsureSuccessStatusCode();

            var setupResponseContent = await setupResponse.Content.ReadAsStringAsync();
            var setup = JsonConvert.DeserializeObject<SecureSetupResponse>(setupResponseContent);

            // Verify setup response
            Assert.True(setup.Success);
            Assert.NotEmpty(setup.QrCodeUrl);
            Assert.StartsWith("otpauth://totp/", setup.QrCodeUrl);

            // Step 2: Generate current TOTP code
            var codeResponse = await _client.GetAsync($"/api/SecureTotp/generate-code/{accountName}");
            codeResponse.EnsureSuccessStatusCode();

            var codeContent = await codeResponse.Content.ReadAsStringAsync();
            dynamic codeResult = JsonConvert.DeserializeObject(codeContent);
            var currentCode = (string)codeResult.code;

            // Verify code format
            Assert.Equal(6, currentCode.Length);
            Assert.True(int.TryParse(currentCode, out _));

            // Step 3: Verify the generated code
            var verifyRequest = new VerifyCodeRequest
            {
                AccountName = accountName,
                Code = currentCode
            };

            var verifyJson = JsonConvert.SerializeObject(verifyRequest);
            var verifyContent = new StringContent(verifyJson, Encoding.UTF8, "application/json");
            var verifyResponse = await _client.PostAsync("/api/SecureTotp/verify-code", verifyContent);
            verifyResponse.EnsureSuccessStatusCode();

            var verifyResponseContent = await verifyResponse.Content.ReadAsStringAsync();
            dynamic verifyResult = JsonConvert.DeserializeObject(verifyResponseContent);

            // Verify that verification was successful
            Assert.True((bool)verifyResult.valid);
        }
    }
}