using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OtpNet;

namespace MServer.Tests.Manual
{
    /// <summary>
    /// Manual test client to demonstrate complete OTP functionality.
    /// This simulates a real-world usage scenario.
    /// </summary>
    public class ManualOtpTestClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public ManualOtpTestClient(string baseUrl = "https://localhost:5001")
        {
            _baseUrl = baseUrl;
            
            // For self-signed certificates in development
            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _httpClient = new HttpClient(handler);
        }

        public async Task RunCompleteWorkflowDemo()
        {
            Console.WriteLine("🔐 MServer OTP Service - Complete Workflow Demo");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine();

            try
            {
                // Step 1: Test basic connectivity
                Console.WriteLine("Step 1: Testing server connectivity...");
                await TestServerConnection();

                // Step 2: Setup new TOTP account
                Console.WriteLine("\nStep 2: Setting up new TOTP account...");
                var accountName = $"demo-user-{DateTime.Now.Ticks}@example.com";
                var qrCodeUrl = await SetupNewAccount(accountName);

                // Step 3: Display QR code information
                Console.WriteLine("\nStep 3: QR Code generated for Google Authenticator");
                Console.WriteLine($"Account: {accountName}");
                Console.WriteLine($"QR Code URL: {qrCodeUrl}");
                Console.WriteLine();
                Console.WriteLine("📱 In a real scenario, you would:");
                Console.WriteLine("   1. Scan this QR code with Google Authenticator app");
                Console.WriteLine("   2. The app would generate 6-digit codes every 30 seconds");
                Console.WriteLine();

                // Step 4: Simulate server-side code generation
                Console.WriteLine("Step 4: Generating current TOTP code (server-side)...");
                var currentCode = await GenerateCurrentCode(accountName);
                Console.WriteLine($"Current TOTP Code: {currentCode}");
                Console.WriteLine();

                // Step 5: Verify the code
                Console.WriteLine("Step 5: Verifying the generated code...");
                var isValid = await VerifyCode(accountName, currentCode);
                Console.WriteLine($"Verification Result: {(isValid ? "✅ VALID" : "❌ INVALID")}");
                Console.WriteLine();

                // Step 6: Test with invalid code
                Console.WriteLine("Step 6: Testing with invalid code...");
                var invalidResult = await VerifyCode(accountName, "000000");
                Console.WriteLine($"Invalid Code Result: {(invalidResult ? "❌ Unexpected!" : "✅ Correctly rejected")}");
                Console.WriteLine();

                // Step 7: Demonstrate phone simulation
                Console.WriteLine("Step 7: Simulating Google Authenticator app...");
                await SimulatePhoneAuthenticator(qrCodeUrl, accountName);

                Console.WriteLine("🎉 Complete workflow demonstration finished successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during demo: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task TestServerConnection()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/SecureTotp/test");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("✅ Server is running and accessible");
                    Console.WriteLine($"Server response: {content}");
                }
                else
                {
                    Console.WriteLine($"⚠️ Server responded with status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Cannot connect to server: {ex.Message}");
                throw;
            }
        }

        private async Task<string> SetupNewAccount(string accountName)
        {
            var setupRequest = new
            {
                AccountName = accountName,
                UserId = "demo-user"
            };

            var json = JsonConvert.SerializeObject(setupRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/SecureTotp/secure-setup", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Setup failed with status {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(responseContent);

            Console.WriteLine($"✅ Account setup successful");
            Console.WriteLine($"Message: {result.message}");
            
            return result.qrCodeUrl;
        }

        private async Task<string> GenerateCurrentCode(string accountName)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/SecureTotp/generate-code/{Uri.EscapeDataString(accountName)}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Code generation failed with status {response.StatusCode}: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(content);

            return result.code;
        }

        private async Task<bool> VerifyCode(string accountName, string code)
        {
            var verifyRequest = new
            {
                AccountName = accountName,
                Code = code
            };

            var json = JsonConvert.SerializeObject(verifyRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/SecureTotp/verify-code", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Verification failed with status {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(responseContent);

            return result.valid;
        }

        private async Task SimulatePhoneAuthenticator(string qrCodeUrl, string accountName)
        {
            try
            {
                // Extract secret from QR code URL
                var uri = new Uri(qrCodeUrl);
                var queryParams = uri.Query.TrimStart('?').Split('&')
                    .Select(param => param.Split('='))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
                queryParams.TryGetValue("secret", out var secret);

                if (string.IsNullOrEmpty(secret))
                {
                    Console.WriteLine("❌ Could not extract secret from QR code");
                    return;
                }

                Console.WriteLine($"📱 Phone extracted secret from QR code: {secret.Substring(0, 4)}...{secret.Substring(secret.Length - 4)}");

                // Create TOTP generator like phone would
                var secretBytes = Base32Encoding.ToBytes(secret);
                var phoneTotp = new Totp(secretBytes);

                // Generate code on "phone"
                var phoneCode = phoneTotp.ComputeTotp();
                Console.WriteLine($"📱 Phone generated code: {phoneCode}");

                // Verify phone code with server
                var isPhoneCodeValid = await VerifyCode(accountName, phoneCode);
                Console.WriteLine($"📱 Phone code verification: {(isPhoneCodeValid ? "✅ SUCCESS" : "❌ FAILED")}");

                // Show time remaining for current code
                var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var timeStep = 30;
                var timeRemaining = timeStep - (unixTime % timeStep);
                Console.WriteLine($"⏰ Current code expires in {timeRemaining} seconds");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Phone simulation error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        public static async Task Main(string[] args)
        {
            var client = new ManualOtpTestClient();
            await client.RunCompleteWorkflowDemo();
            client.Dispose();

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}