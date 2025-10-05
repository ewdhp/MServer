using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MServer.TestClient
{
    public class TotpFlowTestClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public TotpFlowTestClient(string baseUrl = "http://localhost:5000")
        {
            _baseUrl = baseUrl;
            _httpClient = new HttpClient();
        }

        public async Task<bool> TestCompleteFlow()
        {
            Console.WriteLine("🧪 Testing Complete TOTP Flow");
            Console.WriteLine("============================");

            try
            {
                // Step 1: Test encrypted command setup
                Console.WriteLine("\n📋 Step 1: Testing Google Authenticator Setup");
                var setupResult = await TestGoogleAuthSetup("test-account-" + DateTime.Now.Ticks);

                if (setupResult.Success)
                {
                    Console.WriteLine($"✅ Setup Success: {setupResult.Message}");
                    Console.WriteLine($"📱 QR Code URL: {setupResult.QrCodeUrl}");

                    // Step 2: Test code generation
                    Console.WriteLine("\n📋 Step 2: Testing Code Generation");
                    var codeResult = await TestCodeGeneration(setupResult.AccountName);

                    if (codeResult.Success)
                    {
                        Console.WriteLine($"✅ Code Generated: {codeResult.Code}");

                        // Step 3: Test code verification
                        Console.WriteLine("\n📋 Step 3: Testing Code Verification");
                        var verifyResult = await TestCodeVerification(setupResult.AccountName, codeResult.Code);

                        if (verifyResult.Success)
                        {
                            Console.WriteLine($"✅ Code Verification: {(verifyResult.Valid ? "VALID" : "INVALID")}");
                            return verifyResult.Valid;
                        }
                        else
                        {
                            Console.WriteLine($"❌ Verification Failed: {verifyResult.Error}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ Code Generation Failed: {codeResult.Error}");
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Setup Failed: {setupResult.Error}");
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test Error: {ex.Message}");
                return false;
            }
        }

        private async Task<SetupResult> TestGoogleAuthSetup(string accountName)
        {
            try
            {
                var request = new
                {
                    AccountName = accountName,
                    UserId = "test-user"
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/SecureTotp/secure-setup", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<SetupResult>(responseContent, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    return result ?? new SetupResult { Success = false, Error = "Failed to deserialize response" };
                }
                else
                {
                    return new SetupResult 
                    { 
                        Success = false, 
                        Error = $"HTTP {response.StatusCode}: {responseContent}" 
                    };
                }
            }
            catch (Exception ex)
            {
                return new SetupResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<CodeResult> TestCodeGeneration(string accountName)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/SecureTotp/generate-code/{Uri.EscapeDataString(accountName)}");
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<CodeResult>(responseContent, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    return result ?? new CodeResult { Success = false, Error = "Failed to deserialize response" };
                }
                else
                {
                    return new CodeResult 
                    { 
                        Success = false, 
                        Error = $"HTTP {response.StatusCode}: {responseContent}" 
                    };
                }
            }
            catch (Exception ex)
            {
                return new CodeResult { Success = false, Error = ex.Message };
            }
        }

        private async Task<VerifyResult> TestCodeVerification(string accountName, string code)
        {
            try
            {
                var request = new
                {
                    AccountName = accountName,
                    Code = code
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/SecureTotp/verify-code", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<VerifyResult>(responseContent, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    return result ?? new VerifyResult { Success = false, Error = "Failed to deserialize response" };
                }
                else
                {
                    return new VerifyResult 
                    { 
                        Success = false, 
                        Error = $"HTTP {response.StatusCode}: {responseContent}" 
                    };
                }
            }
            catch (Exception ex)
            {
                return new VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class SetupResult
    {
        public string QrCodeUrl { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public class CodeResult
    {
        public string Code { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; } = true;
        public string Error { get; set; } = string.Empty;
    }

    public class VerifyResult
    {
        public bool Valid { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public bool Success { get; set; } = true;
        public string Error { get; set; } = string.Empty;
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var testClient = new TotpFlowTestClient();
            
            try
            {
                var success = await testClient.TestCompleteFlow();
                
                Console.WriteLine("\n🎯 FINAL RESULT");
                Console.WriteLine("================");
                Console.WriteLine(success ? "✅ ALL TESTS PASSED" : "❌ TESTS FAILED");
                
                Environment.Exit(success ? 0 : 1);
            }
            finally
            {
                testClient.Dispose();
            }
        }
    }
}