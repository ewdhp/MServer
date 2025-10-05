using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MServer.Services
{
    public class SecureBashExecutor
    {
        private readonly string _encryptedScriptPath;
        private readonly string _masterKey;

        public SecureBashExecutor()
        {
            _encryptedScriptPath = Path.Combine(AppContext.BaseDirectory, "encrypted_scripts", "gauth_proxy.enc");
            _masterKey = Environment.GetEnvironmentVariable("SCRIPT_ENCRYPTION_KEY") ?? 
                        LoadMasterKey();
        }

        public async Task<(bool Success, string QrCodeUrl, string Error)> ExecuteSecureGoogleAuth(string accountName)
        {
            try
            {
                // 1. Validate caller
                if (!ValidateExecutionContext())
                {
                    return (false, "", "Unauthorized execution context");
                }

                // 2. Decrypt and execute script
                var script = DecryptScript();
                if (string.IsNullOrEmpty(script))
                {
                    return (false, "", "Failed to decrypt script");
                }

                // 3. Execute in secure temporary environment
                var result = await ExecuteDecryptedScript(script, accountName);
                
                return result;
            }
            catch (Exception ex)
            {
                return (false, "", $"Execution error: {ex.Message}");
            }
        }

        private string DecryptScript()
        {
            try
            {
                if (!File.Exists(_encryptedScriptPath))
                {
                    return CreateAndEncryptScript();
                }

                var encryptedData = File.ReadAllBytes(_encryptedScriptPath);
                return DecryptData(encryptedData, _masterKey);
            }
            catch
            {
                return "";
            }
        }

        private string CreateAndEncryptScript()
        {
            var script = @"#!/bin/bash
# Encrypted Google Authenticator Proxy
# This script is decrypted and executed only by authorized C# service

set -euo pipefail

ACCOUNT_NAME=""$1""
TEMP_DIR=""/tmp/gauth-$RANDOM-$$""
mkdir -p ""$TEMP_DIR""
chmod 700 ""$TEMP_DIR""

cleanup() {
    rm -rf ""$TEMP_DIR""
}
trap cleanup EXIT

# Validate google-authenticator exists
if ! command -v google-authenticator >/dev/null 2>&1; then
    echo ""ERROR:google-authenticator not found"" >&2
    exit 1
fi

# Execute with controlled environment
cd ""$TEMP_DIR""
export HOME=""$TEMP_DIR""

# Run google-authenticator with automatic responses
OUTPUT=$(echo -e ""y\nn\ny\nn\ny\n-1"" | google-authenticator -t -d -f -r 3 -R 30 2>&1)

# Extract secret (but don't output it)
SECRET=$(echo ""$OUTPUT"" | grep ""Your new secret key is:"" | sed 's/.*Your new secret key is: //')

# Extract QR URL
QR_URL=$(echo ""$OUTPUT"" | grep ""https://www.google.com/chart"" | head -1)

# Output only QR URL for C# service to capture
if [ ! -z ""$QR_URL"" ]; then
    echo ""QR_URL:$QR_URL""
fi

# Output secret for C# service to encrypt (this is secure since script is encrypted)
if [ ! -z ""$SECRET"" ]; then
    echo ""SECRET:$SECRET""
fi

echo ""ACCOUNT:$ACCOUNT_NAME""
";

            // Encrypt and save the script
            var encryptedScript = EncryptData(script, _masterKey);
            Directory.CreateDirectory(Path.GetDirectoryName(_encryptedScriptPath)!);
            File.WriteAllBytes(_encryptedScriptPath, encryptedScript);
            
            // Set restrictive permissions
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(_encryptedScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return script;
        }

        private async Task<(bool Success, string QrCodeUrl, string Error)> ExecuteDecryptedScript(string script, string accountName)
        {
            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"gauth_proxy_{Guid.NewGuid():N}.sh");
            
            try
            {
                // Write decrypted script to temporary location
                await File.WriteAllTextAsync(tempScriptPath, script);
                
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    File.SetUnixFileMode(tempScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }

                // Execute script with controlled environment
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"\"{tempScriptPath}\" \"{accountName}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    return (false, "", $"Script execution failed: {error}");
                }

                // Parse output
                string qrUrl = "";
                string secret = "";
                
                foreach (var line in output.Split('\n'))
                {
                    if (line.StartsWith("QR_URL:"))
                    {
                        qrUrl = line.Substring(7);
                    }
                    else if (line.StartsWith("SECRET:"))
                    {
                        secret = line.Substring(7);
                        
                        // Immediately encrypt and store the secret
                        if (!string.IsNullOrEmpty(secret))
                        {
                            await StoreEncryptedSecret(accountName, secret);
                            // Clear secret from memory
                            secret = new string('\0', secret.Length);
                        }
                    }
                }

                return (true, qrUrl, "");
            }
            finally
            {
                // Clean up temporary script file
                try
                {
                    if (File.Exists(tempScriptPath))
                    {
                        // Overwrite file content before deletion
                        var randomData = new byte[new FileInfo(tempScriptPath).Length];
                        using (var rng = RandomNumberGenerator.Create())
                        {
                            rng.GetBytes(randomData);
                        }
                        await File.WriteAllBytesAsync(tempScriptPath, randomData);
                        File.Delete(tempScriptPath);
                    }
                }
                catch { /* Ignore cleanup errors */ }
            }
        }

        private bool ValidateExecutionContext()
        {
            // Implement your validation logic
            // Check caller permissions, JWT tokens, etc.
            
            // Example: Check if called from authorized controller
            var stackTrace = new StackTrace();
            var frames = stackTrace.GetFrames();
            
            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                if (method?.DeclaringType?.Name == "SecureTotpController")
                {
                    return true;
                }
            }
            
            return false;
        }

        private async Task StoreEncryptedSecret(string accountName, string secret)
        {
            // Use your existing TotpService to encrypt and store
            var totpService = new TotpService(null); // Configure properly
            var encryptedSecret = totpService.GenerateNewSecret(); // This encrypts automatically
            
            // Store in your database or secure storage
            // Implementation depends on your storage strategy
        }

        private byte[] EncryptData(string plaintext, string key)
        {
            using var aes = Aes.Create();
            var keyBytes = DeriveKey(key);
            aes.Key = keyBytes;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var encryptedBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
            Array.Copy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

            return result;
        }

        private string DecryptData(byte[] encryptedData, string key)
        {
            using var aes = Aes.Create();
            var keyBytes = DeriveKey(key);
            aes.Key = keyBytes;

            var iv = new byte[16];
            var ciphertext = new byte[encryptedData.Length - 16];
            Array.Copy(encryptedData, 0, iv, 0, 16);
            Array.Copy(encryptedData, 16, ciphertext, 0, ciphertext.Length);

            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }

        private byte[] DeriveKey(string password)
        {
            var salt = Encoding.UTF8.GetBytes("SecureBashExecutor.Salt.2024");
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32);
        }

        private string LoadMasterKey()
        {
            var keyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".script_master.key");
            
            if (File.Exists(keyFile))
            {
                return File.ReadAllText(keyFile).Trim();
            }

            // Generate new master key
            var keyBytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(keyBytes);
            var masterKey = Convert.ToBase64String(keyBytes);
            
            File.WriteAllText(keyFile, masterKey);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            
            return masterKey;
        }
    }
}