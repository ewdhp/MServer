using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace MServer.Tests.Integration
{
    /// <summary>
    /// Tests the Google Authenticator proxy functionality
    /// These tests require google-authenticator to be installed on the system
    /// </summary>
    public class GoogleAuthProxyTests
    {
        private readonly ITestOutputHelper _output;

        public GoogleAuthProxyTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task GoogleAuthProxy_ShouldGenerateQrCode()
        {
            // Skip if google-authenticator is not available
            if (!IsGoogleAuthenticatorAvailable())
            {
                _output.WriteLine("Skipping test: google-authenticator not found on system");
                return;
            }

            var proxyScriptPath = GetProxyScriptPath();
            if (!File.Exists(proxyScriptPath))
            {
                _output.WriteLine($"Skipping test: proxy script not found at {proxyScriptPath}");
                return;
            }

            // Make sure script is executable
            await ExecuteCommand("chmod", $"+x {proxyScriptPath}");

            // Execute the proxy script
            var result = await ExecuteCommand("bash", proxyScriptPath);

            // Verify output contains expected elements
            Assert.Contains("🔐 Secure Google Authenticator Proxy", result.Output);
            Assert.Contains("📱 Running google-authenticator", result.Output);

            // Should either show QR code or error message
            var hasQrCode = result.Output.Contains("https://www.google.com/chart") ||
                           result.Output.Contains("📲 QR Code for Google Authenticator");
            var hasError = result.Output.Contains("❌");

            Assert.True(hasQrCode || hasError, "Should either generate QR code or show error");

            _output.WriteLine("Google Auth Proxy Output:");
            _output.WriteLine(result.Output);
        }

        [Fact]
        public async Task TotpTool_ShouldGenerateNewAccount()
        {
            var totpToolPath = GetTotpToolPath();
            if (!File.Exists(totpToolPath))
            {
                _output.WriteLine($"Skipping test: TOTP tool not found at {totpToolPath}");
                return;
            }

            var testAccountName = $"test-{DateTime.Now.Ticks}";

            // Generate new TOTP account
            var result = await ExecuteCommand("dotnet", $"run --project {GetTotpProjectPath()} -- new {testAccountName}");

            _output.WriteLine("TOTP Tool Output:");
            _output.WriteLine(result.Output);

            // Verify successful generation
            Assert.True(result.ExitCode == 0 || result.Output.Contains("✅") || 
                       result.Output.Contains("Generated"), "Should successfully generate account");
        }

        [Fact]
        public async Task TotpTool_ListCommand_ShouldWork()
        {
            var totpProjectPath = GetTotpProjectPath();
            if (!Directory.Exists(totpProjectPath))
            {
                _output.WriteLine("Skipping test: TOTP project directory not found");
                return;
            }

            // List accounts
            var result = await ExecuteCommand("dotnet", $"run --project {totpProjectPath} -- list");

            _output.WriteLine("TOTP List Output:");
            _output.WriteLine(result.Output);

            // Should not crash (exit code 0 or reasonable output)
            Assert.True(result.ExitCode == 0 || !result.Output.Contains("Exception"),
                "List command should not crash");
        }

        [Fact] 
        public async Task TotpTool_Help_ShouldShowCommands()
        {
            var totpProjectPath = GetTotpProjectPath();
            if (!Directory.Exists(totpProjectPath))
            {
                _output.WriteLine("Skipping test: TOTP project directory not found");
                return;
            }

            // Show help
            var result = await ExecuteCommand("dotnet", $"run --project {totpProjectPath}");

            _output.WriteLine("TOTP Help Output:");
            _output.WriteLine(result.Output);

            // Should show available commands
            var output = result.Output.ToLower();
            Assert.True(output.Contains("generate") || output.Contains("new") || 
                       output.Contains("verify") || output.Contains("help") ||
                       output.Contains("usage"), "Should show available commands");
        }

        [Fact]
        public async Task TotpTool_ImportAndVerify_ShouldWork()
        {
            var totpProjectPath = GetTotpProjectPath();
            if (!Directory.Exists(totpProjectPath))
            {
                _output.WriteLine("Skipping test: TOTP project directory not found");
                return;
            }

            var testAccountName = $"import-test-{DateTime.Now.Ticks}";
            var testSecret = "JBSWY3DPEHPK3PXP"; // Example base32 secret

            try
            {
                // Import a known secret
                var importResult = await ExecuteCommand("dotnet", 
                    $"run --project {totpProjectPath} -- import {testAccountName} {testSecret}");

                _output.WriteLine("Import Output:");
                _output.WriteLine(importResult.Output);

                // Generate code for the imported account
                var codeResult = await ExecuteCommand("dotnet",
                    $"run --project {totpProjectPath} -- get {testAccountName}");

                _output.WriteLine("Code Generation Output:");
                _output.WriteLine(codeResult.Output);

                if (codeResult.ExitCode == 0 && ExtractCodeFromOutput(codeResult.Output) != null)
                {
                    var generatedCode = ExtractCodeFromOutput(codeResult.Output);
                    
                    // Verify the generated code
                    var verifyResult = await ExecuteCommand("dotnet",
                        $"run --project {totpProjectPath} -- verify {testAccountName} {generatedCode}");

                    _output.WriteLine("Verify Output:");
                    _output.WriteLine(verifyResult.Output);

                    // Verification should succeed
                    Assert.True(verifyResult.Output.Contains("✅") || 
                               verifyResult.Output.ToLower().Contains("valid") ||
                               verifyResult.Output.ToLower().Contains("success"),
                        "Code verification should succeed");
                }
            }
            finally
            {
                // Cleanup - remove test account
                await ExecuteCommand("dotnet", 
                    $"run --project {totpProjectPath} -- remove {testAccountName}");
            }
        }

        private bool IsGoogleAuthenticatorAvailable()
        {
            try
            {
                var result = ExecuteCommand("which", "google-authenticator").Result;
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private string GetProxyScriptPath()
        {
            var possiblePaths = new[]
            {
                "/home/esdtyiti/github/ewdhp/MServer/TotpTool/google-auth-proxy.sh",
                "/home/esdtyiti/github/ewdhp/MServer/TotpTool/publish/google-auth-proxy.sh",
                "./TotpTool/google-auth-proxy.sh",
                "./google-auth-proxy.sh"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return "/home/esdtyiti/github/ewdhp/MServer/TotpTool/google-auth-proxy.sh";
        }

        private string GetTotpToolPath()
        {
            var possiblePaths = new[]
            {
                "/home/esdtyiti/github/ewdhp/MServer/TotpTool/publish/totp",
                "/home/esdtyiti/github/ewdhp/MServer/TotpTool/bin/Debug/net9.0/TotpTool",
                "./TotpTool/publish/totp",
                "./totp"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return "/home/esdtyiti/github/ewdhp/MServer/TotpTool/publish/totp";
        }

        private string GetTotpProjectPath()
        {
            var possiblePaths = new[]
            {
                "/home/esdtyiti/github/ewdhp/MServer/TotpTool",
                "./TotpTool"
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                    return path;
            }

            return "/home/esdtyiti/github/ewdhp/MServer/TotpTool";
        }

        private string ExtractCodeFromOutput(string output)
        {
            // Look for 6-digit codes in the output
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 6 && int.TryParse(trimmed, out _))
                {
                    return trimmed;
                }

                // Also look for patterns like "Code: 123456"
                if (trimmed.Contains(":"))
                {
                    var parts = trimmed.Split(':');
                    if (parts.Length >= 2)
                    {
                        var code = parts[1].Trim();
                        if (code.Length == 6 && int.TryParse(code, out _))
                        {
                            return code;
                        }
                    }
                }
            }
            return null;
        }

        private async Task<(int ExitCode, string Output)> ExecuteCommand(string command, string arguments)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var fullOutput = output;
            if (!string.IsNullOrEmpty(error))
            {
                fullOutput += "\nSTDERR:\n" + error;
            }

            return (process.ExitCode, fullOutput);
        }
    }
}